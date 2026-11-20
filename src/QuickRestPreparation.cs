using System;
namespace WoWProbe
{
    public static class QuickRestPreparation
    {
        public static void Wait(Func<FarmObservation> read,Action<int> wait)
        {
            double x=0,y=0;int sequence=-1,stable=0;bool first=true;
            for(int elapsed=0;elapsed<=5000;elapsed+=100)
            {
                var o=read();
                if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0||o.InCombat||o.PlayerDead||o.PlayerGhost||!o.ChatOpenKnown||o.ChatOpen||Double.IsNaN(o.X)||Double.IsNaN(o.Y))
                    throw new InvalidOperationException("目标状态变化，停止当前按键。");
                if(o.Sequence!=sequence)
                {
                    if(first||Math.Abs(o.X-x)>.000001||Math.Abs(o.Y-y)>.000001){stable=elapsed;first=false;x=o.X;y=o.Y;}
                    sequence=o.Sequence;
                    if(elapsed-stable>=1000)return;
                }
                wait(100);
            }
            throw new InvalidOperationException("进食前未确认连续静止1秒，等待重试。");
        }
    }
}
