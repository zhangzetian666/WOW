using System;

namespace WoWProbe
{
    public sealed class GrindHealerApproach
    {
        readonly Func<FarmObservation> reader;
        readonly Action<ushort,int> pulse;
        readonly Action<int> wait;
        public string LastReason{get;private set;}
        public GrindHealerApproach(Func<FarmObservation> reader,Action<ushort,int> pulse,Action<int> wait)
        {
            if(reader==null||pulse==null||wait==null)throw new ArgumentNullException("healer approach dependencies");
            this.reader=reader;this.pulse=pulse;this.wait=wait;
        }
        public bool Run(BattleProfile profile,uint token,double targetX,double targetY,double stopDistance,int maximumPulses)
        {
            if(profile==null||profile.Calibration==null||profile.Movement==null)throw new ArgumentNullException("healer approach profile");
            if(!NavigationMath.InRange(stopDistance,.0001,.02)||maximumPulses<1||maximumPulses>1000)throw new ArgumentOutOfRangeException("healer approach bounds");
            var calibration=profile.Calibration.Clone();calibration.Validate();var movement=profile.Movement.Clone();movement.Validate();
            if(token==0||!NavigationMath.InRange(targetX,0,1)||!NavigationMath.InRange(targetY,0,1))return Fail("A known healer identity and position are required.");
            for(int step=0;step<maximumPulses;step++)
            {
                var o=reader();if(!Matches(o,token,targetX,targetY,calibration))return Fail("Healer identity, position or observation changed during approach.");
                if(Arrived(o,targetX,targetY,calibration,stopDistance))return Success();
                // Re-read after the planning boundary, before any turn or forward callback.
                o=reader();if(!Matches(o,token,targetX,targetY,calibration))return Fail("Healer context changed before movement.");
                if(Arrived(o,targetX,targetY,calibration,stopDistance))return Success();
                double bearing=Math.Atan2((targetX-o.X)*calibration.AxisScale,-(targetY-o.Y));
                double facing=NavigationMath.Normalize(NavigationMath.Normalize(calibration.FacingSign*o.Facing)+NavigationMath.Normalize(calibration.FacingOffset));
                double delta=NavigationMath.Normalize(bearing-facing);bool turn=Math.Abs(delta)>.10;
                if(turn)pulse(delta>0?movement.TurnRight:movement.TurnLeft,NavigationMath.Pulse(Math.Abs(delta)/calibration.TurnRadiansPerSecond*1000,60,220));
                else
                {
                    double distance=NavigationMath.Distance(targetX-o.X,targetY-o.Y,calibration.AxisScale);
                    pulse(movement.Forward,NavigationMath.Pulse(Math.Min(distance-stopDistance,.02)/calibration.ForwardUnitsPerSecond*1000,60,250));
                }
                o=reader();if(!Matches(o,token,targetX,targetY,calibration))return Fail("Healer context changed during movement; no further movement is authorized.");
                if(Arrived(o,targetX,targetY,calibration,stopDistance))return Success();
                wait(turn?90:130);
                o=reader();if(!Matches(o,token,targetX,targetY,calibration))return Fail("Healer context changed while waiting; no further movement is authorized.");
                if(Arrived(o,targetX,targetY,calibration,stopDistance))return Success();
            }
            return Fail("Healer approach exhausted its pulse limit without verified arrival; revive manually.");
        }
        static bool Matches(FarmObservation o,uint token,double x,double y,NavigationCalibration c)
        {
            return GrindRecoveryPolicy.CanApproachHealer(o)&&o.ReadFailures==0&&o.TargetDeadKnown&&!o.TargetDead&&o.TargetToken==token&&
                o.TargetX==x&&o.TargetY==y&&o.MapId==c.MapId&&o.MapMode==c.MapMode&&String.Equals(o.Zone,c.Zone,StringComparison.Ordinal)&&
                NavigationMath.InRange(o.X,0,1)&&NavigationMath.InRange(o.Y,0,1)&&NavigationMath.InRange(o.Facing,0,Math.PI*2);
        }
        static bool Arrived(FarmObservation o,double x,double y,NavigationCalibration c,double stop)
        {return NavigationMath.Distance(x-o.X,y-o.Y,c.AxisScale)<=stop;}
        bool Success(){LastReason="Arrival at the same verified healer position was observed.";return true;}
        bool Fail(string reason){LastReason=reason;return false;}
    }
}
