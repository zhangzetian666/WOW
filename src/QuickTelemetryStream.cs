using System;
using System.Threading;
namespace WoWProbe
{
    internal sealed class QuickTelemetryStream:IDisposable
    {
        readonly object gate=new object();readonly Thread thread;readonly Func<long> clock;
        FarmObservation latest;long captured;volatile bool stopped;
        internal QuickTelemetryStream(Func<FarmObservation> capture,Func<long> clock,FarmObservation initial)
        {
            this.clock=clock;latest=initial;captured=clock();
            thread=new Thread(()=>{while(!stopped){long at=clock();var value=capture();lock(gate){latest=value;captured=at;}Thread.Sleep(60);}});
            thread.IsBackground=true;thread.Start();
        }
        internal FarmObservation Read()
        {lock(gate){long now=clock();if(now<captured||now-captured>=900)throw new InvalidOperationException("屏幕状态已超时。");return latest;}}
        public void Dispose(){stopped=true;thread.Join(1500);}
    }
}
