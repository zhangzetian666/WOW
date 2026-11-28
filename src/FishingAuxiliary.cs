using System;
namespace WoWProbe
{
    public enum FishingAuxiliaryKind { None, Bag, TimedKey }
    // A pair of B presses or a skill's cast window owns the idle interval between rods.
    // Foreground loss suspends input without forgetting a key that was already sent.
    public sealed class FishingAuxiliaryCycle
    {
        public FishingAuxiliaryKind Kind{get;private set;}int phase;ushort key;long readyAt,bagWaitMs=3000;
        public bool Active{get{return Kind!=FishingAuxiliaryKind.None;}}
        public void Start(FishingAuxiliaryKind kind,ushort value,long bagOpenMs=3000)
        {if(Active||kind==FishingAuxiliaryKind.None)throw new InvalidOperationException("已有定时动作正在进行。");if(bagOpenMs<100||bagOpenMs>3600000)throw new ArgumentOutOfRangeException("bagOpenMs");Kind=kind;key=value;phase=1;readyAt=0;bagWaitMs=bagOpenMs;}
        public ushort KeyDue(long now,bool casting)
        {if(!Active||casting)return 0;if(phase==1||Kind==FishingAuxiliaryKind.Bag&&phase==2&&now>=readyAt)return key;return 0;}
        public void InputSent(long now)
        {if(phase==1){phase=2;readyAt=checked(now+(Kind==FishingAuxiliaryKind.Bag?bagWaitMs:10000));}else if(Kind==FishingAuxiliaryKind.Bag&&phase==2&&now>=readyAt)phase=3;else throw new InvalidOperationException("定时按键顺序无效。");}
        public bool Finished(long now,bool casting)
        {return Active&&!casting&&(Kind==FishingAuxiliaryKind.Bag?phase==3:phase==2&&now>=readyAt);}
        public void Clear(){Kind=FishingAuxiliaryKind.None;phase=0;}
    }
    public sealed class FishingPeriodicKeyClock
    {
        readonly FishingClientSettings settings;readonly Func<int,int> random;public long NextDue{get;private set;}
        public FishingPeriodicKeyClock(FishingClientSettings settings,long now,Func<int,int> random=null)
        {this.settings=settings;this.random=random??QuickRandom.Next;Completed(now);}
        public bool Due(long now){return settings.TimedKeyEnabled&&now>=NextDue;}
        public void Completed(long now)
        {if(!settings.TimedKeyEnabled){NextDue=Int64.MaxValue;return;}long min=(long)Math.Round(settings.KeyMinutes.Min*60000),max=(long)Math.Round(settings.KeyMinutes.Max*60000);long delay=max-min>1?min+1+random(checked((int)(max-min-1))):min;NextDue=checked(now+delay);}
    }
    public static class FishingArrivalFacing
    {
        public static FishingCommand Next(FarmObservation current,FishingPoint target,NavigationCalibration calibration,RouteKeyBindings keys)
        {
            double delta=NavigationMath.Normalize(calibration.FacingSign*NavigationMath.Normalize(target.Facing-current.Facing));
            if(Math.Abs(delta)<=.10)return new FishingCommand{Action=FishingAction.None,Reason="已确认面向保存的水面方向。"};
            return new FishingCommand{Action=FishingAction.Key,Purpose=FishingPurpose.RestoreFacing,Key=delta>0?keys.TurnRight:keys.TurnLeft,HoldMilliseconds=FishingTravelPolicy.FacingHold(delta,calibration.TurnRadiansPerSecond),Reason=String.Format(System.Globalization.CultureInfo.InvariantCulture,"到点校正面水朝向：当前{0:F3} → 保存{1:F3}弧度。",current.Facing,target.Facing)};
        }
    }
}
