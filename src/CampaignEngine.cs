using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace WoWProbe
{
    public sealed class CampaignCheckpoint
    {
        public int SchemaVersion=1,FarmIndex;
        public string PlanFingerprint,Phase;
        public bool Interrupted,Completed;
    }
    public static class CampaignCheckpointStore
    {
        public static void Save(string path,CampaignCheckpoint cp)
        {Validate(cp);RouteFiles.Save(path,new JavaScriptSerializer().Serialize(cp));}
        public static CampaignCheckpoint Load(string path,CampaignPlan plan)
        {
            var o=RouteJson.Object(RouteFiles.Load(path));RouteJson.Keys(o,"SchemaVersion,FarmIndex,PlanFingerprint,Phase,Interrupted,Completed",null);
            var c=new CampaignCheckpoint{SchemaVersion=RouteJson.Integer(o,"SchemaVersion",1,1),FarmIndex=RouteJson.Integer(o,"FarmIndex",0,5000),PlanFingerprint=RouteJson.String(o,"PlanFingerprint"),Phase=RouteJson.String(o,"Phase"),Interrupted=RouteJson.Boolean(o,"Interrupted"),Completed=RouteJson.Boolean(o,"Completed")};Validate(c);
            if(plan==null||c.PlanFingerprint!=plan.Fingerprint()||c.FarmIndex>plan.FarmRoute.Points.Count||c.Completed!=(c.FarmIndex==plan.FarmRoute.Points.Count))throw new FormatException("Campaign checkpoint does not match the complete current plan.");return c;
        }
        static void Validate(CampaignCheckpoint c)
        {
            if(c==null||c.SchemaVersion!=1||c.FarmIndex<0||c.FarmIndex>5000||c.PlanFingerprint==null||c.PlanFingerprint.Length!=64)throw new FormatException("Invalid campaign checkpoint.");
            foreach(char x in c.PlanFingerprint)if(!(x>='0'&&x<='9')&&!(x>='a'&&x<='f'))throw new FormatException("Invalid campaign fingerprint.");
            if(c.Phase!="Farm"&&c.Phase!="Recovery"&&c.Phase!="Vendor"&&c.Phase!="Stopped"&&c.Phase!="Completed")throw new FormatException("Invalid campaign checkpoint phase.");
            if(c.Completed!=(c.Phase=="Completed"))throw new FormatException("Checkpoint completed flag and phase must agree.");
            if(c.Phase=="Farm"&&c.Interrupted)throw new FormatException("A farm checkpoint cannot retain a pending interruption.");
            if((c.Phase=="Recovery"||c.Phase=="Vendor")&&!c.Interrupted)throw new FormatException("An interruption checkpoint must retain its pending interruption.");
            if(c.Completed&&c.Interrupted)throw new FormatException("A completed campaign cannot have a pending interruption.");
        }
    }
    public sealed class CampaignEngine
    {
        enum Mode{Farm,Recovery,Vendor,Stopped,Completed}
        readonly CampaignPlan plan;readonly string fingerprint;FarmEngine farm;RecoveryEngine recovery;VendorEngine vendor;Mode mode;
        long lastTime,fullAt;bool hasTime,hasSequence,fullPending,interrupted,requireNewFarmFrame;int sequence,fullFrames,savedIndex;
        public CampaignEngine(CampaignPlan plan){if(plan==null)throw new ArgumentNullException("plan");this.plan=plan.Clone();this.plan.Validate();fingerprint=this.plan.Fingerprint();savedIndex=this.plan.StartIndex;NewFarm();LastReason="等待完整流程数据。";}
        public string Status{get{return mode==Mode.Farm?"Farm/"+farm.Status:mode==Mode.Recovery?"Recovery/"+recovery.Status:mode==Mode.Vendor?"Vendor/"+vendor.Status:mode.ToString();}}
        public string LastReason{get;private set;}public bool Completed{get{return mode==Mode.Completed;}}
        public int SavedFarmIndex{get{return savedIndex;}}public int RouteIndex{get{return interrupted?savedIndex:farm.RouteIndex;}}
        public int RecoveryCount{get;private set;}public int VendorCount{get;private set;}
        public CampaignCheckpoint CurrentCheckpoint{get{return new CampaignCheckpoint{PlanFingerprint=fingerprint,FarmIndex=Completed?plan.FarmRoute.Points.Count:Math.Min(plan.FarmRoute.Points.Count-1,Math.Max(0,RouteIndex)),Phase=mode.ToString(),Interrupted=interrupted,Completed=Completed};}}
        public void Stop(string why){if(mode==Mode.Stopped||mode==Mode.Completed)return;mode=Mode.Stopped;LastReason=String.IsNullOrEmpty(why)?"完整流程已停止。":why;if(farm!=null)farm.Stop(LastReason);if(recovery!=null)recovery.Stop(LastReason);if(vendor!=null)vendor.Stop(LastReason);}
        public OperationCommand Step(OperationObservation o,long now)
        {
            if(mode==Mode.Stopped)return Result(OperationAction.Stop,LastReason);if(Completed)return Result(OperationAction.Completed,LastReason);
            if(now<0||(hasTime&&now<lastTime))return Fail("完整流程时钟异常。");hasTime=true;lastTime=now;
            string unsafeReason=Unsafe(o);if(unsafeReason!=null)return Fail(unsafeReason);
            bool distinct=!hasSequence||o.Sequence!=sequence;if(distinct){hasSequence=true;sequence=o.Sequence;}
            if((o.Farm.PlayerDead||o.Farm.PlayerGhost)&&mode!=Mode.Recovery)
            {
                if(!plan.Settings.EnableRecovery)return Fail("角色死亡，自动恢复未启用。");SaveInterruption();farm.Stop("进入死亡恢复。");if(vendor!=null)vendor.Stop("死亡优先于售卖。");recovery=new RecoveryEngine(plan,savedIndex);mode=Mode.Recovery;fullPending=false;
            }
            if(mode==Mode.Recovery)return Child(recovery.Step(o,now),o,true);
            if(mode==Mode.Vendor)return Child(vendor.Step(o,now),o,false);
            if(o.FreeSlots==0)
            {
                if(!fullPending){fullPending=true;fullAt=now;fullFrames=distinct?1:0;}else if(distinct)fullFrames++;
                if(fullFrames<2||now-fullAt<3000)return Result(OperationAction.None,"等待连续数据确认背包已满，期间暂停农场输入。");
                if(!plan.Settings.EnableVendor)return Fail("背包已满，自动售卖未启用。");SaveInterruption();farm.Stop("进入商人清包。");vendor=new VendorEngine(plan,savedIndex);mode=Mode.Vendor;fullPending=false;return Child(vendor.Step(o,now),o,false);
            }
            if(o.Farm.BagFull)
            {
                if(!fullPending){fullPending=true;fullAt=now;}if(now-fullAt>10000)return Fail("背包已满提示与剩余空位长期不一致。");return Result(OperationAction.None,"等待背包错误提示更新。");
            }
            fullPending=false;fullFrames=0;
            if(requireNewFarmFrame&&!distinct)return Result(OperationAction.None,"恢复后等待新的完整帧。");requireNewFarmFrame=false;
            string portrait=PortraitProblem(o);if(portrait!=null)return Fail(portrait);FarmCommand c=farm.Step(o.Farm,now);LastReason=c.Reason;
            if(c.Action==FarmAction.Stop)return Fail(c.Reason);if(c.Action==FarmAction.Completed){mode=Mode.Completed;return Result(OperationAction.Completed,c.Reason);}
            if(c.Action==FarmAction.None)return Result(OperationAction.None,c.Reason);
            return new OperationCommand{Action=OperationAction.Key,Purpose=OperationPurpose.Farm,Key=FarmKey(c.Action,plan.Battle.Movement),HoldMilliseconds=c.HoldMilliseconds,ExpectedTargetToken=c.ExpectedTargetToken,ExpectedNameHash=c.ExpectedTargetToken==0?0:o.Farm.TargetNameHash,Farm=c,Reason=c.Reason};
        }
        OperationCommand Child(OperationCommand c,OperationObservation o,bool isRecovery)
        {
            if(c==null)return Fail("子流程未返回有效指令。");LastReason=c.Reason;if(c.Action==OperationAction.Stop)return Fail(c.Reason);
            if(c.Action==OperationAction.ResumeFarm)
            {
                if(isRecovery)RecoveryCount++;else VendorCount++;
                if(isRecovery&&o.FreeSlots==0&&plan.Settings.EnableVendor){vendor=new VendorEngine(plan,savedIndex);mode=Mode.Vendor;return Result(OperationAction.None,"复活恢复已完成，保留原农场断点继续处理包满。");}
                NewFarm();interrupted=false;requireNewFarmFrame=true;fullPending=false;return Result(OperationAction.None,"中断已处理，下一帧从原农场路点继续。");
            }
            return c;
        }
        void SaveInterruption(){if(interrupted)return;savedIndex=Math.Min(plan.FarmRoute.Points.Count-1,Math.Max(0,farm.RouteIndex));interrupted=true;}
        void NewFarm(){farm=new FarmEngine(plan.FarmRoute,plan.Battle,plan.FarmReverse,plan.FarmLoop,savedIndex);mode=Mode.Farm;}
        internal string SafetyProblem(OperationObservation o)
        {
            string problem=Unsafe(o);if(problem!=null)return problem;
            if(mode==Mode.Recovery)return recovery.SafetyProblem(o);if(mode==Mode.Vendor)return vendor.SafetyProblem(o);
            if(mode==Mode.Farm){problem=FarmSession.GlobalProblem(o.Farm,plan.FarmRoute,plan.Battle);if(problem!=null)return problem;problem=PortraitProblem(o);if(problem!=null)return problem;if(FarmEngine.DistanceFromRoute(plan.FarmRoute,plan.Battle.Calibration.AxisScale,o.Farm.X,o.Farm.Y)>.02)return "角色偏离已记录农场路线。";}return null;
        }
        string PortraitProblem(OperationObservation o)
        {
            if(!plan.Settings.RequirePortraitMatch||mode!=Mode.Farm||o.Farm.TargetDead||o.Farm.PlayerDead||o.Farm.PlayerGhost)return null;
            if(!farm.EncounterActive&&TargetCatalog.Match(plan.Battle,o.Farm)==null)return null;
            if(!o.PortraitCheckKnown||o.PortraitCheckSequence!=o.Sequence||o.PortraitCheckTargetToken!=o.Farm.TargetToken)return "已启用头像核对，但缺少本帧本目标的有效参考图结果。";
            return o.PortraitMatch?null:"目标头像与已保存参考图不匹配，已停止。";
        }
        string Unsafe(OperationObservation o)
        {
            if(o==null||!o.Fresh||o.Farm==null||!o.Farm.Fresh||!o.Farm.KnownCore||o.Farm.ReadFailures!=0||o.Sequence!=o.Farm.Sequence)return "完整流程状态过期、序号不配或必要能力未知。";
            if(!o.FreeSlotsKnown||o.FreeSlots<0)return "背包空位未知，停止完整流程。";
            var f=o.Farm;if(f.MapId!=plan.FarmRoute.MapId||o.MapId!=f.MapId||f.MapMode!=plan.FarmRoute.MapMode||!String.Equals(f.Zone,plan.FarmRoute.Zone,StringComparison.Ordinal))return "完整流程地图或区域不匹配。";
            if(!NavigationMath.InRange(f.X,0,1)||!NavigationMath.InRange(f.Y,0,1)||!NavigationMath.Finite(f.Facing)||f.Health<0||f.Health>100)return "角色位置、朝向或血量无效。";return null;
        }
        OperationCommand Fail(string why){Stop(why);return Result(OperationAction.Stop,LastReason);}
        static OperationCommand Result(OperationAction a,string reason){return new OperationCommand{Action=a,Reason=reason};}
        internal static ushort FarmKey(FarmAction a,RouteKeyBindings b)
        {
            switch(a){case FarmAction.Forward:return b.Forward;case FarmAction.TurnLeft:return b.TurnLeft;case FarmAction.TurnRight:return b.TurnRight;case FarmAction.StrafeLeft:return b.StrafeLeft;case FarmAction.StrafeRight:return b.StrafeRight;case FarmAction.Jump:return b.Jump;case FarmAction.SelectTarget:return 9;case FarmAction.Interact:return 0x46;case FarmAction.Skin:return 0x35;case FarmAction.Heal:return 0x38;case FarmAction.Mana:return 0x39;case FarmAction.Skill1:return 0x31;case FarmAction.Skill2:return 0x32;case FarmAction.Skill3:return 0x33;case FarmAction.Skill4:return 0x34;default:throw new ArgumentException("Unsupported farm action.");}
        }
    }
}
