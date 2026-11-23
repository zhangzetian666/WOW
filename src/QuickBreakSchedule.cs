using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace WoWProbe
{
    public sealed class QuickBreakSchedule
    {
        public long OnlineMilliseconds,OfflineUntil;
        public int[] ExtraPool;
        public int ExtraSeconds,OfflineMinutes;
        public string Phase="Online";
        long last=-1,lastSave;bool active;
        public static long Now {get{return DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond;}}
        public static long ClockNow {get{return (long)(System.Diagnostics.Stopwatch.GetTimestamp()*1000.0/System.Diagnostics.Stopwatch.Frequency);}}
        public long OnlineLimit {get{return 7200000L+ExtraSeconds*1000L;}}
        public bool Due {get{return OnlineMilliseconds>=OnlineLimit;}}
        public QuickBreakSchedule()
        {
            var values=new HashSet<int>();while(values.Count<100)values.Add(1+QuickRandom.Next(7200));ExtraPool=new int[100];values.CopyTo(ExtraPool);ExtraSeconds=ExtraPool[QuickRandom.Next(100)];
        }
        public void Activate(long now){last=now;active=true;}
        public void Tick(long now){if(now<last)return;if(active&&last>=0&&Phase=="Online")OnlineMilliseconds+=now-last;last=now;}
        public void Pause(long now){Tick(now);active=false;}
        public bool RequestLogout(bool inCombat,bool dead,bool casting)
        {if(Phase!="Online"||!Due||inCombat||dead||casting)return false;Phase="Logout";return true;}
        public void ConfirmOffline(long now)
        {if(Phase!="Logout")return;OfflineMinutes=new[]{13,15,18}[QuickRandom.Next(3)];OfflineUntil=now+OfflineMinutes*60000L;Phase="Offline";}
        public void ConfirmOnline(long now)
        {int old=ExtraSeconds;do{ExtraSeconds=ExtraPool[QuickRandom.Next(ExtraPool.Length)];}while(ExtraSeconds==old);OnlineMilliseconds=0;OfflineUntil=0;Phase="Online";last=now;active=true;}
        public string Summary {get{return "本轮在线上限：2小时 + "+(ExtraSeconds/60)+"分"+(ExtraSeconds%60)+"秒；售卖、修理、跑尸均计时。";}}
        public void Save(string path,bool force=false)
        {
            long now=ClockNow;if(!force&&now-lastSave<30000)return;lastSave=now;
            string temp=path+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
        public static QuickBreakSchedule Load(string path)
        {
            if(!File.Exists(path))return new QuickBreakSchedule();var s=new JavaScriptSerializer().Deserialize<QuickBreakSchedule>(File.ReadAllText(path));
            if(s==null||s.ExtraPool==null||s.ExtraPool.Length!=100||new HashSet<int>(s.ExtraPool).Count!=100||Array.Exists(s.ExtraPool,n=>n<1||n>7200)||Array.IndexOf(s.ExtraPool,s.ExtraSeconds)<0||s.OnlineMilliseconds<0||!(s.Phase=="Online"||s.Phase=="Logout"||s.Phase=="Offline"||s.Phase=="Login"))throw new FormatException("定时休息记录无效，请检查quick-session.json。");return s;
        }
    }
    public sealed class QuickScreenText
    {public string Text;public int X,Y,Width,Height;}
    public static class QuickMenuText
    {
        static string Clean(string s){return (s??"").Replace(" ","").Replace("\r","").Replace("\n","").ToLowerInvariant();}
        public static bool HasContext(QuickScreenText[] lines,bool enter)
        {foreach(var line in lines??new QuickScreenText[0]){string s=Clean(line.Text);if(enter?(s=="创建角色"||s=="创建新角色"||s=="创建新的角色"||s=="删除角色"||s=="更改服务器"||s=="选择服务器"||s=="createcharacter"||s=="deletecharacter"):(s=="返回游戏"||s=="返回魔兽世界"||s=="returntogame"))return true;}return false;}
        public static QuickScreenText Find(QuickScreenText[] lines,bool enter)
        {
            QuickScreenText found=null;foreach(var line in lines??new QuickScreenText[0])
            {
                string s=Clean(line.Text);bool match=enter?(s=="进入魔兽世界"||s=="进入游戏"||s=="进入世界"||s=="enterworld"):(s=="返回角色选择"||s=="返回角色菜单"||s=="返回角色列表"||s=="logout");
                if(match){if(found!=null)return null;found=line;}
            }
            return found;
        }
    }
}
