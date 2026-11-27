using System;
namespace WoWProbe
{
    public static class QuickPetInput
    {
        // Never synthesize a movement tap for an already stationary character.
        public static void Prepare(Func<FarmObservation> read,Action<int> wait,Action stopMovement)
        {
            double x=0,y=0;int sequence=-1,stable=0,frames=0;bool first=true,stopped=false;
            for(int elapsed=0;elapsed<=5000;elapsed+=100)
            {
                var o=read();
                if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0||o.PlayerDead||o.PlayerGhost||!o.ChatOpenKnown||o.ChatOpen||!o.CastingKnown||o.Casting||o.Channeling||Double.IsNaN(o.X)||Double.IsNaN(o.Y))
                    throw new InvalidOperationException("目标状态变化，停止当前按键。");
                if(o.Sequence!=sequence)
                {
                    bool moved=!first&&(Math.Abs(o.X-x)>.000001||Math.Abs(o.Y-y)>.000001);
                    if(first||moved){if(moved&&!stopped){stopMovement();stopped=true;}stable=elapsed;frames=0;x=o.X;y=o.Y;first=false;}
                    sequence=o.Sequence;frames++;
                    if(elapsed-stable>=1000&&frames>=3)return;
                }
                wait(100);
            }
            throw new InvalidOperationException("目标状态变化，停止当前按键。");
        }
    }
}
