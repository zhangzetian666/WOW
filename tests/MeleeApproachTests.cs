using System;
using WoWProbe;
class MeleeApproachTests
{
    static FarmObservation O(double facing,uint token=1){return new FarmObservation{Fresh=true,KnownCore=true,TargetExists=true,TargetIdentityKnown=true,TargetToken=token,AttackabilityKnown=true,TargetCanAttack=true,TargetDeadKnown=true,ChatOpenKnown=true,Facing=facing,X=.5,Y=.5};}
    static void Check(bool value){if(!value)throw new Exception("Melee facing verification failed");}
    static int Main()
    {
        var a=new MeleeApproach();Check(!a.Observe(1,O(0),O(0)));Check(!a.Observe(1,O(0),O(0)));
        var moved=O(0);moved.Y-=.001;Check(!a.Observe(1,O(0),moved));Check(a.Observe(1,moved,moved));
        Check(!a.Observe(1,O(.3),O(.3)));Check(!a.Observe(1,O(0),O(0)));
        a.Observe(1,O(0),moved);var combat=O(0);combat.InCombat=true;Check(!a.Observe(1,moved,combat));Check(!a.Observe(1,O(0),O(0)));
        a.Observe(1,O(0),moved);Check(!a.Observe(2,O(0,2),O(0,2)));
        moved=O(6.26,2);moved.Y-=.001;a.Observe(2,O(6.26,2),moved);Check(a.Observe(2,O(.01,2),O(.01,2)));
        var stale=O(.01,2);stale.Fresh=false;Check(!a.Observe(2,stale,stale));
        Console.WriteLine("PASS native movement required, facing/target/combat/freshness changes revoke continuation, and wraparound");return 0;
    }
}
