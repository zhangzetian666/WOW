using System;
using WoWProbe;
class MeleeApproachTests
{
    static FarmObservation O(double facing,uint token=1){return new FarmObservation{Fresh=true,KnownCore=true,TargetExists=true,TargetIdentityKnown=true,TargetToken=token,AttackabilityKnown=true,TargetCanAttack=true,TargetDeadKnown=true,ChatOpenKnown=true,Facing=facing,X=.5,Y=.5};}
    static void Check(bool value){if(!value)throw new Exception("Melee facing verification failed");}
    static int Main()
    {
        var a=new MeleeApproach();Check(!a.Observe(1,O(0),O(0)));Check(a.Observe(1,O(0),O(0)));
        Check(!a.Verify(1,O(0),O(.25),O(.25)));Check(!a.Verified(1));
        Check(!a.Verify(1,O(0),O(0),O(0)));Check(a.Verify(1,O(0),O(.25),O(.02)));Check(a.Verified(1));
        Check(!a.Verify(1,O(0),O(.25),O(0,2)));Check(!a.Verified(1));
        a.Observe(1,O(0),O(0));Check(a.Verify(1,O(6.26),O(.25),O(.01)));
        var combat=O(0);combat.InCombat=true;Check(!a.Verify(1,O(0),O(.25),combat));
        a.Observe(1,O(0),O(0));a.Verify(1,O(0),O(.25),O(0));Check(!a.Observe(2,O(0,2),O(0,2)));Check(!a.Verified(2));
        var moved=O(0,2);moved.X+=.001;Check(!a.Observe(2,O(0,2),moved));
        Console.WriteLine("PASS native facing restoration, unchanged heading rejection, identity/combat guards, wraparound and movement reset");return 0;
    }
}
