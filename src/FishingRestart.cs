using System;
namespace WoWProbe
{
    public sealed class FishingStayClock
    {
        readonly Func<int,int> random;public long DurationMs{get;private set;}public long DueAt{get;private set;}
        public FishingStayClock(Func<int,int> random=null){this.random=random??QuickRandom.Next;DueAt=-1;}
        public void Select(FishingRange range){DurationMs=range.DrawMilliseconds(60000,random);Depart();}
        public void Depart(){DueAt=-1;}
        public void Arrive(long now){DueAt=checked(now+DurationMs);}
        public bool Due(long now){return DueAt>=0&&now>=DueAt;}
    }
    // UI timer owns this schedule; a new worker can start only after the old one exits.
    public sealed class FishingRestartSchedule
    {
        public bool Armed{get;private set;}public bool ManualPending{get;private set;}public long DueAt{get;private set;}
        public FishingRestartSchedule(){DueAt=-1;}
        public void RequestStart(){Armed=true;ManualPending=true;DueAt=-1;}
        public void Cancel(){Armed=false;ManualPending=false;DueAt=-1;}
        public bool Ready(long now,bool workerAlive,bool retryable,bool enabled,int seconds)
        {
            if(workerAlive){DueAt=-1;return false;}
            if(ManualPending){ManualPending=false;DueAt=-1;return true;}
            if(!Armed||!retryable||!enabled){DueAt=-1;return false;}
            if(DueAt<0){DueAt=checked(now+seconds*1000L);return false;}
            if(now<DueAt)return false;DueAt=-1;return true;
        }
    }
}
