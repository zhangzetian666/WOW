using System;
namespace WoWProbe
{
    public enum FishingLoginAction { Wait, OpenMenu, Logout, Enter, Ignore, Disconnect, Login, Complete }
    public sealed class FishingLoginFrame
    {
        public bool Foreground,Released,World,ReadyWorld,CanLogout;
        public int Sequence;public QuickScreenText[] Lines=new QuickScreenText[0];
    }
    public sealed class FishingLoginCommand
    {
        public FishingLoginAction Action;public QuickScreenText Button;public string Reason;
    }
    public interface IFishingLoginHost
    {
        bool Foreground{get;}bool ModifiersReleased{get;}bool InputFaulted{get;}
        void CheckCancelled();void Idle(int milliseconds);void Destination();
        bool TryReturnFocus(bool enabled,int seconds);
        FishingLoginFrame ReadLoginFrame();void ForgetPanels();
        void MenuKey(ushort key);void MenuClick(QuickScreenText button);
    }
    // Decisions and transitions are separate from native input so the entire round trip is testable.
    public sealed class FishingLoginFlow
    {
        readonly FishingActivityClock schedule;long logoutAt=-1,enterAt=-1,lastAction=-60000,worldSince=-1;int worldSequence,loginAttempts;
        public FishingLoginFlow(FishingActivityClock schedule){this.schedule=schedule;}
        FishingLoginCommand C(FishingLoginAction action,string reason,QuickScreenText button=null){return new FishingLoginCommand{Action=action,Reason=reason,Button=button};}
        public FishingLoginCommand Step(FishingLoginFrame f,long now)
        {
            if(!f.Foreground||!f.Released){worldSince=-1;return C(FishingLoginAction.Wait,"上下线任务保留：等待游戏前台及按键松开。");}
            if(schedule.Phase=="Offline"&&now<schedule.OfflineDue)return C(FishingLoginAction.Wait,"离线等待，剩余"+Math.Ceiling((schedule.OfflineDue-now)/60000.0)+"分钟。");
            bool select=QuickMenuText.HasContext(f.Lines,true)&&QuickMenuText.Find(f.Lines,true)!=null;
            bool login=FishingLoginScreen.Login(f.Lines),disconnected=FishingLoginScreen.Disconnected(f.Lines);
            if(schedule.Phase=="Logout")
            {
                if(logoutAt>=0&&now-logoutAt<20000)return C(FishingLoginAction.Wait,"已点击登出，等待20秒；期间不按ESC、不移动。");
                if((select||login||disconnected)&&!f.World){schedule.OfflineConfirmed(now);return C(FishingLoginAction.Wait,"已确认离开角色，开始随机离线等待。");}
                if(FishingLoginScreen.LogoutCountdown(f.Lines)||logoutAt>=0&&now-logoutAt<35000)return C(FishingLoginAction.Wait,"登出等待：尚未确认角色选择界面，不打断倒计时或加载。");
                if(now-lastAction<1000)return C(FishingLoginAction.Wait,"等待登出菜单更新。");
                if(f.CanLogout&&QuickMenuText.HasContext(f.Lines,false)){
                    var button=FishingLoginScreen.Find(f.Lines,"登出","返回角色选择","返回角色菜单","返回角色列表","logout");
                    if(button!=null)return C(FishingLoginAction.Logout,"点击已确认的登出按钮，开始20秒等待。",button);
                }
                else if(f.CanLogout)return C(FishingLoginAction.OpenMenu,"打开ESC菜单，准备登出。");
                return C(FishingLoginAction.Wait,"等待可登出的角色状态或已完成的下线画面。");
            }
            if(schedule.Phase=="Offline")schedule.Phase="Login";
            var ignore=FishingLoginScreen.AddonIgnore(f.Lines);
            if(ignore!=null){worldSince=-1;return now-lastAction<2000?C(FishingLoginAction.Wait,"等待插件错误弹窗关闭。"):C(FishingLoginAction.Ignore,"识别到插件/Lua错误，点击“忽略”。",ignore);}
            if(f.World){
                if(!f.ReadyWorld){worldSince=-1;return C(FishingLoginAction.Wait,"已进入游戏，等待弹窗、光标和角色状态恢复。");}
                if(worldSince<0){worldSince=now;worldSequence=f.Sequence;}
                if(now-worldSince<3000||worldSequence==f.Sequence)return C(FishingLoginAction.Wait,"已进入游戏，核对3秒稳定画面及登录弹窗。");
                schedule.Online(now);return C(FishingLoginAction.Complete,"已确认上线且插件弹窗已清除，重新计时并恢复钓鱼。");
            }
            worldSince=-1;
            if(now-lastAction<1000)return C(FishingLoginAction.Wait,"等待登录界面更新。");
            if(select&&(enterAt<0||now-enterAt>=30000))return C(FishingLoginAction.Enter,"已确认角色选择界面，回车进入当前选中角色。");
            if(disconnected)return C(FishingLoginAction.Disconnect,"关闭已识别的断线提示。",FishingLoginScreen.Find(f.Lines,"确定","确认","okay","ok"));
            if(login&&loginAttempts<3)return C(FishingLoginAction.Login,"点击已有登录按钮，不输入或保存账号密码。",FishingLoginScreen.Find(f.Lines,"登录","登入","登录游戏","log in","login"));
            return C(FishingLoginAction.Wait,login?"等待账号登录条件满足，不输入密码或验证码。":"等待角色加载；不重复按回车。");
        }
        public void Sent(FishingLoginCommand command,long now)
        {
            lastAction=now;worldSince=-1;
            if(command.Action==FishingLoginAction.Logout)logoutAt=now;
            if(command.Action==FishingLoginAction.Enter)enterAt=now;
            if(command.Action==FishingLoginAction.Login)loginAttempts++;
        }
        public static bool Same(FishingLoginCommand a,FishingLoginCommand b)
        {return a.Action==b.Action&&(a.Button==null?b.Button==null:FishingLoginScreen.Same(a.Button,b.Button));}
    }
}
