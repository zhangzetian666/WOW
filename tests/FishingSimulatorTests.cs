using System;
using System.Diagnostics;
using WoWProbe;
class FishingSimulatorTests
{
    static int passed,failed;
    static void Test(string n,Action a){try{a();passed++;Console.WriteLine("PASS "+n);}catch(Exception e){failed++;Console.WriteLine("FAIL "+n+": "+e.Message);}}
    static void Assert(bool value,string why){if(!value)throw new Exception(why);}
    static FishingCommand Run(FishingSimulator s,int maximum=5000){for(int i=0;i<maximum;i++){var c=s.Step();if(c.Action==FishingAction.Stop||s.Completed||c.Action==FishingAction.Completed)return c;}throw new Exception("simulator step bound: "+s.Engine.Status+" "+s.Engine.LastReason);}
    static int Main()
    {
        Test("virtual fishing catches twice vendors returns restores facing and recasts",delegate{var s=new FishingSimulator();Run(s);Assert(s.Completed,s.Engine.LastReason);Assert(s.Engine.ConfirmedCatches==2&&s.Engine.Reels==2&&s.Engine.VendorVisits==1&&s.Engine.Casts==3,"missing complete fishing cycle");Assert(s.InputEvents==0&&s.ElapsedMilliseconds>0,"native input or no virtual time");var o=s.Observation.Game.Farm;Assert(NavigationMath.Distance(o.X-s.Plan.Spot.X,o.Y-s.Plan.Spot.Y,s.Plan.Calibration.AxisScale)<=s.Plan.Spot.Tolerance,"did not return to spot");Assert(Math.Abs(NavigationMath.Normalize(o.Facing-s.Plan.Facing))<=.12,"saved facing lost");});
        Test("virtual no-bite waits thirty seconds then recasts without F",delegate{var s=new FishingSimulator(){NoBite=true};Run(s);Assert(s.Completed&&s.Engine.Casts==2&&s.Engine.Reels==0&&s.ElapsedMilliseconds>=30000,"no-bite timeout failed");});
        Test("external selling failure stops instead of pretending slots recovered",delegate{var s=new FishingSimulator(){NoSale=true};var c=Run(s);Assert(c.Action==FishingAction.Stop&&!s.Completed&&s.Engine.VendorVisits==0&&s.InputEvents==0,"failed selling resumed fishing");});
        Test("wrong NPC is never interacted with or counted as a vendor",delegate{var s=new FishingSimulator(){WrongNpc=true};var c=Run(s);Assert(c.Action==FishingAction.Stop&&!s.Completed&&s.Engine.VendorVisits==0&&!s.Observation.Game.MerchantOpen,"wrong NPC opened");});
        Test("default stationary real settings stop when full rather than navigate",delegate{var s=new FishingSimulator(FishingFixtures.Plan());var c=Run(s);Assert(c.Action==FishingAction.Stop&&!s.Completed&&s.Engine.VendorVisits==0,"stationary plan moved to vendor");});
        Test("simulation observations and supplied plans are detached",delegate{var p=FishingFixtures.Plan(true);var s=new FishingSimulator(p);p.Spot.X=0;s.Observation.Game.Farm.X=0;s.Plan.Spot.X=0;Assert(s.Observation.Game.Farm.X==.5&&s.Plan.Spot.X==.5,"mutable simulator state escaped");});
        Test("two virtual hours repeatedly fish and vendor with zero native input",delegate{var s=new FishingSimulator(){CompleteAfterDemo=false,RepeatBagFull=true};var wall=Stopwatch.StartNew();int steps=0;while(s.ElapsedMilliseconds<7200000&&steps<150000){var c=s.Step();Assert(c.Action!=FishingAction.Stop&&!s.Completed,s.Engine.LastReason);steps++;}wall.Stop();Assert(s.ElapsedMilliseconds>=7200000&&s.Engine.VendorVisits>5&&s.Engine.ConfirmedCatches>20&&s.InputEvents==0,"virtual stress coverage missing");Assert(s.Engine.Reels-s.Engine.ConfirmedCatches>=0&&s.Engine.Reels-s.Engine.ConfirmedCatches<=1,"repeated reel or fabricated catches");Console.WriteLine("FISHING_VIRTUAL_STRESS elapsed_ms="+s.ElapsedMilliseconds+" wall_ms="+wall.ElapsedMilliseconds+" steps="+steps+" catches="+s.Engine.ConfirmedCatches+" vendors="+s.Engine.VendorVisits+" input=0");});
        Console.WriteLine("Fishing simulator tests: "+passed+" passed, "+failed+" failed. Virtual time and no native input.");return failed==0?0:1;
    }
}
