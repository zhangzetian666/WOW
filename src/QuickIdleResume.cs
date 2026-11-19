using System;
namespace WoWProbe
{
    public sealed class QuickIdleResume
    {
        public bool Pending {get;private set;}
        long interruptedAt;
        bool waitForIdle;
        public void Cancel(){Pending=false;}
        public void Interrupted(string reason,long now)
        {
            reason=reason??"";
            Pending=!reason.Contains("急停")&&reason.IndexOf("emergency",StringComparison.OrdinalIgnoreCase)<0;
            waitForIdle=reason.IndexOf("foreground",StringComparison.OrdinalIgnoreCase)>=0||reason.IndexOf("already holding",StringComparison.OrdinalIgnoreCase)>=0||reason.Contains("用户正在按")||reason.Contains("失焦");
            interruptedAt=now;
        }
        public bool Ready(long now,long idleMilliseconds,int seconds)
        {return Pending&&seconds>=5&&now>=interruptedAt&&now-interruptedAt>=(waitForIdle?seconds:30)*1000L&&(!waitForIdle||idleMilliseconds>=seconds*1000L);}
    }
}
