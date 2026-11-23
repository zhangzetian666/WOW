using System;
namespace WoWProbe
{
    // Continue the heading observed during actual native target approach.
    // Camera position and nameplate screen coordinates are not heading evidence.
    public sealed class MeleeApproach
    {
        uint token;bool nativeMovement;double facing;
        public bool Observe(uint target,FarmObservation before,FarmObservation after)
        {
            if(target!=token){token=target;nativeMovement=false;}
            if(!Valid(target,before)||!Valid(target,after)){nativeMovement=false;return false;}
            double dx=after.X-before.X,dy=after.Y-before.Y;
            if(dx*dx+dy*dy>1e-8){nativeMovement=true;facing=after.Facing;return false;}
            if(nativeMovement&&Math.Abs(NavigationMath.Normalize(after.Facing-facing))>=.12)nativeMovement=false;
            return nativeMovement;
        }
        public static bool Valid(uint target,FarmObservation o)
        {return target!=0&&GrindRecoveryPolicy.CanAttack(o)&&o.ReadFailures==0&&!o.InCombat&&o.TargetToken==target&&!QuickCombat.Contested(o)&&o.ChatOpenKnown&&!o.ChatOpen;}
    }
}
