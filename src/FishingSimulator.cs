using System;
namespace WoWProbe
{
    public sealed class FishingSimulator
    {
        readonly FishingPlan plan;readonly FishingObservation observation;long castIssued=-1,lootDue=-1,saleDue=-1;bool biteDelivered,bagInjected;int sequence,nextBagAfter=2,lastVisit;FishingCommand last;
        public FishingPlan Plan{get{return plan.Clone();}}public FishingEngine Engine{get;private set;}public FishingObservation Observation{get{return observation.Clone();}}public long ElapsedMilliseconds{get;private set;}public int InputEvents{get{return 0;}}public bool Completed{get;private set;}
        public bool InjectBagFull=true,CompleteAfterDemo=true,RepeatBagFull,NoBite,NoSale,WrongNpc,NoLoot,AudioUnavailable,Stuck;
        public FishingSimulator(FishingPlan plan=null)
        {
            this.plan=(plan??CreateDemoPlan()).Clone();this.plan.Validate();Engine=new FishingEngine(this.plan);var p=this.plan;
            observation=new FishingObservation{AudioReady=true,Game=new OperationObservation{Fresh=true,MapId=p.Calibration.MapId,FreeSlotsKnown=true,FreeSlots=8,GeometryKnown=true,ClientWidth=1280,ClientHeight=720,CursorKnown=true,CursorEmpty=true,ConfirmationKnown=true,IncomingResurrectionKnown=true,MerchantKnown=true,InventoryEventsKnown=true,BagEventsKnown=true,Farm=new FarmObservation{Fresh=true,KnownCore=true,MapId=p.Calibration.MapId,MapMode=p.Calibration.MapMode,Zone=p.Calibration.Zone,X=p.Spot.X,Y=p.Spot.Y,Facing=p.Facing,Health=100,Power=100,PowerType=0,CastingKnown=true,LootKnown=true,LootEventsKnown=true,CastEventsKnown=true}}};
        }
        public static FishingPlan CreateDemoPlan()
        {
            var p=new FishingPlan{Name="Offline fishing and vendor demonstration",Spot=new Waypoint{X=.5,Y=.5,Tolerance=.001},Facing=0,Calibration=new NavigationCalibration{MapId=900001,MapMode=2,Zone="SIMULATION",FacingSign=1},Vendor=new BattleTarget{Name="Demo fishing merchant",NameHash=TargetCatalog.HashName("Demo fishing merchant"),Kind="npc"},VendorRoute=new RouteDocument{RouteId="fishing-vendor-demo",Name="Recorded fishing vendor demonstration",RouteType="vendor_return",MapId=900001,MapMode=2,Zone="SIMULATION"}};
            p.Settings.EnableVendor=true;p.VendorRoute.Points.Add(p.Spot.Clone());p.VendorRoute.Points.Add(new Waypoint{X=.5,Y=.49,Tolerance=.001});p.VendorRoute.Points.Add(new Waypoint{X=.505,Y=.49,Tolerance=.001});p.Validate();return p;
        }
        public FishingCommand Step()
        {
            if(Completed)return new FishingCommand{Action=FishingAction.Completed,Reason="Offline demonstration completed; no game or native input was used."};
            if(last!=null&&last.Action==FishingAction.Stop)return new FishingCommand{Action=FishingAction.Stop,Reason=Engine.LastReason};
            Update();var c=Engine.Step(observation,ElapsedMilliseconds);last=c;
            if(c.Action==FishingAction.Key)Apply(c);
            bool noBiteDone=NoBite&&Engine.Casts>=2;
            bool catchesDone=!InjectBagFull&&Engine.ConfirmedCatches>=2&&Engine.Casts>=3;
            bool vendorDone=bagInjected&&Engine.VendorVisits>=1&&Engine.Casts>=3;
            if(CompleteAfterDemo&&c.Action!=FishingAction.Stop&&(noBiteDone||catchesDone||vendorDone)){Completed=true;Engine.Stop("Offline fishing demonstration completed, including its observed outcomes; game validation is pending.");}
            ElapsedMilliseconds+=Math.Max(100,c.HoldMilliseconds);return c;
        }
        void Update()
        {
            var g=observation.Game;var f=g.Farm;sequence=(sequence+1)&65535;g.Sequence=f.Sequence=sequence;observation.CapturedAtMs=ElapsedMilliseconds;observation.AudioReady=!AudioUnavailable;
            // The virtual client ends its own old fishing channel before the engine
            // may request another cast; the planner does not cancel it by pressing 1.
            if(castIssued>=0&&ElapsedMilliseconds-castIssued>=plan.Settings.CastTimeoutMs)f.Channeling=f.Casting=false;
            if(RepeatBagFull&&Engine.VendorVisits>lastVisit){lastVisit=Engine.VendorVisits;bagInjected=false;nextBagAfter=Engine.ConfirmedCatches+2;}
            if(!NoBite&&castIssued>=0&&!biteDelivered&&ElapsedMilliseconds-castIssued>=1500&&Engine.Status=="WaitBite")
            {observation.LastBiteAtMs=ElapsedMilliseconds;observation.BiteConfidence=.98;biteDelivered=true;}
            if(lootDue>=0&&ElapsedMilliseconds>=lootDue)
            {lootDue=-1;f.Channeling=f.Casting=false;if(!NoLoot){f.LootOpened++;f.LootClosed++;g.InventoryRevision++;g.FreeSlots=Math.Max(0,g.FreeSlots-1);}}
            if(InjectBagFull&&!bagInjected&&Engine.ConfirmedCatches>=nextBagAfter)
            {bagInjected=true;g.FreeSlots=0;f.BagFull=true;g.InventoryRevision++;observation.LastBagTextAtMs=observation.LastBagVoiceAtMs=ElapsedMilliseconds;observation.BagTextConfidence=observation.BagVoiceConfidence=.98;}
            if(saleDue>=0&&ElapsedMilliseconds>=saleDue&&!NoSale)
            {saleDue=-1;g.FreeSlots=8;f.BagFull=false;g.InventoryRevision++;}
        }
        void Apply(FishingCommand c)
        {
            if(c.HoldMilliseconds<(c.Purpose==FishingPurpose.RestoreFacing?20:60)||c.HoldMilliseconds>250)throw new InvalidOperationException("Unbounded virtual fishing pulse.");var g=observation.Game;var f=g.Farm;var s=plan.Settings;
            switch(c.Purpose)
            {
                case FishingPurpose.Cast:
                    if(f.Casting||f.Channeling)throw new InvalidOperationException("Virtual fishing cannot cast over an active old channel.");
                    if(c.Key!=s.CastKey||NavigationMath.Distance(f.X-plan.Spot.X,f.Y-plan.Spot.Y,plan.Calibration.AxisScale)>plan.Spot.Tolerance)throw new InvalidOperationException("Virtual cast outside the saved spot.");
                    castIssued=ElapsedMilliseconds;biteDelivered=false;f.Channeling=true;break;
                case FishingPurpose.Reel:
                    if(c.Key!=s.InteractKey||c.ExpectedBiteAtMs!=observation.LastBiteAtMs||!observation.AudioReady)throw new InvalidOperationException("Unproven virtual reel.");lootDue=ElapsedMilliseconds+200;f.Channeling=false;break;
                case FishingPurpose.SelectNpc:
                    f.TargetExists=f.TargetIdentityKnown=f.NameHashKnown=f.AttackabilityKnown=f.TargetDeadKnown=true;f.TargetCanAttack=f.TargetIsPlayer=f.TargetDead=false;f.TargetToken=WrongNpc?999u:123u;f.TargetName=WrongNpc?"Other merchant":plan.Vendor.Name;f.TargetNameHash=TargetCatalog.HashName(f.TargetName);break;
                case FishingPurpose.OpenMerchant:
                    VerifyMerchant(c);g.MerchantKnown=g.MerchantOpen=true;g.MerchantNameHash=f.TargetNameHash;g.MerchantTargetToken=f.TargetToken;g.MerchantOpened++;saleDue=ElapsedMilliseconds+700;break;
                case FishingPurpose.CloseMerchant:
                    VerifyMerchant(c);if(!g.MerchantOpen)throw new InvalidOperationException("Virtual merchant was not open.");g.MerchantOpen=false;g.MerchantNameHash=g.MerchantTargetToken=0;g.MerchantClosed++;break;
                case FishingPurpose.Travel:
                case FishingPurpose.RestoreFacing:Move(c);break;
                default:throw new InvalidOperationException("Unsupported virtual fishing input purpose.");
            }
        }
        void VerifyMerchant(FishingCommand c)
        {var f=observation.Game.Farm;if(plan.Vendor==null||!f.TargetExists||!f.TargetIdentityKnown||f.TargetToken!=c.ExpectedTargetToken||f.TargetNameHash!=c.ExpectedNameHash||f.TargetNameHash!=plan.Vendor.NameHash||f.TargetIsPlayer||f.TargetCanAttack)throw new InvalidOperationException("Virtual command lacks saved merchant identity.");}
        void Move(FishingCommand c)
        {
            var f=observation.Game.Farm;var m=plan.Movement;var k=plan.Calibration;double seconds=c.HoldMilliseconds/1000.0,world=NavigationMath.Normalize(k.FacingSign*f.Facing+k.FacingOffset);
            if(c.Key==m.TurnLeft||c.Key==m.TurnRight){world+=(c.Key==m.TurnRight?1:-1)*k.TurnRadiansPerSecond*seconds;f.Facing=NavigationMath.PositiveAngle(k.FacingSign*(NavigationMath.Normalize(world)-k.FacingOffset));return;}
            if(c.Key==m.Jump)return;if(c.Key!=m.Forward&&c.Key!=m.Backward&&c.Key!=m.StrafeLeft&&c.Key!=m.StrafeRight)throw new InvalidOperationException("Not a virtual movement key.");if(Stuck)return;
            if(c.Key==m.Backward)world+=Math.PI;if(c.Key==m.StrafeLeft)world-=Math.PI/2;if(c.Key==m.StrafeRight)world+=Math.PI/2;
            double distance=k.ForwardUnitsPerSecond*seconds;f.X=Math.Max(0,Math.Min(1,f.X+Math.Sin(world)*distance/k.AxisScale));f.Y=Math.Max(0,Math.Min(1,f.Y-Math.Cos(world)*distance));
        }
    }
}
