using System;
namespace WoWProbe
{
    // A recovered optional stream starts a fresh baseline, without replaying old events.
    public sealed class FishingSocialGate
    {
        public bool Available{get;private set;}
        public int HandledLap{get;private set;}
        public bool Observe(FishingSocialObservation info,int lap,FishingWhisperPolicy replies)
        {
            bool wasAvailable=Available;Available=info!=null&&info.Known;
            if(!Available||!wasAvailable){HandledLap=lap;if(Available)replies.Discard(info.WhisperCount);}
            return Available;
        }
        public void LapSent(int lap){HandledLap=lap;}
    }
    public sealed class FishingSocialUnavailableException:InvalidOperationException
    {
        public FishingSocialUnavailableException():base("聊天扩展反馈暂不可读，本次聊天已跳过，继续钓鱼。"){}
        public static bool Caused(Exception error){for(var current=error;current!=null;current=current.InnerException)if(current is FishingSocialUnavailableException)return true;return false;}
    }
}
