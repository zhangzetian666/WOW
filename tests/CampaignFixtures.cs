using System.Collections.Generic;
using System.Drawing;
using WoWProbe;
static class CampaignFixtures
{
    public static CampaignPlan Plan()
    {
        var f=new RouteDocument{RouteId="farm-demo",Name="Farm",MapId=900001,MapMode=2,Zone="Simulation only",Points=new List<Waypoint>{new Waypoint{X=.5,Y=.5},new Waypoint{X=.52,Y=.5},new Waypoint{X=.52,Y=.52},new Waypoint{X=.5,Y=.52},new Waypoint{X=.5,Y=.5}}};
        var c=f.Clone();c.RouteId="corpse-demo";c.RouteType="corpse_run";c.Points=new List<Waypoint>{new Waypoint{X=.47,Y=.5},new Waypoint{X=.49,Y=.5},new Waypoint{X=.5,Y=.5}};
        var v=f.Clone();v.RouteId="vendor-demo";v.RouteType="vendor_return";v.Points=new List<Waypoint>{new Waypoint{X=.5,Y=.5},new Waypoint{X=.48,Y=.5},new Waypoint{X=.46,Y=.5}};
        return new CampaignPlan{FarmRoute=f,CorpseRoute=c,VendorRoute=v,Settings=new CampaignSettings(),Battle=new BattleProfile{Skinning=true,AllowApproach=true,Calibration=new NavigationCalibration{MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone},Targets=new List<BattleTarget>{new BattleTarget{Name="演示野兽",NameHash=TargetCatalog.HashName("演示野兽")},new BattleTarget{Name="演示商人",NameHash=TargetCatalog.HashName("演示商人"),Kind="npc"}}}};
    }
    public static OperationObservation Sample()
    {
        return new OperationObservation{Sequence=1,Fresh=true,MapId=900001,FreeSlotsKnown=true,FreeSlots=4,GeometryKnown=true,ClientWidth=1920,ClientHeight=1080,LayoutToken=123,
            ReleaseKnown=true,ReleaseRegion=new Rectangle(700,450,150,40),ResurrectKnown=true,ResurrectRegion=new Rectangle(700,500,150,40),RecoveryDelayKnown=true,RecoveryDelayMs=0,IncomingResurrectionKnown=true,
            MerchantKnown=true,CursorKnown=true,CursorEmpty=true,ConfirmationKnown=true,SellTabKnown=true,SellTab=true,InventoryEventsKnown=true,InventoryCycleKnown=true,BagEventsKnown=true,
            Farm=new FarmObservation{Sequence=1,Fresh=true,KnownCore=true,Health=90,Power=90,PowerType=0,MapId=900001,MapMode=2,Zone="Simulation only",X=.5,Y=.5,Facing=0,CastingKnown=true,ActionsKnown=true,KnownActionsMask=15,ReadyActionsMask=15,UsableActionsMask=15}};
    }
    public static void Next(OperationObservation o){o.Sequence=(o.Sequence+1)&65535;o.Farm.Sequence=o.Sequence;}
}
