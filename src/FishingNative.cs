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
    }
    public sealed class FishingNativeHost
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
        public void Focus(){GrindCli.FocusGameWindow();}
        public void CheckCancelled(){if(cancel()||Backend.IsEmergencyPressed())throw new OperationCanceledException("钓鱼已停止。");}
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
        {Destination();var bounds=Native.ClientBounds(Window);var p=new Point(bounds.X+text.X+text.Width/2,bounds.Y+text.Y+text.Height/2);if(!bounds.Contains(p))throw new InvalidOperationException("菜单按钮超出游戏窗口。");if(Backend.IsKeyAlreadyDown(1)||Backend.IsKeyAlreadyDown(2))throw new InvalidOperationException("用户正按住鼠标。");Native.MoveMouse(p);Destination();try{Native.SendMouseButton(false,false);Thread.Sleep(80);}finally{Native.SendMouseButton(false,true);}}
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
    }
    public sealed class FishingLoginRunner
    {
        readonly FishingNativeHost host;readonly FishingActivityClock schedule;readonly Action<string> log;readonly Func<long> now;readonly Func<bool> cancelled;
        public FishingLoginRunner(FishingNativeHost h,FishingActivityClock schedule,Func<long> now,Func<bool> cancelled,Action<string> log){host=h;this.schedule=schedule;this.now=now;this.cancelled=cancelled;this.log=log;}
        public void Run()
        {
            long lastAction=-60000,lastNotice=-60000;int loginAttempts=0;
            while(!cancelled())
            {
                host.CheckCancelled();long time=now();
                if(schedule.Phase=="Offline"&&time<schedule.OfflineDue){if(time-lastNotice>60000){log("离线等待，剩余"+Math.Ceiling((schedule.OfflineDue-time)/60000.0)+"分钟。");lastNotice=time;}host.Idle(250);continue;}
                if(!host.Foreground){host.Idle(250);continue;}
                if(schedule.Phase!="Logout"&&GrindCli.FishingTryWorld()){schedule.Online(now());log("已确认重新上线，重新计时并恢复钓鱼。");return;}
                var lines=GrindCli.FishingMenus(cancelled);host.Destination();
                bool select=QuickMenuText.HasContext(lines,true);bool login=FishingLoginScreen.Login(lines),disconnected=FishingLoginScreen.Disconnected(lines);
                if(schedule.Phase=="Logout"&&(select||login||disconnected)&&!GrindCli.FishingTryWorld()){schedule.OfflineConfirmed(now());GrindCli.FishingForgetPanels();log("已确认离开角色，开始随机离线等待。");continue;}
                if(time-lastAction<10000){host.Idle(250);continue;}
                QuickScreenText action=null;string[] words=null;
                if(schedule.Phase=="Logout")
                {
                    if(QuickMenuText.HasContext(lines,false)){words=new[]{"登出","返回角色选择","返回角色菜单","返回角色列表","logout"};action=FishingLoginScreen.Find(lines,words);}
                    else if(GrindCli.FishingTryWorld()){var o=host.Read();if(!o.Farm.Casting&&!o.Farm.Channeling&&!o.Farm.InCombat&&!o.Farm.ChatOpen){host.MenuKey(27);lastAction=now();log("打开ESC菜单，等待确认登出按钮。");}}
                }
                else if(select){var verified=GrindCli.FishingMenus(cancelled);if(QuickMenuText.HasContext(verified,true)&&QuickMenuText.Find(verified,true)!=null){host.MenuKey(13);lastAction=now();log("已确认角色选择界面，回车进入游戏。");}}
                else if(disconnected){words=new[]{"确定","确认","okay","ok"};action=FishingLoginScreen.Find(lines,words);}
                else if(login&&loginAttempts<3){words=new[]{"登录","登入","登录游戏","login"};action=FishingLoginScreen.Find(lines,words);loginAttempts++;}
                else if(time-lastNotice>30000){log(login?"登录尚未成功，等待账号登录条件满足；不会输入或保存密码。":"等待可识别的登录或角色选择界面。");lastNotice=time;}
                if(action!=null){var fresh=GrindCli.FishingMenus(cancelled);var button=FishingLoginScreen.Find(fresh,words);if(FishingLoginScreen.Same(action,button)&&(schedule.Phase=="Logout"?QuickMenuText.HasContext(fresh,false):disconnected?FishingLoginScreen.Disconnected(fresh):FishingLoginScreen.Login(fresh))){host.MenuClick(button);lastAction=now();log("已点击已核对的按钮："+button.Text);}}
                host.Idle(300);
            }
        }
    }
}
