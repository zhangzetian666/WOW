using System;
namespace WoWProbe
{
    public sealed class QuickKillCounter
    {
        readonly object sync=new object();long total;int runMaximum;
        public long Total {get{lock(sync)return total;}}
        public void BeginRun(){lock(sync)runMaximum=0;}
        public void Record(int confirmed){if(confirmed<0)throw new ArgumentOutOfRangeException("confirmed");lock(sync){if(confirmed>runMaximum){total+=confirmed-runMaximum;runMaximum=confirmed;}}}
    }
}
