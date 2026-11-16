using WoWProbe;
internal static class FishingFixtures
{
    public static FishingPlan Plan(bool vendor=false)
    {
        var p=new FishingPlan{Name="Fishing demo",Calibration=new NavigationCalibration{MapId=42,MapMode=2,Zone="TEST",FacingSign=1},Spot=new Waypoint{X=.5,Y=.5,Tolerance=.001},Facing=0};p.Settings.EnableVendor=vendor;
        if(vendor){p.Vendor=new BattleTarget{Name="Test Merchant",NameHash=TargetCatalog.HashName("Test Merchant"),Kind="npc"};p.VendorRoute=new RouteDocument{RouteId="fishing-vendor",Name="Fishing vendor",RouteType="vendor_return",MapId=42,MapMode=2,Zone="TEST"};p.VendorRoute.Points.Add(p.Spot.Clone());p.VendorRoute.Points.Add(new Waypoint{X=.5,Y=.49,Tolerance=.001});}return p;
    }
    public static FishingObservation Observation(FishingPlan p=null,long now=0)
    {
        p=p??Plan();return new FishingObservation{CapturedAtMs=now,AudioReady=true,Game=new OperationObservation{Fresh=true,Sequence=1,MapId=p.Calibration.MapId,FreeSlotsKnown=true,FreeSlots=8,GeometryKnown=true,ClientWidth=1280,ClientHeight=720,CursorKnown=true,CursorEmpty=true,ConfirmationKnown=true,IncomingResurrectionKnown=true,MerchantKnown=true,InventoryEventsKnown=true,BagEventsKnown=true,Farm=new FarmObservation{Fresh=true,KnownCore=true,Sequence=1,MapId=p.Calibration.MapId,MapMode=p.Calibration.MapMode,Zone=p.Calibration.Zone,X=p.Spot.X,Y=p.Spot.Y,Facing=p.Facing,Health=100,Power=100,PowerType=0,CastingKnown=true,LootKnown=true,LootEventsKnown=true,CastEventsKnown=true}}};
    }
    public static void Frame(FishingObservation o,long now){o.CapturedAtMs=now;o.Game.Sequence++;o.Game.Farm.Sequence=o.Game.Sequence;}
}
internal sealed class FishingVendorCase
{
    public FishingPlan Plan=FishingFixtures.Plan(true);public FishingEngine Engine;public FishingObservation Observation;public long Now;
    public FishingVendorCase()
    {
        Engine=new FishingEngine(Plan);Observation=FishingFixtures.Observation(Plan);Engine.Step(Observation,0);Now=1000;FishingFixtures.Frame(Observation,Now);Observation.Game.FreeSlots=0;Observation.LastBagTextAtMs=Observation.LastBagVoiceAtMs=Now;Observation.BagTextConfidence=Observation.BagVoiceConfidence=.95;Engine.Step(Observation,Now);
        Travel(false);var f=Observation.Game.Farm;f.TargetExists=f.TargetIdentityKnown=f.NameHashKnown=f.AttackabilityKnown=f.TargetDeadKnown=true;f.TargetToken=123;f.TargetNameHash=Plan.Vendor.NameHash;f.TargetName=Plan.Vendor.Name;Tick();if(Engine.Status!="VendorWait")throw new System.Exception("fixture did not reach merchant wait");
    }
    public FishingCommand Tick(int milliseconds=100){Now+=milliseconds;FishingFixtures.Frame(Observation,Now);return Engine.Step(Observation,Now);}
    public void Open(){var g=Observation.Game;g.MerchantOpen=true;g.MerchantKnown=true;g.MerchantTargetToken=123;g.MerchantNameHash=Plan.Vendor.NameHash;Tick();}
    public void Travel(bool reverse)
    {var route=reverse?Plan.VendorRoute.Reversed():Plan.VendorRoute;for(int i=0;i<50&&(Engine.Status=="ReturnNpc"||Engine.Status=="ReturnSpot");i++){var point=route.Points[System.Math.Min(route.Points.Count-1,Engine.RouteIndex)];Observation.Game.Farm.X=point.X;Observation.Game.Farm.Y=point.Y;Tick();}}
}
