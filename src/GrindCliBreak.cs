using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace WoWProbe
{
    public static partial class GrindCli
    {
        static QuickScreenText[] MenuRead(Rectangle bounds,Func<bool> cancelled)
        {
            string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","read-menu.ps1");
            if(!File.Exists(script))throw new InvalidOperationException("缺少菜单识别组件assets/read-menu.ps1。");
            var psi=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -X "+bounds.X+" -Y "+bounds.Y+" -Width "+bounds.Width+" -Height "+bounds.Height){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            using(var process=Process.Start(psi))
            {
                var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();var elapsed=Stopwatch.StartNew();
                while(!process.WaitForExit(50))if(cancelled()||(Native.GetAsyncKeyState(123)&0x8000)!=0||elapsed.ElapsedMilliseconds>12000){process.Kill();throw new InvalidOperationException(cancelled()||((Native.GetAsyncKeyState(123)&0x8000)!=0)?"Emergency stop during menu recognition.":"菜单识别超时。");}
                if(process.ExitCode!=0)throw new InvalidOperationException("菜单文字识别不可用："+errors.Result.Trim());
                return new JavaScriptSerializer().Deserialize<QuickScreenText[]>(output.Result)??new QuickScreenText[0];
            }
        }
        static FarmObservation SessionObservation()
        {try{if(!located&&!LocatePanels(null))return null;var o=Snapshot();return o!=null&&o.Fresh&&o.KnownCore&&o.ReadFailures==0?o:null;}catch{return null;}}
        internal static void ResumeScheduledBreak(QuickBreakSchedule schedule,QuickRoutePlan plan,Func<bool> cancelled,Action<string> report,string path)
        {
            var uiReport=report;report=message=>{File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-session.log"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+message+Environment.NewLine);uiReport(message);};var window=FindWowWindow();if(window==null)throw new InvalidOperationException("定时休息等待魔兽世界窗口。");Native.RequireInputIntegrity(window.ProcessId);
            target=window;client=Native.ClientBounds(window);located=false;heartbeat=new Heartbeat();var bounds=client;var backend=new WindowsInputBackend(window,null);
            Action ensure=()=>{if(cancelled()||backend.IsEmergencyPressed())throw new InvalidOperationException("Emergency stop during scheduled break.");if(!backend.IsTargetForeground()||Native.ClientBounds(window)!=bounds)throw new InvalidOperationException("Target window lost foreground during scheduled break.");};
            Action<ushort> key=k=>{ensure();if(backend.IsKeyAlreadyDown(k)||backend.IsKeyAlreadyDown(16)||backend.IsKeyAlreadyDown(17)||backend.IsKeyAlreadyDown(18))throw new InvalidOperationException("等待用户松开按键。");try{backend.KeyDown(k);Thread.Sleep(80);}finally{backend.KeyUp(k);}};
            long lastNotice=0,lastEscape=-60000,lastLogout=-60000,lastEnter=-60000;bool first=true;
            while(schedule.Phase!="Online"&&!cancelled())
            {
                long now=QuickBreakSchedule.Now;
                if(backend.IsEmergencyPressed())throw new InvalidOperationException("Emergency stop during scheduled break.");
                // Offline time may pass while another app is foreground; input is only
                // allowed after the bound game window is foreground again.
                if(schedule.Phase=="Offline"&&now<schedule.OfflineUntil)
                {if(first||now-lastNotice>60000){report("已确认角色菜单；休息剩余"+(int)Math.Ceiling((schedule.OfflineUntil-now)/60000.0)+"分钟。");lastNotice=now;first=false;}Thread.Sleep(250);continue;}
                ensure();var online=SessionObservation();
                if(online!=null&&(schedule.Phase=="Login"||schedule.Phase=="Offline"))
                {if(!plan.MapMatches(online))throw new InvalidOperationException("重新上线的地图与当前路线不匹配。");schedule.ConfirmOnline(QuickBreakSchedule.ClockNow);schedule.Save(path,true);report("确认重新进入游戏，恢复巡航。"+schedule.Summary);return;}
                if(schedule.Phase=="Logout"&&online!=null&&(online.InCombat||online.PlayerDead||online.PlayerGhost))
                {schedule.Phase="Online";schedule.Save(path,true);report("下线前状态变化，先完成战斗或跑尸，再尝试休息。");return;}
                var lines=MenuRead(bounds,cancelled);ensure();var enter=QuickMenuText.HasContext(lines,true)?QuickMenuText.Find(lines,true):null;
                if(enter!=null)
                {
                    var checkedLines=MenuRead(bounds,cancelled);var verified=QuickMenuText.HasContext(checkedLines,true)?QuickMenuText.Find(checkedLines,true):null;ensure();
                    if(verified==null||Math.Abs(verified.X-enter.X)>8||Math.Abs(verified.Y-enter.Y)>8){Thread.Sleep(300);continue;}
                    if(schedule.Phase=="Logout"){schedule.ConfirmOffline(QuickBreakSchedule.Now);schedule.Save(path,true);report("已确认返回角色菜单，开始休息"+schedule.OfflineMinutes+"分钟。");continue;}
                    if(now-lastEnter>=30000){schedule.Phase="Login";schedule.Save(path,true);key(13);lastEnter=now;report("休息结束，已确认进入游戏按钮，按回车并等待有效游戏状态。");}
                }
                else if(schedule.Phase=="Logout")
                {
                    var logout=QuickMenuText.HasContext(lines,false)?QuickMenuText.Find(lines,false):null;
                    if(logout!=null&&now-lastLogout>30000)
                    {
                        var checkedLines=MenuRead(bounds,cancelled);var verified=QuickMenuText.HasContext(checkedLines,false)?QuickMenuText.Find(checkedLines,false):null;ensure();var fresh=SessionObservation();
                        if(fresh!=null&&!fresh.InCombat&&!fresh.PlayerDead&&!fresh.PlayerGhost&&fresh.ChatOpenKnown&&!fresh.ChatOpen&&!fresh.Casting&&!fresh.Channeling&&verified!=null&&Math.Abs(verified.X-logout.X)<8&&Math.Abs(verified.Y-logout.Y)<8)
                        {var point=new Point(bounds.X+verified.X+verified.Width/2,bounds.Y+verified.Y+verified.Height/2);if(!bounds.Contains(point))throw new InvalidOperationException("下线按钮超出游戏窗口。");if(backend.IsKeyAlreadyDown(1)||backend.IsKeyAlreadyDown(2))throw new InvalidOperationException("等待用户松开鼠标。");Native.MoveMouse(point);ensure();try{Native.SendMouseButton(false,false);Thread.Sleep(80);}finally{Native.SendMouseButton(false,true);}lastLogout=now;report("已点击返回角色菜单，等待实际下线确认。");}
                    }
                    else if(online!=null&&now-lastEscape>30000&&now-lastLogout>35000)
                    {var fresh=SessionObservation();if(fresh!=null&&fresh.ChatOpenKnown&&!fresh.ChatOpen&&!fresh.InCombat&&!fresh.PlayerDead&&!fresh.PlayerGhost&&!fresh.Casting&&!fresh.Channeling){key(27);lastEscape=now;report("定时休息：打开ESC菜单，识别返回角色菜单按钮。");}}
                }
                if(now-lastNotice>30000){report("定时休息：等待可确认的角色菜单或有效上线状态；未确认时不继续输入。");lastNotice=now;}
                Thread.Sleep(500);
            }
        }
    }
}
