using System;
using System.Drawing;
using WoWProbe;
class QuickRecoveryTests
{
    static int passed,failed;
    static void Check(bool b){if(!b)throw new Exception("Unexpected recovery behavior");}
    static OperationObservation O(){return new OperationObservation{Sequence=1,Fresh=true,GeometryKnown=true,ReleaseKnown=true,ReleaseVisible=true,ReleaseEnabled=true,ReleaseRegion=new Rectangle(100,100,100,30),ResurrectKnown=true,ResurrectRegion=new Rectangle(200,100,100,30),RecoveryDelayKnown=true,RecoveryDelayMs=0,CorpseKnown=true,CorpseX=.35,CorpseY=.52,Farm=new FarmObservation{Fresh=true,KnownCore=true,MapId=1432,MapMode=1,Zone="test",PlayerDead=true,Health=0,X=.32,Y=.47,Facing=0,Sequence=1}};}
    static QuickRecovery Engine(OperationObservation o){var r=new RouteDocument{RouteId="test",Name="test",RouteType="corpse_run",MapId=1432,MapMode=1,Zone="test"};r.Points.Add(new Waypoint{X=.32,Y=.47});r.Points.Add(new Waypoint{X=.344,Y=.512});return new QuickRecovery(r,new NavigationCalibration{MapId=1432,MapMode=1,Zone="test",TurnRadiansPerSecond=5.85},o.Farm);}
    static QuickRecoveryCommand Step(QuickRecovery e,OperationObservation o,long now){o.Sequence++;o.Farm.Sequence=o.Sequence;return e.Step(o,now);}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static int Main()
    {
        Test("startup route availability agrees with map mode zone and corpse purpose",()=>{var o=O().Farm;var r=new RouteDocument{RouteType="corpse_run",MapId=o.MapId,MapMode=o.MapMode,Zone=o.Zone};Check(QuickRecovery.RouteMatches(r,o));r.MapId++;Check(!QuickRecovery.RouteMatches(r,o));r.MapId=o.MapId;r.MapMode++;Check(!QuickRecovery.RouteMatches(r,o));r.MapMode=o.MapMode;r.Zone="another";Check(!QuickRecovery.RouteMatches(r,o));r.Zone=o.Zone;r.RouteType="farm";Check(!QuickRecovery.RouteMatches(r,o));Check(!QuickRecovery.RouteMatches(null,o));});
        Test("release is a single click in the observed button and ghost travel follows",()=>{var o=O();var e=Engine(o);var c=Step(e,o,0);Check(c.Action==QuickRecoveryAction.Click&&c.Release&&QuickRecovery.Matches(c,o));Check(Step(e,o,1000).Action==QuickRecoveryAction.None);o.Farm.PlayerGhost=true;Check(!QuickRecovery.Matches(c,o));Check(Step(e,o,2000).Action==QuickRecoveryAction.None);Check(Step(e,o,2200).Action!=QuickRecoveryAction.Stop);});
        Test("known zero cooldown and actual button are required to resurrect",()=>{var o=O();o.Farm.PlayerGhost=true;o.Farm.X=.344;o.Farm.Y=.512;var e=Engine(o);o.ResurrectVisible=o.ResurrectEnabled=true;o.RecoveryDelayMs=30000;Check(Step(e,o,0).Action!=QuickRecoveryAction.Click);o.RecoveryDelayMs=0;var c=Step(e,o,1000);Check(c.Action==QuickRecoveryAction.Click&&!c.Release&&QuickRecovery.Matches(c,o));o.ResurrectRegion=new Rectangle(20,20,40,40);Check(!QuickRecovery.Matches(c,o));});
        Test("two distinct positive living frames complete recovery",()=>{var o=O();var e=Engine(o);o.Farm.PlayerDead=false;o.Farm.Health=50;Check(Step(e,o,0).Action==QuickRecoveryAction.None);Check(e.Step(o,40).Action==QuickRecoveryAction.None);Check(Step(e,o,200).Action==QuickRecoveryAction.Complete);});
        Test("unknown corpse stale packets wrong maps and different graveyards stop",()=>{for(int i=0;i<4;i++){var o=O();o.Farm.PlayerGhost=true;var e=Engine(o);if(i==0)o.CorpseKnown=false;if(i==1)o.Fresh=false;if(i==2)o.Farm.MapId++;if(i==3)o.Farm.X=.5;Check(Step(e,o,0).Action==QuickRecoveryAction.Stop);}});
        Test("corpse identity cannot move during the same recovery and clicks time out",()=>{var o=O();o.Farm.PlayerGhost=true;var e=Engine(o);Step(e,o,0);o.CorpseX+=.01;Check(Step(e,o,200).Action==QuickRecoveryAction.Stop);o=O();e=Engine(o);Step(e,o,0);Check(Step(e,o,11000).Action==QuickRecoveryAction.Stop);});
        Console.WriteLine("Quick recovery tests: "+passed+" passed, "+failed+" failed. Simulated observations only.");return failed==0?0:1;
    }
}
