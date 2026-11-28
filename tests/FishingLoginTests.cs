using System;
using System.Collections.Generic;
using WoWProbe;
class FishingLoginTests
{
    static int passed,failed;
    static void Check(bool yes){if(!yes)throw new Exception("Unexpected login behavior");}
    static void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static QuickScreenText L(string text,int x=10){return new QuickScreenText{Text=text,X=x,Y=10,Width=120,Height=25};}
    static FishingActivityClock Clock(){var s=new FishingClientSettings{TimedSessions=true,OfflineHours=new FishingRange(.01,.01)};var c=new FishingActivityClock(s,0,n=>0);c.LogoutRequested();return c;}
    static FishingLoginFrame F(bool world,params string[] lines){var f=new FishingLoginFrame{Foreground=true,Released=true,World=world,ReadyWorld=world,CanLogout=world,Sequence=1};var all=new List<QuickScreenText>();foreach(var t in lines)all.Add(L(t));f.Lines=all.ToArray();return f;}
    static int Main()
    {
        Test("logout click owns twenty seconds with no repeated Escape or click",()=>{
            var clock=Clock();var flow=new FishingLoginFlow(clock);var menu=F(true,"返回游戏","登出");var action=flow.Step(menu,100);Check(action.Action==FishingLoginAction.Logout);flow.Sent(action,100);
            foreach(long time in new[]{101L,10100,20099})Check(flow.Step(menu,time).Action==FishingLoginAction.Wait&&clock.Phase=="Logout");
            var select=F(false,"创建角色","进入游戏");flow.Step(select,20100);Check(clock.Phase=="Offline"&&clock.OfflineDue==56100);
        });
        Test("even instant logout waits twenty seconds before starting offline timer",()=>{var clock=Clock();var flow=new FishingLoginFlow(clock);flow.Sent(new FishingLoginCommand{Action=FishingLoginAction.Logout},0);var f=F(false,"创建角色","进入游戏");flow.Step(f,1000);Check(clock.Phase=="Logout");flow.Step(f,20000);Check(clock.Phase=="Offline"&&clock.OfflineDue==56000);});
        Test("visible countdown and unknown loading screen never receive Escape",()=>{
            var flow=new FishingLoginFlow(Clock());flow.Sent(new FishingLoginCommand{Action=FishingLoginAction.Logout},0);
            Check(flow.Step(F(true,"你将在20秒后登出","取消"),50000).Action==FishingLoginAction.Wait);
            Check(flow.Step(F(false),60000).Action==FishingLoginAction.Wait);
        });
        Test("ignore requires addon error context and one unambiguous button",()=>{
            Check(FishingLoginScreen.AddonIgnore(new[]{L("忽略")})==null);
            Check(FishingLoginScreen.AddonIgnore(new[]{L("插件出现错误"),L("忽略")})!=null);
            Check(FishingLoginScreen.AddonIgnore(new[]{L("Lua error"),L("Ignore")})!=null);
            Check(FishingLoginScreen.AddonIgnore(new[]{L("插件出现错误"),L("忽略"),L("忽略",200)})==null);
            Check(!FishingLoginScreen.Same(L("忽略"),L("忽略",30)));
        });
        Test("user supplied protected UI popup matches wrapped text and only chooses ignore",()=>{
            var lines=new[]{L("WoWProbe已被禁用，因为该功能只对暴"),L("雪的UI开放。"),L("你可以禁用这个插件并重新载入UI。"),L("禁用"),L("忽略",200)};
            var button=FishingLoginScreen.AddonIgnore(lines);Check(button!=null&&button.Text=="忽略"&&button.X==200);
            Check(FishingLoginScreen.AddonIgnore(new[]{L("WoWProbe已被禁用，因为该功能只对暴雪的UI开放"),L("禁用")})==null);
            Check(FishingLoginScreen.AddonIgnore(new[]{L("WoWProbetik 禁 用 ， 因 为 该 功 能 只 对 暴"),L("雪 的 IJI 开 放"),L("你 可 以 禁 用 这 个 插 件 并 重 新 装 载 l-ll 。"),L("禁 用"),L("忽 略",258)}).X==258);
        });
        Test("login enters current character once then waits thirty seconds before any retry",()=>{
            var clock=Clock();clock.OfflineConfirmed(0);var flow=new FishingLoginFlow(clock);var f=F(false,"删除角色","进入游戏");Check(flow.Step(f,35999).Action==FishingLoginAction.Wait);
            var enter=flow.Step(f,36000);Check(enter.Action==FishingLoginAction.Enter);flow.Sent(enter,36000);
            Check(flow.Step(f,65999).Action==FishingLoginAction.Wait);Check(flow.Step(f,66000).Action==FishingLoginAction.Enter);
        });
        Test("popup blocks fishing and late popup resets world stability",()=>{
            var clock=Clock();clock.OfflineConfirmed(0);var flow=new FishingLoginFlow(clock);var ready=F(true);flow.Step(ready,36000);ready.Sequence++;
            var popup=F(true,"插件出现大量错误","忽略");Check(flow.Step(popup,38000).Action==FishingLoginAction.Ignore&&clock.Phase!="Online");flow.Sent(flow.Step(popup,38000),38000);
            flow.Step(ready,40000);ready.Sequence++;Check(flow.Step(ready,42999).Action==FishingLoginAction.Wait);Check(flow.Step(ready,43000).Action==FishingLoginAction.Complete&&clock.Phase=="Online");
        });
        Test("unknown cursor or repeated world frame cannot confirm online",()=>{
            var clock=Clock();clock.OfflineConfirmed(0);var flow=new FishingLoginFlow(clock);var f=F(true);f.ReadyWorld=false;Check(flow.Step(f,36000).Action==FishingLoginAction.Wait);f.ReadyWorld=true;flow.Step(f,37000);Check(flow.Step(f,41000).Action==FishingLoginAction.Wait);f.Sequence++;Check(flow.Step(f,41001).Action==FishingLoginAction.Complete);
        });
        Test("focus and held modifier preserve logout stage without input",()=>{
            var clock=Clock();var flow=new FishingLoginFlow(clock);var f=F(true,"返回游戏","登出");f.Foreground=false;Check(flow.Step(f,50000).Action==FishingLoginAction.Wait);f.Foreground=true;f.Released=false;Check(flow.Step(f,60000).Action==FishingLoginAction.Wait&&clock.Phase=="Logout");
        });
        Test("runner completes menu countdown offline enter ignore and stable world in order",()=>{var c=Clock();var h=new Fake();new FishingLoginRunner(h,c,()=>h.Now,()=>false,s=>{}).Run();Check(c.Phase=="Online"&&h.Actions.Count==4&&String.Join(",",h.Actions.ToArray())=="ESC,Logout,Enter,Ignore"&&h.EnterAt>=h.LogoutAt+20000+36000&&h.Now>=h.IgnoreAt+3000);});
        Test("runner retains twenty second countdown across foreground and OCR interruptions",()=>{var c=Clock();var h=new Fake{Interrupt=true};new FishingLoginRunner(h,c,()=>h.Now,()=>false,s=>{},true,1).Run();Check(c.Phase=="Online"&&h.FocusAttempts>0&&h.Actions.Count==4&&h.EnterAt>=h.LogoutAt+56000);});
        Test("runner does not swallow emergency cancellation",()=>{var c=Clock();var h=new Fake{Cancel=true};bool rejected=false;try{new FishingLoginRunner(h,c,()=>h.Now,()=>false,s=>{}).Run();}catch(OperationCanceledException){rejected=true;}Check(rejected&&h.Actions.Count==0);});
        Console.WriteLine("Fishing login tests: "+passed+" passed, "+failed+" failed. Fake host and virtual time only.");return failed==0?0:1;
    }
    sealed class Fake:IFishingLoginHost
    {
        public long Now,LogoutAt=-1,EnterAt=-1,IgnoreAt;public int FocusAttempts;public bool Interrupt,Cancel;bool restored,thrown;int sequence;string stage="World";
        public readonly List<string> Actions=new List<string>();
        public bool Foreground{get{return !(Interrupt&&stage=="Countdown"&&!restored);}}public bool ModifiersReleased{get{return true;}}public bool InputFaulted{get{return false;}}
        public void CheckCancelled(){if(Cancel)throw new OperationCanceledException();if(Now>180000)throw new Exception("Fake login timed out");}
        public void Idle(int ms){Now+=ms;CheckCancelled();}
        public void Destination(){if(!Foreground)throw new InvalidOperationException("游戏失去前台；切回游戏后继续。");}
        public bool TryReturnFocus(bool enabled,int seconds){FocusAttempts++;if(enabled&&Now>=LogoutAt+5000)restored=true;return Foreground;}
        public void ForgetPanels(){}
        public FishingLoginFrame ReadLoginFrame(){
            if(Interrupt&&stage=="Countdown"&&!thrown){thrown=true;throw new InvalidOperationException("菜单识别超时。");}
            if(stage=="Countdown"&&Now>=LogoutAt+20000)stage="Select";
            if(stage=="Loading"&&Now>=EnterAt+1500)stage="Popup";
            var f=stage=="Menu"?F(true,"返回游戏","登出"):stage=="Countdown"?F(true,"将在20秒后登出","取消"):stage=="Select"?F(false,"创建角色","进入游戏"):stage=="Loading"?F(false):stage=="Popup"?F(true,"插件出现Lua错误","忽略"):F(true);
            f.Sequence=++sequence;if(stage=="Popup")f.ReadyWorld=false;return f;
        }
        public void MenuKey(ushort key){Destination();if(key==27){Actions.Add("ESC");stage="Menu";}else if(key==13){Actions.Add("Enter");stage="Loading";EnterAt=Now;}else throw new Exception("Unexpected key");}
        public void MenuClick(QuickScreenText button){Destination();if(button.Text=="登出"){Actions.Add("Logout");stage="Countdown";LogoutAt=Now;}else if(button.Text=="忽略"){Actions.Add("Ignore");stage="World";IgnoreAt=Now;}else throw new Exception("Unexpected button");}
    }
}
