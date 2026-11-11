using System;
using System.Collections.Generic;
using System.Drawing;
using WoWProbe;

internal static class RecoveryWorkflowTests
{
    static int passed,failed;
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Equal<T>(T a,T b,string why){Check(EqualityComparer<T>.Default.Equals(a,b),why+" expected="+a+" actual="+b);}
    static void Reject(Action body){try{body();}catch(InvalidOperationException){return;}catch(ArgumentException){return;}catch(FormatException){return;}throw new Exception("expected rejection");}
    static RouteDocument Route(string id,string kind,params double[] xy){var r=new RouteDocument{RouteId=id,Name=id,RouteType=kind,MapId=12,MapMode=1,Zone="Test zone"};for(int i=0;i<xy.Length;i+=2)r.Points.Add(new Waypoint{X=xy[i],Y=xy[i+1],Tolerance=.0005});return r;}
    static CampaignPlan Plan()
    {
        return new CampaignPlan{FarmRoute=Route("farm","farm",.50,.50,.52,.50,.52,.52,.50,.52),CorpseRoute=Route("corpse","corpse_run",.45,.45,.45,.50,.50,.50),VendorRoute=Route("vendor","vendor_return",.50,.50,.50,.45,.45,.45),Battle=new BattleProfile{Targets=new List<BattleTarget>{new BattleTarget{Name="Monster",NameHash=TargetCatalog.HashName("Monster")},new BattleTarget{Name="Merchant",NameHash=TargetCatalog.HashName("Merchant"),Kind="npc"}},Calibration=new NavigationCalibration{MapId=12,MapMode=1,Zone="Test zone"}},Settings=new CampaignSettings()};
    }
    static OperationObservation Observe(int sequence,bool dead,bool ghost,double x,double y)
    {
        return new OperationObservation{Sequence=sequence,Fresh=true,MapId=12,FreeSlotsKnown=true,FreeSlots=10,GeometryKnown=true,ClientWidth=1920,ClientHeight=1080,LayoutToken=1,CorpseKnown=true,CorpseX=.51,CorpseY=.50,ReleaseKnown=true,ReleaseVisible=dead&&!ghost,ReleaseEnabled=true,ReleaseRegion=new Rectangle(100,100,100,30),ResurrectKnown=true,ResurrectVisible=ghost,ResurrectEnabled=true,ResurrectRegion=new Rectangle(200,100,100,30),RecoveryDelayKnown=true,RecoveryDelayMs=0,IncomingResurrectionKnown=true,Farm=new FarmObservation{Sequence=sequence,Fresh=true,KnownCore=true,MapId=12,MapMode=1,Zone="Test zone",X=x,Y=y,Facing=0,PlayerDead=dead,PlayerGhost=ghost,Health=dead?0:90,Power=90,PowerType=0,CastingKnown=true}};
    }
    static void Next(OperationObservation o){o.Sequence=(o.Sequence+1)&65535;o.Farm.Sequence=o.Sequence;}
    static bool Has(RouteDocument route,double x,double y){foreach(var p in route.Points)if(Math.Abs(p.X-x)<1e-9&&Math.Abs(p.Y-y)<1e-9)return true;return false;}
    static int Main()
    {
        Test("vendor outward travel follows recorded farm vertices before joining NPC route",delegate{var p=Plan();var o=Observe(1,false,false,.52,.51);var r=TravelPlanner.ToVendor(p,o,3);r.Validate();Check(Has(r,.52,.50)&&Has(r,.50,.50)&&Has(r,.50,.45),"recorded corner omitted");Equal(.45,r.Points[r.Points.Count-1].X,"vendor endpoint");Check(!Has(p.FarmRoute,.52,.51),"source route mutated");});
        Test("vendor return reverses NPC route then follows farm to exact previous anchor",delegate{var r=TravelPlanner.FromVendor(Plan(),3);r.Validate();Equal(.45,r.Points[0].X,"vendor origin");Check(Has(r,.50,.45)&&Has(r,.50,.50)&&Has(r,.52,.50),"return shortcut invented");Equal(.52,r.Points[r.Points.Count-1].X,"anchor x");Equal(.52,r.Points[r.Points.Count-1].Y,"anchor y");});
        Test("farm reverse changes the anchor and traversal order without changing source",delegate{var p=Plan();p.FarmReverse=true;p.VendorRoute.Points[0].Y=.52;p.CorpseRoute.Points[p.CorpseRoute.Points.Count-1].Y=.52;var r=TravelPlanner.FromVendor(p,2);Equal(.52,r.Points[r.Points.Count-1].X,"reverse anchor x");Equal(.52,r.Points[r.Points.Count-1].Y,"reverse anchor y");Equal(.50,p.FarmRoute.Points[0].Y,"original order retained");});
        Test("travel rejects distant positions changed maps and invalid saved indices",delegate{var p=Plan();Reject(delegate{TravelPlanner.ToVendor(p,Observe(1,false,false,.8,.8),2);});var o=Observe(1,false,false,.51,.5);o.MapId=13;Reject(delegate{TravelPlanner.ToVendor(p,o,2);});Reject(delegate{TravelPlanner.FromVendor(p,-1);});Reject(delegate{TravelPlanner.FromVendor(p,4);});});
        Test("corpse travel requires actual ghost known corpse and reachable recorded corridor",delegate{var p=Plan();var o=Observe(1,true,true,.45,.45);var r=TravelPlanner.ToCorpse(p,o,2);r.Validate();Check(Has(r,.45,.50)&&Has(r,.50,.50)&&Has(r,.51,.50),"corpse recorded connection missing");o.CorpseKnown=false;Reject(delegate{TravelPlanner.ToCorpse(p,o,2);});o.CorpseKnown=true;o.CorpseY=.8;Reject(delegate{TravelPlanner.ToCorpse(p,o,2);});o=Observe(1,false,false,.45,.45);Reject(delegate{TravelPlanner.ToCorpse(p,o,2);});});
        Test("corpse route start must join graveyard and cannot use death snapshot as corpse evidence",delegate{var p=Plan();var o=Observe(1,true,true,.1,.1);Reject(delegate{TravelPlanner.ToCorpse(p,o,2);});o=Observe(1,true,true,.45,.45);o.CorpseX=Double.NaN;Reject(delegate{TravelPlanner.ToCorpse(p,o,2);});});
        Test("already at recorded recovery anchor yields no fabricated degenerate route",delegate{var p=Plan();var o=Observe(1,false,false,.52,.50);Check(TravelPlanner.FromCorpse(p,o,2)==null,"already at anchor requires no travel");o.Farm.X=.51;var r=TravelPlanner.FromCorpse(p,o,2);r.Validate();Equal(.52,r.Points[r.Points.Count-1].X,"exact saved previous anchor");});
        Test("generated routes contain no executable interaction metadata and own their points",delegate{var p=Plan();p.FarmRoute.Points[0].Action="scan";var r=TravelPlanner.FromVendor(p,2);foreach(var point in r.Points)Equal("wait",point.Action,"travel metadata");r.Points[0].X=.9;Equal(.45,p.VendorRoute.Points[2].X,"source points independent");});
        Test("death needs configured distinct confirmations before single release click",delegate{var e=new RecoveryEngine(Plan(),2);var o=Observe(1,true,false,.51,.5);Equal(OperationAction.None,e.Step(o,0).Action,"first dead");Equal(OperationAction.None,e.Step(o,200).Action,"duplicate dead");Next(o);var c=e.Step(o,400);Equal(OperationAction.LeftClick,c.Action,"release click");Equal(OperationPurpose.Release,c.Purpose,"release purpose");Next(o);Equal(OperationAction.None,e.Step(o,600).Action,"release cannot repeat");});
        Test("living observation resets death confirmation and never clicks release",delegate{var e=new RecoveryEngine(Plan(),2);var o=Observe(1,true,false,.51,.5);e.Step(o,0);Next(o);o.Farm.PlayerDead=false;o.Farm.Health=90;Check(e.Step(o,200).Action!=OperationAction.LeftClick,"living release");});
        Test("missing release capability unavailable button and incoming resurrect stop",delegate{foreach(int variant in new[]{0,1,2,3}){var e=new RecoveryEngine(Plan(),2);var o=Observe(1,true,false,.51,.5);e.Step(o,0);Next(o);if(variant==0)o.ReleaseKnown=false;if(variant==1)o.ReleaseVisible=false;if(variant==2)o.ReleaseEnabled=false;if(variant==3)o.IncomingResurrection=true;var c=e.Step(o,200);Check(c.Action!=OperationAction.LeftClick,"unverified release");}});
        Test("released ghost skips release and starts only the recorded corpse route",delegate{var e=new RecoveryEngine(Plan(),2);var o=Observe(1,true,true,.45,.45);for(int i=0;i<3;i++){var c=e.Step(o,i*200);Check(c.Purpose!=OperationPurpose.Release||c.Action!=OperationAction.LeftClick,"ghost release repeated");Next(o);}Check(e.Status!="Stopped","valid ghost cannot begin route");});
        Test("release timeout is enforced even on repeated frames",delegate{var p=Plan();p.Settings.ReleaseTimeoutMs=5000;var e=new RecoveryEngine(p,2);var o=Observe(1,true,false,.51,.5);e.Step(o,0);Next(o);e.Step(o,200);Equal(OperationAction.Stop,e.Step(o,6000).Action,"release evidence timeout");});
        Test("stale unknown core changed map and reversed clock stop recovery",delegate{foreach(int variant in new[]{0,1,2,3}){var e=new RecoveryEngine(Plan(),2);var o=Observe(1,true,false,.51,.5);e.Step(o,100);Next(o);if(variant==0)o.Fresh=false;if(variant==1)o.Farm.KnownCore=false;if(variant==2)o.Farm.MapId=13;Equal(OperationAction.Stop,e.Step(o,variant==3?99:200).Action,"unsafe recovery observation");}});
        Test("manual stop is latched and cannot automatically resume",delegate{var e=new RecoveryEngine(Plan(),2);e.Stop("manual");Equal(OperationAction.Stop,e.Step(Observe(1,true,true,.45,.45),0).Action,"manual stop persistent");Equal("manual",e.LastReason,"stop reason");});
        Test("corpse location knowledge loss stops even on a duplicate sequence",delegate{var e=new RecoveryEngine(Plan(),2);var o=Observe(1,true,true,.45,.45);e.Step(o,0);o.CorpseKnown=false;Equal(OperationAction.Stop,e.Step(o,1).Action,"corpse knowledge on duplicate");});
        Test("ghost state loss stops even on a duplicate sequence",delegate{var e=new RecoveryEngine(Plan(),2);var o=Observe(1,true,true,.45,.45);e.Step(o,0);o.Farm.PlayerGhost=false;Equal(OperationAction.Stop,e.Step(o,1).Action,"ghost loss on duplicate");});
        Test("resurrection cooldown knowledge loss stops even on a duplicate sequence",delegate{var e=new RecoveryEngine(Plan(),2);var o=Observe(1,true,true,.51,.5);e.Step(o,0);o.RecoveryDelayKnown=false;Equal(OperationAction.Stop,e.Step(o,1).Action,"cooldown knowledge on duplicate");});
        Test("release dialog capability loss stops pending release confirmation",delegate{var e=new RecoveryEngine(Plan(),2);var o=Observe(1,true,false,.51,.5);e.Step(o,0);Next(o);e.Step(o,200);o.ReleaseKnown=false;Equal(OperationAction.Stop,e.Step(o,201).Action,"pending release capability");});
        Test("travel cannot chase an old waypoint after moving outside its recorded corridor",delegate{var e=new RecoveryEngine(Plan(),2);var o=Observe(1,true,true,.45,.45);e.Step(o,0);o.Farm.X=.9;Equal(OperationAction.Stop,e.Step(o,1).Action,"unrecorded travel after displacement");});
        Test("full recovery restores low resurrection health and mana then exact farm index",delegate{RunCycle(false,false,false);});
        Test("starting as ghost completes without a release click",delegate{RunCycle(true,false,false);});
        Test("resurrection cooldown waits for known zero and does not click early",delegate{RunCycle(true,true,false);});
        Test("missing supplies cannot fabricate recovery completion",delegate{RunCycle(false,false,true);});
        Console.WriteLine("Recovery workflow tests: "+passed+" passed, "+failed+" failed. Pure observations only; no input backend.");return failed==0?0:1;
    }
    static void RunCycle(bool startsGhost,bool cooldown,bool noSupplies)
    {
        var p=Plan();var original=p.FarmRoute.Fingerprint();var e=new RecoveryEngine(p,2);var o=Observe(1,true,startsGhost,startsGhost?.45:.51,startsGhost?.45:.50);long now=0;int release=0,resurrect=0,food=0,water=0;double facing=0;bool done=false,sawCooldown=false;
        for(int i=0;i<20000;i++)
        {
            o.Farm.Facing=facing;o.ResurrectVisible=o.Farm.PlayerGhost&&NavigationMathForTests.Distance(o.Farm.X-o.CorpseX,o.Farm.Y-o.CorpseY)<.002;
            o.RecoveryDelayMs=cooldown&&o.ResurrectVisible&&now<30000?2000:0;if(o.RecoveryDelayMs>0)sawCooldown=true;
            var c=e.Step(o,now);
            if(c.Action==OperationAction.Stop){Check(noSupplies,"unexpected stop: "+e.LastReason);Check(!e.Completed,"missing supplies completed");return;}
            if(c.Action==OperationAction.ResumeFarm){done=true;break;}
            if(c.Action==OperationAction.LeftClick&&c.Purpose==OperationPurpose.Release){release++;o.Farm.PlayerGhost=true;o.Farm.X=.45;o.Farm.Y=.45;o.ReleaseVisible=false;}
            else if(c.Action==OperationAction.LeftClick&&c.Purpose==OperationPurpose.Resurrect){Check(o.RecoveryDelayMs==0,"clicked during cooldown");resurrect++;o.Farm.PlayerDead=o.Farm.PlayerGhost=false;o.Farm.Health=10;o.Farm.Power=20;o.ResurrectVisible=false;}
            else if(c.Action==OperationAction.Key)
            {
                Check(c.Key!=9&&!(c.Key>=0x31&&c.Key<=0x35),"recovery attacked or selected target");double secs=c.HoldMilliseconds/1000.0;var cal=p.Battle.Calibration;double compass=cal.FacingSign*facing+cal.FacingOffset;
                if(c.Key==p.Battle.Movement.TurnLeft)compass-=cal.TurnRadiansPerSecond*secs;
                else if(c.Key==p.Battle.Movement.TurnRight)compass+=cal.TurnRadiansPerSecond*secs;
                else if(c.Key==p.Battle.Movement.Forward){o.Farm.X+=Math.Sin(compass)*cal.ForwardUnitsPerSecond*secs/cal.AxisScale;o.Farm.Y-=Math.Cos(compass)*cal.ForwardUnitsPerSecond*secs;}
                else if(c.Key==0x38){food++;if(!noSupplies)o.Farm.Health=100;}
                else if(c.Key==0x39){water++;if(!noSupplies)o.Farm.Power=100;}
                facing=(compass-cal.FacingOffset)/cal.FacingSign;facing%=Math.PI*2;if(facing<0)facing+=Math.PI*2;
            }
            now+=c.HoldMilliseconds>0?c.HoldMilliseconds:200;Next(o);
        }
        Check(done&&e.Completed,"cycle did not complete");Equal(startsGhost?0:1,release,"release count");Equal(1,resurrect,"resurrect count");Check(food==1&&water==1,"recover once per resource");Equal(2,e.SavedFarmIndex,"saved index");Check(NavigationMathForTests.Distance(o.Farm.X-.52,o.Farm.Y-.50)<=.001,"did not return original anchor");if(cooldown)Check(sawCooldown,"cooldown scenario never waited");Equal(original,p.FarmRoute.Fingerprint(),"route mutated");
    }
    static class NavigationMathForTests{public static double Distance(double x,double y){return Math.Sqrt(x*x+y*y);}}
}
