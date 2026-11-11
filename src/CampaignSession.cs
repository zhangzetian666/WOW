using System;
using System.Drawing;

namespace WoWProbe
{
    public interface ICampaignInputBackend:IInputBackend
    {
        Rectangle ClientBounds();bool IsMouseAlreadyDown(bool right);void MoveMouse(Point screen);void MouseDown(bool right);void MouseUp(bool right);
    }
    public sealed class CampaignSession
    {
        readonly object gate=new object();readonly ICampaignInputBackend backend;readonly Func<long> clock;readonly Func<OperationObservation> reader;readonly Action<string> log;readonly InputFailureLatch safety;readonly Func<Rectangle,bool> regionAllowed;
        CampaignPlan plan;CampaignEngine engine;OperationCommand command;bool running,holding,ownsKey,ownsMouse,right,waiting,armed,faulted,ticking,starting;long generation,due,releaseAt,lastTime;int releaseSequence;ushort key;uint layout;
        string status="Idle",reason="";
        public CampaignSession(ICampaignInputBackend backend,Func<long> clock,Func<OperationObservation> reader,Action<string> log,InputFailureLatch safety,Func<Rectangle,bool> regionAllowed=null)
        {if(backend==null||clock==null||reader==null||safety==null)throw new ArgumentNullException("campaign dependencies");this.backend=backend;this.clock=clock;this.reader=reader;this.log=log;this.safety=safety;this.regionAllowed=regionAllowed;}
        public bool IsActive{get{lock(gate)return running;}}public bool IsHolding{get{lock(gate)return holding;}}public string Status{get{lock(gate)return status;}}public string LastReason{get{lock(gate)return reason;}}public string EngineStatus{get{lock(gate)return engine==null?"Idle":engine.Status;}}
        public CampaignCheckpoint CurrentCheckpoint{get{lock(gate)return engine==null?null:engine.CurrentCheckpoint;}}
        public OperationCommand LastCommand{get{lock(gate)return Clone(command);}}
        public void Start(CampaignPlan source)
        {
            lock(gate)
            {
                if(starting||running||ownsKey||ownsMouse)throw new InvalidOperationException("完整流程已有会话。");if(faulted||safety.Faulted)throw new InvalidOperationException("输入故障尚未处理，请手动松开按键/鼠标并重启工具。");starting=true;long ticket=generation;
                try{
                    if(source==null)throw new ArgumentNullException("plan");var p=source.Clone();p.Validate();
                    if(!p.Settings.BindingsConfirmed||!p.Settings.UiConfirmed||!p.Settings.AutoResumeConfirmed||!p.Battle.BindingsConfirmed||!p.Battle.Calibration.Confirmed||(p.Battle.AllowApproach&&!p.Battle.ApproachPositionConfirmed))throw new InvalidOperationException("请先确认地图标定、键位、界面布局、恢复续跑及目标坐标能力。");
                    var o=reader();string problem=FrameProblem(o,p,0);if(problem!=null)throw new InvalidOperationException(problem);var candidate=new CampaignEngine(p);long now=clock();
                    if(ticket!=generation||faulted||safety.Faulted)throw new InvalidOperationException("完整流程启动已被取消。");if(now<0)throw new InvalidOperationException("Invalid monotonic clock.");long countdown=checked(now+5000);
                    var bounds=backend.ClientBounds();if(ticket!=generation||faulted||safety.Faulted)throw new InvalidOperationException("完整流程启动已被取消。");if(bounds.Width!=o.ClientWidth||bounds.Height!=o.ClientHeight)throw new InvalidOperationException("客户端实际尺寸与确认布局不一致。");
                    plan=p;engine=candidate;layout=o.LayoutToken;due=countdown;lastTime=now;generation++;running=true;holding=ownsKey=ownsMouse=waiting=armed=false;command=null;status="Countdown";reason="5秒后开始，请切换到游戏。";WriteLog("Campaign armed with a five-second countdown.");
                }finally{starting=false;}
            }
        }
        public void Tick()
        {
            lock(gate)
            {
                if(!running||faulted||ticking)return;ticking=true;long ticket=generation;
                try{
                    if(!Continue(ticket))return;long now=clock();if(!Continue(ticket))return;if(now<lastTime){StopCore("完整流程时钟异常。");return;}lastTime=now;
                    bool emergency=backend.IsEmergencyPressed();if(!Continue(ticket))return;if(emergency){StopCore("F12/安全角急停。");return;}
                    var o=reader();if(!Continue(ticket))return;string problem=FrameProblem(o,plan,layout);if(problem!=null){StopCore(problem);return;}
                    if(!armed&&now<due)return;bool focus=backend.IsTargetForeground();if(!Continue(ticket))return;if(!focus){StopCore("游戏窗口失焦。");return;}armed=true;
                    if(holding)
                    {
                        bool interruption=(o.Farm.PlayerDead||o.Farm.PlayerGhost||o.FreeSlots==0||o.Farm.BagFull)&&command.Purpose==OperationPurpose.Farm;
                        interruption=interruption||((o.Farm.PlayerDead&&!o.Farm.PlayerGhost)&&engine.Status.StartsWith("Vendor",StringComparison.Ordinal));
                        if(!interruption){problem=engine.SafetyProblem(o)??CommandProblem(o,command,false);if(problem!=null){StopCore(problem);return;}}
                        if(interruption||now>=releaseAt||InteractionCompleted(o))
                        {Release();if(!Continue(ticket))return;waiting=true;releaseSequence=o.Sequence;status=interruption?"Interruption":"WaitingForFrame";}
                        return;
                    }
                    if(waiting&&o.Sequence==releaseSequence)return;waiting=false;
                    OperationCommand next=engine.Step(o,now);command=Clone(next);reason=next.Reason;
                    if(next.Action==OperationAction.Stop){StopCore(next.Reason);return;}if(next.Action==OperationAction.Completed){running=false;status="Completed";WriteLog("Campaign completed.");return;}if(next.Action==OperationAction.None||next.Action==OperationAction.ResumeFarm){status="WaitingForFrame";return;}
                    if(next.HoldMilliseconds<60||next.HoldMilliseconds>250)throw new InvalidOperationException("Unbounded campaign input rejected.");
                    if(next.Action==OperationAction.Key&&!AllowedKey(next.Key))throw new InvalidOperationException("Unsupported campaign key rejected.");
                    WriteLog("Campaign pulse "+next.Action+" / "+next.Purpose+" "+next.HoldMilliseconds+"ms");if(!Continue(ticket))return;
                    var latest=reader();if(!Continue(ticket))return;problem=FrameProblem(latest,plan,layout)??engine.SafetyProblem(latest)??CommandProblem(latest,next,true);if(problem!=null){StopCore(problem);return;}
                    Point? placedAt=null;
                    if(next.Action!=OperationAction.Key)
                    {
                        bool anyMouse=backend.IsMouseAlreadyDown(false)||backend.IsMouseAlreadyDown(true);if(!Continue(ticket))return;if(anyMouse){StopCore("用户正在按住鼠标，已停止。");return;}
                        var bounds=backend.ClientBounds();if(!Continue(ticket))return;if(bounds.Size!=new Size(latest.ClientWidth,latest.ClientHeight)){StopCore("窗口布局变化。");return;}
                        placedAt=new Point(bounds.X+next.Region.X+next.Region.Width/2,bounds.Y+next.Region.Y+next.Region.Height/2);
                        backend.MoveMouse(placedAt.Value);if(!Continue(ticket))return;
                    }
                    else{bool held=backend.IsKeyAlreadyDown(next.Key);if(!Continue(ticket))return;if(held){StopCore("用户正在按住目标键，已停止。");return;}}
                    // Validation, clock and backend callbacks can cancel or change the planned candidate.
                    // Run them before the final observation check and before claiming input ownership.
                    if(next.Action!=OperationAction.Key&&regionAllowed!=null){bool allowed=regionAllowed(next.Region);if(!Continue(ticket))return;if(!allowed){StopCore("目标区域与诊断色块重叠或超出有效界面。");return;}}
                    long pressTime=clock();if(!Continue(ticket))return;if(pressTime<lastTime){StopCore("完整流程时钟异常。");return;}
                    focus=backend.IsTargetForeground();if(!Continue(ticket))return;emergency=backend.IsEmergencyPressed();if(!Continue(ticket))return;if(!focus||emergency){StopCore("输入前窗口或急停状态变化。");return;}
                    bool borrowed=next.Action==OperationAction.Key?backend.IsKeyAlreadyDown(next.Key):(backend.IsMouseAlreadyDown(false)||backend.IsMouseAlreadyDown(true));if(!Continue(ticket))return;
                    if(borrowed){StopCore("输入前用户按住了目标按键或鼠标按钮。");return;}
                    var finalBounds=backend.ClientBounds();if(!Continue(ticket))return;
                    latest=reader();if(!Continue(ticket))return;problem=FrameProblem(latest,plan,layout)??engine.SafetyProblem(latest)??CommandProblem(latest,next,true);if(problem!=null){StopCore(problem);return;}
                    if(finalBounds.Width!=latest.ClientWidth||finalBounds.Height!=latest.ClientHeight){StopCore("输入前实际客户端尺寸变化。");return;}
                    if(placedAt.HasValue&&placedAt.Value!=new Point(finalBounds.X+next.Region.X+next.Region.Width/2,finalBounds.Y+next.Region.Y+next.Region.Height/2)){StopCore("光标定位后游戏窗口位置变化，取消旧屏幕坐标点击。");return;}
                    long deadline=checked(pressTime+next.HoldMilliseconds);lastTime=pressTime;releaseAt=deadline;holding=true;status="Holding";
                    if(next.Action==OperationAction.Key){key=next.Key;ownsKey=true;backend.KeyDown(key);}else{right=next.Action==OperationAction.RightClick;ownsMouse=true;backend.MouseDown(right);}
                }catch(Exception ex){Fault(ex);}finally{ticking=false;}
            }
        }
        public void Stop(string why){lock(gate){if(!faulted)StopCore(String.IsNullOrEmpty(why)?"用户停止。":why);}}
        void StopCore(string why){generation++;running=false;status="Stopped";reason=why;if(engine!=null)engine.Stop(why);Release();WriteLog("Campaign stopped: "+why);}
        void Fault(Exception ex){generation++;faulted=true;running=false;status="Faulted";reason="完整流程输入失败："+ex.Message;safety.Fail(reason);if(engine!=null)engine.Stop(reason);Release();WriteLog(reason);}
        void Release()
        {
            holding=false;if(ownsKey){ownsKey=false;try{backend.KeyUp(key);}catch(Exception ex){ReleaseFault(ex);}}if(ownsMouse){ownsMouse=false;try{backend.MouseUp(right);}catch(Exception ex){ReleaseFault(ex);}}WriteLog("Campaign released owned input.");
        }
        void ReleaseFault(Exception ex){faulted=true;running=false;status="Faulted";reason="输入释放失败："+ex.Message;safety.Fail(reason);if(engine!=null)engine.Stop(reason);}
        bool Continue(long ticket){if(ticket!=generation||!running||faulted)return false;if(safety.Faulted){Fault(new InvalidOperationException(safety.Reason));return false;}return true;}
        void WriteLog(string s){if(log!=null)try{log(s);}catch{}}
        bool AllowedKey(ushort k){var b=plan.Battle.Movement;return k==b.Forward||k==b.TurnLeft||k==b.TurnRight||k==b.StrafeLeft||k==b.StrafeRight||k==b.Jump||k==9||k==0x31||k==0x32||k==0x33||k==0x34||k==0x35||k==0x38||k==0x39||k==0x46||k==0x42||k==0x1B||k==plan.Settings.NpcSelectKey;}
        bool InteractionCompleted(OperationObservation o)
        {return command!=null&&((command.Purpose==OperationPurpose.Release&&o.Farm.PlayerGhost)||(command.Purpose==OperationPurpose.Resurrect&&!o.Farm.PlayerDead&&!o.Farm.PlayerGhost&&o.Farm.Health>0)||(command.Purpose==OperationPurpose.MerchantOpen&&o.MerchantOpen)||(command.Purpose==OperationPurpose.Sell&&o.InventoryKnown&&o.InventoryRevision!=command.ExpectedInventoryRevision&&!o.SlotOccupied(command.Bag,command.Slot)));}
        string CommandProblem(OperationObservation o,OperationCommand c,bool before)
        {
            if(c==null)return "缺少输入指令。";var f=o.Farm;
            if(c.Purpose==OperationPurpose.Farm)return FarmSession.TargetProblem(f,c.Farm,before,c.ExpectedNameHash,plan.Battle);
            if(c.Purpose==OperationPurpose.Release)
            {if(!before&&f.PlayerGhost)return null;if(!f.PlayerDead||f.PlayerGhost||!o.ReleaseKnown||!o.ReleaseVisible||!o.ReleaseEnabled||!o.IncomingResurrectionKnown||o.IncomingResurrection||o.ConfirmationVisible)return "释放灵魂条件已变化。";if(o.ReleaseRegion!=c.Region)return "释放按钮位置变化。";}
            if(c.Purpose==OperationPurpose.Resurrect)
            {if(!before&&!f.PlayerDead&&!f.PlayerGhost&&f.Health>0)return null;if(!f.PlayerGhost||!o.ResurrectKnown||!o.ResurrectVisible||!o.ResurrectEnabled||!o.RecoveryDelayKnown||o.RecoveryDelayMs!=0||!o.CorpseKnown||!o.IncomingResurrectionKnown||o.IncomingResurrection||o.ConfirmationVisible)return "复活按钮、尸体或冷却条件变化。";if(o.ResurrectRegion!=c.Region)return "复活按钮位置变化。";}
            if(c.Purpose==OperationPurpose.MerchantOpen||c.Purpose==OperationPurpose.BagsOpen||c.Purpose==OperationPurpose.Sell)
            {
                if(!f.TargetExists||!f.TargetIdentityKnown||f.TargetToken!=c.ExpectedTargetToken||!f.NameHashKnown||f.TargetNameHash!=c.ExpectedNameHash||!f.AttackabilityKnown||f.TargetCanAttack||f.TargetIsPlayer||!f.TargetDeadKnown||f.TargetDead)return "商人目标身份或友好状态变化。";
                if(!o.CursorKnown||!o.CursorEmpty||!o.ConfirmationKnown||o.ConfirmationVisible)return "鼠标携带物品或确认弹窗，已停止。";
                if(c.Purpose!=OperationPurpose.MerchantOpen&&(!o.MerchantKnown||!o.MerchantOpen||o.MerchantTargetToken!=c.ExpectedTargetToken||o.MerchantNameHash!=c.ExpectedNameHash||!o.SellTabKnown||!o.SellTab))return "商店已关闭、换商人或不在出售页。";
                if(c.Purpose==OperationPurpose.Sell)
                {
                    if(!o.InventoryKnown||!o.InventoryEventsKnown)return "背包或变更事件未知。";
                    if(before){var item=o.Item;if(!SalePolicy.CanSell(item,plan.Settings)||item.Bag!=c.Bag||item.Slot!=c.Slot||item.ItemId!=c.ExpectedItemId||item.Fingerprint!=c.ExpectedItemFingerprint||item.Count!=c.ExpectedItemCount||o.InventoryRevision!=c.ExpectedInventoryRevision||!item.RectangleKnown||item.Region!=c.Region||!o.SlotOccupied(c.Bag,c.Slot))return "出售前格子身份、物品或位置变化。";}
                    else if(o.InventoryRevision==c.ExpectedInventoryRevision&&(o.Item==null||o.Item.Bag!=c.Bag||o.Item.Slot!=c.Slot||o.Item.ItemId!=c.ExpectedItemId||o.Item.Fingerprint!=c.ExpectedItemFingerprint))return "鼠标按住期间目标物品页已变化。";
                }
            }
            if(before&&(f.Casting||f.Channeling))return "当前正在施法或引导，停止待执行输入。";
            if(c.Action==OperationAction.LeftClick||c.Action==OperationAction.RightClick)
            {var r=c.Region;if(r.Width<1||r.Height<1||r.Width>512||r.Height>512||r.X<0||r.Y<0||(long)r.X+r.Width>o.ClientWidth||(long)r.Y+r.Height>o.ClientHeight||r.IntersectsWith(new Rectangle(8,8,800,96)))return "点击区域无效或与诊断面板重叠。";}
            return null;
        }
        static string FrameProblem(OperationObservation o,CampaignPlan p,uint expectedLayout)
        {
            if(o==null||!o.Fresh||o.Farm==null||!o.Farm.Fresh||!o.Farm.KnownCore||o.Farm.ReadFailures!=0||o.Sequence!=o.Farm.Sequence)return "完整流程数据过期、未知或序号不配。";
            if(!o.GeometryKnown||o.LayoutToken==0||(expectedLayout!=0&&o.LayoutToken!=expectedLayout)||o.ClientWidth!=p.Settings.ClientWidth||o.ClientHeight!=p.Settings.ClientHeight)return "布局未知或与已确认尺寸不一致。";
            if(o.MapId!=p.FarmRoute.MapId||o.Farm.MapId!=o.MapId||o.Farm.MapMode!=p.FarmRoute.MapMode||!String.Equals(o.Farm.Zone,p.FarmRoute.Zone,StringComparison.Ordinal))return "地图身份变化。";return null;
        }
        static OperationCommand Clone(OperationCommand c){if(c==null)return null;return new OperationCommand{Action=c.Action,Purpose=c.Purpose,Key=c.Key,HoldMilliseconds=c.HoldMilliseconds,Region=c.Region,ExpectedTargetToken=c.ExpectedTargetToken,ExpectedNameHash=c.ExpectedNameHash,ExpectedItemId=c.ExpectedItemId,ExpectedItemFingerprint=c.ExpectedItemFingerprint,ExpectedInventoryRevision=c.ExpectedInventoryRevision,ExpectedItemCount=c.ExpectedItemCount,Bag=c.Bag,Slot=c.Slot,Reason=c.Reason,Farm=c.Farm==null?null:new FarmCommand{Action=c.Farm.Action,HoldMilliseconds=c.Farm.HoldMilliseconds,ExpectedTargetToken=c.Farm.ExpectedTargetToken,RequireAliveTarget=c.Farm.RequireAliveTarget,RequireDeadTarget=c.Farm.RequireDeadTarget,Reason=c.Farm.Reason}};}
    }
}
