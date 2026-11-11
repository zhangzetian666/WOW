using System.Collections.Generic;

namespace WoWProbe
{
    public static class CampaignDemo
    {
        public static CampaignPlan Create()
        {
            var f=new RouteDocument{RouteId="complete-farm",Name="完整流程·离线农场",MapId=900001,MapMode=2,Zone="Simulation only",Points=new List<Waypoint>{new Waypoint{X=.5,Y=.5},new Waypoint{X=.52,Y=.5},new Waypoint{X=.52,Y=.52},new Waypoint{X=.5,Y=.52},new Waypoint{X=.5,Y=.5}}};
            var c=f.Clone();c.RouteId="complete-corpse";c.Name="完整流程·墓地到农场";c.RouteType="corpse_run";c.Points=new List<Waypoint>{new Waypoint{X=.47,Y=.5},new Waypoint{X=.49,Y=.5},new Waypoint{X=.5,Y=.5}};
            var v=f.Clone();v.RouteId="complete-vendor";v.Name="完整流程·农场到商人";v.RouteType="vendor_return";v.Points=new List<Waypoint>{new Waypoint{X=.5,Y=.5},new Waypoint{X=.48,Y=.5},new Waypoint{X=.46,Y=.5}};
            var p=new CampaignPlan{FarmRoute=f,CorpseRoute=c,VendorRoute=v,Settings=new CampaignSettings{ProtectedItemIds=new List<uint>{1004}},Battle=new BattleProfile{Name="完整流程·离线战斗",Skinning=true,AllowApproach=true,Calibration=new NavigationCalibration{MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone},Targets=new List<BattleTarget>{new BattleTarget{Name="演示野兽",NameHash=TargetCatalog.HashName("演示野兽")},new BattleTarget{Name="演示商人",NameHash=TargetCatalog.HashName("演示商人"),Kind="npc"}}}};
            p.Validate();return p;
        }
    }
}
