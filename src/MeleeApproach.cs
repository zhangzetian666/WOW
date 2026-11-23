using System;
namespace WoWProbe
{
    // Verify target interaction restores a deliberately changed character heading.
    // Camera position and nameplate screen coordinates are not heading evidence.
    public sealed class MeleeApproach
    {
        uint token;int stationary;bool verified;
        public bool Observe(uint target,FarmObservation before,FarmObservation after)
        {
            if(target!=token){token=target;stationary=0;verified=false;}
            if(!Valid(target,before)||!Valid(target,after)){stationary=0;verified=false;return false;}
            double dx=after.X-before.X,dy=after.Y-before.Y;
            stationary=dx*dx+dy*dy>1e-8?0:stationary+1;
            return stationary>=2;
        }
        public bool Verified(uint target){return token==target&&verified;}
        public bool Verify(uint target,FarmObservation original,FarmObservation turned,FarmObservation restored)
        {
            verified=target==token&&Valid(target,original)&&Valid(target,turned)&&Valid(target,restored)
                &&MouseLookInput.TurnObserved(original.Facing,turned.Facing)
                &&MouseLookInput.TurnObserved(turned.Facing,restored.Facing)
                &&Math.Abs(NavigationMath.Normalize(restored.Facing-original.Facing))<.12;
            return verified;
        }
        public static bool Valid(uint target,FarmObservation o)
        {return target!=0&&GrindRecoveryPolicy.CanAttack(o)&&o.ReadFailures==0&&!o.InCombat&&o.TargetToken==target&&!QuickCombat.Contested(o)&&o.ChatOpenKnown&&!o.ChatOpen;}
    }
}
