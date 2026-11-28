using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
namespace WoWProbe
{
    public static partial class GrindCli
    {
        internal static Bitmap FishingCapture(){return Native.Capture(Native.ClientBounds(target));}
        internal static FishingSocialObservation FishingSocial(int expectedSequence)
        {
            if(!located)return new FishingSocialObservation();
            try{var region=new Rectangle(firstCenter.X-cell/2,firstCenter.Y-cell/2+20*cell,32*cell,12*cell);if(!client.Contains(region))return new FishingSocialObservation();using(var bitmap=Native.Capture(region))return FishingSocialObservation.Decode(PixelCodec.Decode(bitmap,cell/2,cell/2,cell),expectedSequence);}catch(FormatException){return new FishingSocialObservation();}
        }
        internal static QuickScreenText[] FishingMenus(Func<bool> cancel){return MenuRead(Native.ClientBounds(target),cancel);}
        internal static void FishingForgetPanels(){located=false;heartbeat=new Heartbeat();}
        internal static bool FishingTryWorld(){return SessionObservation()!=null;}
        internal static bool PrepareFishingWindow(bool manual,bool autoReturn,int seconds)
        {
            var window=FindWowWindow();if(window==null)throw new InvalidOperationException("未找到魔兽世界窗口。");
            var keys=new WindowsInputBackend(window,null);if(keys.IsEmergencyPressed())throw new OperationCanceledException("F12急停。");
            bool released=!keys.IsKeyAlreadyDown(16)&&!keys.IsKeyAlreadyDown(17)&&!keys.IsKeyAlreadyDown(18)&&!keys.IsKeyAlreadyDown(1)&&!keys.IsKeyAlreadyDown(2);
            if(manual||FishingFocusRecovery.MayReturn(autoReturn,keys.IsTargetForeground(),released,Native.IdleMilliseconds(),seconds)){Native.RequireInputIntegrity(window.ProcessId);Activate(window.Handle);Thread.Sleep(150);}
            return keys.IsTargetForeground();
        }
        internal static void FocusFishingWindow(WindowInfo window){if(Native.Matches(window))Activate(window.Handle);}
    }
    public sealed class FishingNativeHost:IFishingLoginHost
    {
        readonly Func<bool> cancel;readonly Func<long> clock;readonly InputFailureLatch safety=new InputFailureLatch();readonly NavigationCalibration map;
        readonly Action<string> report;public readonly WindowInfo Window;public readonly WindowsInputBackend Backend;
        public FishingNativeHost(NavigationCalibration map,Func<bool> cancel,Func<long> clock,Action<string> report)
        {this.map=map;this.cancel=cancel;this.clock=clock;this.report=report;Window=GrindCli.OpenTelemetry();Native.RequireInputIntegrity(Window.ProcessId);Backend=new WindowsInputBackend(Window,null);}
        public OperationObservation Read(){GrindCli.ReadTelemetry();return GrindCli.ReadOperations().Clone();}
        public FishingSocialObservation Social(OperationObservation o)
        {var s=GrindCli.FishingSocial(o.Sequence);for(int i=0;i<2&&!s.Known;i++){Idle(30);o=Read();s=GrindCli.FishingSocial(o.Sequence);}return s;}
        public Bitmap Capture(){return GrindCli.FishingCapture();}
        public bool Foreground{get{return Backend.IsTargetForeground();}}
        public bool InputFaulted{get{return safety.Faulted;}}
        public bool ModifiersReleased{get{return !Backend.IsKeyAlreadyDown(16)&&!Backend.IsKeyAlreadyDown(17)&&!Backend.IsKeyAlreadyDown(18);}}
        public void Focus(){CheckCancelled();GrindCli.FocusFishingWindow(Window);}
        long nextFocusAttempt;
        Rectangle menuBounds;
        public bool TryReturnFocus(bool enabled,int seconds)
        {
            CheckCancelled();long now=clock();if(now<nextFocusAttempt||!FishingFocusRecovery.MayReturn(enabled,Foreground,ModifiersReleased&&!Backend.IsKeyAlreadyDown(1)&&!Backend.IsKeyAlreadyDown(2),Native.IdleMilliseconds(),seconds))return false;
            nextFocusAttempt=now+5000;Focus();return Foreground;
        }
        public void CheckCancelled(){if(cancel()||Backend.IsEmergencyPressed())throw new OperationCanceledException("钓鱼已停止。");}
        public void ForgetPanels(){GrindCli.FishingForgetPanels();}
        QuickScreenText[] ReadMenu()
        {
            Destination();var bounds=Native.ClientBounds(Window);var lines=GrindCli.FishingMenus(cancel);Destination();
            if(bounds!=Native.ClientBounds(Window))throw new InvalidOperationException("菜单识别期间窗口尺寸变化，等待重新识别。");menuBounds=bounds;return lines;
        }
        public bool TryDismissAddonPopup()
        {
            Destination();var button=FishingLoginScreen.AddonIgnore(ReadMenu());if(button==null)return false;
            var verified=FishingLoginScreen.AddonIgnore(ReadMenu());if(!FishingLoginScreen.Same(button,verified))return false;
            MenuClick(verified);report("已核对插件错误/受限UI提示，点击“忽略”；没有禁用插件。");Idle(500);return true;
        }
        public FishingLoginFrame ReadLoginFrame()
        {
            Destination();var frame=new FishingLoginFrame{Lines=ReadMenu(),Foreground=Foreground,Released=ModifiersReleased};
            frame.World=GrindCli.FishingTryWorld();if(frame.World){var o=Read();var f=o.Farm;frame.Sequence=o.Sequence;
                frame.ReadyWorld=o.Fresh&&f.Fresh&&f.KnownCore&&o.CursorKnown&&o.CursorEmpty&&o.ConfirmationKnown&&!o.ConfirmationVisible&&o.IncomingResurrectionKnown&&!o.IncomingResurrection&&f.ChatOpenKnown&&!f.ChatOpen;
                frame.CanLogout=frame.ReadyWorld&&!f.PlayerDead&&!f.PlayerGhost&&!f.InCombat&&!f.Casting&&!f.Channeling;}
            Destination();return frame;
        }
        public void Idle(int ms){for(int n=0;n<ms;n+=50){CheckCancelled();Thread.Sleep(Math.Min(50,ms-n));}}
        public void Destination()
        {CheckCancelled();if(!Foreground)throw new InvalidOperationException("游戏失去前台；切回游戏后继续。");foreach(ushort k in new ushort[]{16,17,18})if(Backend.IsKeyAlreadyDown(k))throw new InvalidOperationException("等待用户松开修饰键。");}
        GrindInputGuard Guard(Func<OperationObservation,bool> context,bool chat=false)
        {
            return new GrindInputGuard(Backend,()=>{
                CheckCancelled();var o=Read();var f=o.Farm;
                if(f.PlayerDead||f.PlayerGhost||f.InCombat||!f.ChatOpenKnown||(!chat&&f.ChatOpen))throw new InvalidOperationException("角色死亡、战斗或聊天状态改变，停止当前钓鱼动作。");
                if(context!=null&&!context(o))throw new InvalidOperationException("钓鱼输入条件已改变。");return f;
            },clock,ms=>Thread.Sleep(ms),safety,map.MapId,map.MapMode,map.Zone);
        }
        public void Pulse(ushort key,int hold=80,Func<OperationObservation,bool> context=null,Func<FarmObservation,bool> releaseWhen=null)
        {Destination();Guard(context).Pulse(key,hold,null,releaseWhen);}
        public void Click(Rectangle region,Func<OperationObservation,bool> context)
        {Destination();var bounds=Native.ClientBounds(Window);var point=new Point(bounds.X+region.X+region.Width/2,bounds.Y+region.Y+region.Height/2);bool clicked=false;Guard(o=>clicked||context==null||context(o)).Click(point,false,p=>Native.ClientBounds(Window)==bounds&&bounds.Contains(p),Native.MoveMouse,(right,up)=>{Native.SendMouseButton(right,up);if(!up)clicked=true;});}
        public void MenuKey(ushort key)
        {Destination();if(Backend.IsKeyAlreadyDown(key))throw new InvalidOperationException("用户正按住菜单键。");try{Backend.KeyDown(key);Thread.Sleep(80);}finally{Backend.KeyUp(key);}}
        public void MenuClick(QuickScreenText text)
        {Destination();var bounds=Native.ClientBounds(Window);if(bounds!=menuBounds)throw new InvalidOperationException("菜单识别期间窗口尺寸变化，等待重新识别。");var p=new Point(bounds.X+text.X+text.Width/2,bounds.Y+text.Y+text.Height/2);if(!bounds.Contains(p))throw new InvalidOperationException("菜单按钮超出游戏窗口。");if(Backend.IsKeyAlreadyDown(1)||Backend.IsKeyAlreadyDown(2))throw new InvalidOperationException("用户正按住鼠标。");Native.MoveMouse(p);Destination();try{Native.SendMouseButton(false,false);Thread.Sleep(80);}finally{Native.SendMouseButton(false,true);}}
        public void Chat(string message,bool whisper,uint expectedSender=0,uint expectedEvent=0,bool command=false)
        {
            if(String.IsNullOrWhiteSpace(message)||message.Length>160)throw new FormatException("聊天文本为空或过长。");Destination();var before=Read();if(before.Farm.ChatOpen||before.Farm.Casting||before.Farm.Channeling)throw new InvalidOperationException("已有聊天或施法，暂不自动聊天。");
            Func<OperationObservation,bool> recipient=o=>{if(command)return true;var s=Social(o);if(!s.Known)throw new FishingSocialUnavailableException();return !whisper||s.WhisperCount==expectedEvent&&s.SenderHash==expectedSender;};
            bool owned=false;
            try{
                bool opened=false;var guard=Guard(o=>recipient(o)&&(opened||!o.Farm.ChatOpen&&!o.Farm.Casting&&!o.Farm.Channeling),true);owned=true;guard.Pulse(whisper?(ushort)82:(ushort)13,80,()=>opened=true);Idle(250);
                bool requireSay=false;Func<OperationObservation,bool> edit=o=>{if(!recipient(o)||!o.Farm.ChatOpen)return false;if(!whisper){if(!requireSay)return true;var channel=Social(o);if(!channel.Known||!channel.ChatTypeKnown)throw new FishingSocialUnavailableException();return channel.Say;}var s=Social(o);if(!s.Known||!s.ChatTypeKnown)throw new FishingSocialUnavailableException();return s.Whisper&&s.ReplyTargetHash==expectedSender;};
                guard=Guard(edit,true);
                // Explicit /s chooses local say, irrespective of the user's previous channel.
                if(!whisper&&!command){guard.TypeText("/s ",Native.SendUnicodeChar);Idle(100);var current=Read();var channel=Social(current);if(!channel.Known||!channel.ChatTypeKnown)throw new FishingSocialUnavailableException();if(!channel.Say)throw new InvalidOperationException("未确认普通说话频道，保留消息待重试。");requireSay=true;}
                guard.TypeText(message,Native.SendUnicodeChar);guard.Ensure();bool submitted=false;
                var submit=Guard(o=>submitted||edit(o),true);submit.Pulse(13,80,()=>submitted=true);owned=false;
                report(whisper?"已按R回复新私信："+message:command?"已选择录制的商人目标。":"圈末交流："+message);
            }finally{
                if(owned&&Foreground&&!cancel())try{var o=Read();if(o.Farm.ChatOpen)Guard(null,true).Pulse(27,60);}catch{}
            }
        }
        public void AuxiliaryPulse(ushort key,Action sent)
        {
            bool pressed=false;Destination();Guard(o=>pressed||!o.Farm.Casting&&!o.Farm.Channeling).Pulse(key,80,()=>{pressed=true;sent();});
        }
        public void RecoveryInput(QuickRecoveryCommand command,RouteKeyBindings keys)
        {
            Destination();bool clicked=false;
            var guard=new GrindInputGuard(Backend,()=>{CheckCancelled();var o=Read();var f=o.Farm;bool completed=clicked&&(command.Release?f.PlayerGhost:!f.PlayerDead&&!f.PlayerGhost&&f.Health>0);
                if(!f.ChatOpenKnown||f.ChatOpen||f.InCombat||(!completed&&!QuickRecovery.Matches(command,o)))throw new InvalidOperationException("跑尸/复活输入状态发生变化。");return f;},clock,ms=>Thread.Sleep(ms),safety,map.MapId,map.MapMode,map.Zone);
            if(command.Action==QuickRecoveryAction.Key){guard.Pulse(FishingRecoveryRoutes.Key(command.Key,keys),command.Hold);return;}
            if(command.Action!=QuickRecoveryAction.Click)return;var bounds=Native.ClientBounds(Window);var r=command.Region;var point=new Point(bounds.X+r.X+r.Width/2,bounds.Y+r.Y+r.Height/2);
            guard.Click(point,false,p=>Native.ClientBounds(Window)==bounds&&bounds.Contains(p),Native.MoveMouse,(right,up)=>{Native.SendMouseButton(right,up);if(!up)clicked=true;});
        }
    }
    public static class FishingLoginScreen
    {
        public static string Clean(string s){return (s??"").Replace(" ","").Replace("\r","").Replace("\n","").ToLowerInvariant();}
        public static QuickScreenText Find(QuickScreenText[] lines,params string[] words)
        {QuickScreenText found=null;foreach(var l in lines??new QuickScreenText[0])foreach(var w in words)if(Clean(l.Text)==Clean(w)){if(found!=null)return null;found=l;break;}return found;}
        public static bool Contains(QuickScreenText[] lines,params string[] words)
        {foreach(var l in lines??new QuickScreenText[0])foreach(var w in words)if(Clean(l.Text).Contains(w))return true;return false;}
        public static bool Login(QuickScreenText[] l){return Contains(l,"账号","帐户","account","战网")&&Contains(l,"密码","password")&&Find(l,"登录","登入","登录游戏","log in","login")!=null;}
        public static bool Disconnected(QuickScreenText[] l){return Contains(l,"断开连接","连接中断","disconnected","连接已断开")&&Find(l,"确定","确认","okay","ok")!=null;}
        public static bool Same(QuickScreenText a,QuickScreenText b){return a!=null&&b!=null&&Math.Abs(a.X-b.X)<=8&&Math.Abs(a.Y-b.Y)<=8&&Clean(a.Text)==Clean(b.Text);}
        public static bool LogoutCountdown(QuickScreenText[] lines)
        {return Find(lines,"取消","cancel")!=null&&Contains(lines,"登出","logout","loggingout")&&Contains(lines,"秒","second");}
        public static QuickScreenText AddonIgnore(QuickScreenText[] lines)
        {
            string text="";foreach(var line in lines??new QuickScreenText[0])text+=Clean(line.Text);
            bool error=(text.Contains("错误")||text.Contains("error"))&&(text.Contains("插件")||text.Contains("lua")||text.Contains("addon")||text.Contains("脚本")||text.Contains("script"));
            bool blocked=(text.Contains("wowprobe")||text.Contains("插件")||text.Contains("addon"))&&(text.Contains("禁用")||text.Contains("blocked"))&&(text.Contains("只对暴雪")||text.Contains("blizzard")&&text.Contains("ui"));
            if(!error&&!blocked)return null;
            return Find(lines,"忽略","ignore");
        }
    }
    public sealed class FishingLoginRunner
    {
        readonly IFishingLoginHost host;readonly FishingActivityClock schedule;readonly Action<string> log;readonly Func<long> now;readonly Func<bool> cancelled;
        readonly bool autoReturn;readonly int idleSeconds;string lastText;long lastNotice=-60000;
        public FishingLoginRunner(IFishingLoginHost h,FishingActivityClock schedule,Func<long> now,Func<bool> cancelled,Action<string> log,bool autoReturn=false,int idleSeconds=30)
        {host=h;this.schedule=schedule;this.now=now;this.cancelled=cancelled;this.log=log;this.autoReturn=autoReturn;this.idleSeconds=idleSeconds;}
        void Report(string text){long time=now();if(text!=lastText||time-lastNotice>=30000){log(text);lastText=text;lastNotice=time;}}
        public void Run()
        {
            var flow=new FishingLoginFlow(schedule);
            while(!cancelled())
            {
                try{
                    host.CheckCancelled();long time=now();
                    if(schedule.Phase=="Offline"&&time<schedule.OfflineDue){Report("离线等待，剩余"+Math.Ceiling((schedule.OfflineDue-time)/60000.0)+"分钟。");host.Idle(250);continue;}
                    if(!host.Foreground){if(host.TryReturnFocus(autoReturn,idleSeconds))Report("上下线：键鼠已空闲，自动切回游戏。");else Report("上下线任务保留：等待游戏前台或键鼠空闲后自动切回。");host.Idle(250);continue;}
                    if(!host.ModifiersReleased){Report("上下线任务保留：等待Alt/Ctrl/Shift松开。");host.Idle(250);continue;}
                    var frame=host.ReadLoginFrame();string previous=schedule.Phase;var action=flow.Step(frame,now());
                    if(previous=="Logout"&&schedule.Phase=="Offline")host.ForgetPanels();
                    Report(action.Reason);if(action.Action==FishingLoginAction.Complete)return;
                    if(action.Action==FishingLoginAction.Wait){host.Idle(250);continue;}
                    // Confirm the same screen and button immediately before each input.
                    var fresh=host.ReadLoginFrame();var verified=flow.Step(fresh,now());if(!FishingLoginFlow.Same(action,verified)){host.Idle(250);continue;}
                    host.Destination();
                    if(action.Action==FishingLoginAction.OpenMenu)host.MenuKey(27);
                    else if(action.Action==FishingLoginAction.Enter)host.MenuKey(13);
                    else host.MenuClick(verified.Button);
                    flow.Sent(verified,now());Report("上下线动作已发送："+verified.Reason);host.Idle(250);
                }catch(InvalidOperationException ex){
                    if(cancelled()||host.InputFaulted)throw;
                    if(!FishingFocusRecovery.Transient(ex.Message)&&ex.Message!="菜单识别超时。"&&!ex.Message.StartsWith("菜单文字识别不可用："))throw;
                    Report("上下线任务保留："+ex.Message);host.Idle(500);
                }
            }
        }
    }
}
