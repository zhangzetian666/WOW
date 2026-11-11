using System;
using System.Collections.Generic;
using System.Drawing;

namespace WoWProbe
{
    /// <summary>Connects observed positions only to recorded, map-bound polylines.</summary>
    public static class TravelPlanner
    {
        sealed class Projection{public int Segment;public double X,Y,Distance;}
        static CampaignPlan Checked(CampaignPlan plan,int saved)
        {if(plan==null)throw new ArgumentNullException("plan");var p=plan.Clone();p.Validate();if(saved<0||saved>=p.FarmRoute.Points.Count)throw new ArgumentOutOfRangeException("savedFarmIndex");return p;}
        static RouteDocument Farm(CampaignPlan p){return p.FarmReverse?p.FarmRoute.Reversed():p.FarmRoute.Clone();}
        static void CheckObservation(CampaignPlan p,OperationObservation o)
        {
            if(o==null||!o.Fresh||o.Farm==null||!o.Farm.Fresh||!o.Farm.KnownCore||o.Farm.ReadFailures!=0)throw new InvalidOperationException("Travel requires fresh known observations.");
            if(o.MapId!=p.FarmRoute.MapId||o.Farm.MapId!=p.FarmRoute.MapId||o.Farm.MapMode!=p.FarmRoute.MapMode||!String.Equals(o.Farm.Zone,p.FarmRoute.Zone,StringComparison.Ordinal))throw new InvalidOperationException("Travel map identity changed.");
            if(!NavigationMath.InRange(o.Farm.X,0,1)||!NavigationMath.InRange(o.Farm.Y,0,1))throw new InvalidOperationException("Travel position is unknown.");
        }
        static Projection Project(RouteDocument route,double x,double y,double scale)
        {
            Projection best=null;
            for(int i=0;i<route.Points.Count-1;i++)
            {
                Waypoint a=route.Points[i],b=route.Points[i+1];double dx=(b.X-a.X)*scale,dy=b.Y-a.Y,px=(x-a.X)*scale,py=y-a.Y,length=dx*dx+dy*dy;
                double t=length==0?0:Math.Max(0,Math.Min(1,(px*dx+py*dy)/length));double qx=a.X+(b.X-a.X)*t,qy=a.Y+(b.Y-a.Y)*t;
                double distance=NavigationMath.Distance(x-qx,y-qy,scale);if(best==null||distance<best.Distance)best=new Projection{Segment=i,X=qx,Y=qy,Distance=distance};
            }
            return best;
        }
        internal static double DistanceFromRecordedRoute(RouteDocument route,double x,double y,double scale)
        {return Project(route,x,y,scale).Distance;}
        static void Add(List<Waypoint> points,double x,double y)
        {
            if(points.Count>0){var previous=points[points.Count-1];if(Math.Abs(previous.X-x)<1e-12&&Math.Abs(previous.Y-y)<1e-12)return;}
            if(points.Count>=5000)throw new InvalidOperationException("Combined travel exceeds the 5000-point route limit.");
            points.Add(new Waypoint{X=x,Y=y,Tolerance=.0005,Action="wait"});
        }
        static void Add(List<Waypoint> points,Waypoint p){Add(points,p.X,p.Y);}
        static RouteDocument Build(CampaignPlan p,List<Waypoint> points,string id,string type)
        {
            if(points.Count<2)return null;
            var r=new RouteDocument{RouteId=id,Name=id,RouteType=type,MapId=p.FarmRoute.MapId,MapMode=p.FarmRoute.MapMode,Zone=p.FarmRoute.Zone,Points=points,AllowReverse=true};r.Validate();return r;
        }
        static void FarmToAnchor(List<Waypoint> points,RouteDocument farm,Projection from,int anchor)
        {
            Add(points,from.X,from.Y);
            if(anchor<=from.Segment){for(int i=from.Segment;i>=anchor;i--)Add(points,farm.Points[i]);}
            else{for(int i=from.Segment+1;i<=anchor;i++)Add(points,farm.Points[i]);}
        }
        public static RouteDocument ToVendor(CampaignPlan plan,OperationObservation current,int savedFarmIndex)
        {
            var p=Checked(plan,savedFarmIndex);if(!p.Settings.EnableVendor||p.VendorRoute==null)throw new InvalidOperationException("Vendor travel is disabled.");CheckObservation(p,current);
            if(current.Farm.PlayerDead||current.Farm.PlayerGhost||current.Farm.InCombat)throw new InvalidOperationException("Vendor travel requires a living out-of-combat player.");
            var farm=Farm(p);double scale=p.Battle.Calibration.AxisScale;var destination=p.VendorRoute.Points[p.VendorRoute.Points.Count-1];
            if(NavigationMath.Distance(current.Farm.X-destination.X,current.Farm.Y-destination.Y,scale)<=p.Settings.JoinTolerance)return null;
            var projection=Project(farm,current.Farm.X,current.Farm.Y,scale);if(projection.Distance>p.Settings.JoinTolerance)throw new InvalidOperationException("Current position cannot safely join the recorded farm route.");
            var points=new List<Waypoint>();Add(points,current.Farm.X,current.Farm.Y);FarmToAnchor(points,farm,projection,0);foreach(var v in p.VendorRoute.Points)Add(points,v);
            return Build(p,points,"travel-to-vendor","vendor_return");
        }
        public static RouteDocument FromVendor(CampaignPlan plan,int savedFarmIndex)
        {
            var p=Checked(plan,savedFarmIndex);if(!p.Settings.EnableVendor||p.VendorRoute==null)throw new InvalidOperationException("Vendor travel is disabled.");var farm=Farm(p);int anchor=Math.Max(0,savedFarmIndex-1);var end=p.VendorRoute.Points[p.VendorRoute.Points.Count-1];var target=farm.Points[anchor];
            if(NavigationMath.Distance(end.X-target.X,end.Y-target.Y,p.Battle.Calibration.AxisScale)<=p.Settings.JoinTolerance)return null;
            var points=new List<Waypoint>();for(int i=p.VendorRoute.Points.Count-1;i>=0;i--)Add(points,p.VendorRoute.Points[i]);for(int i=0;i<=anchor;i++)Add(points,farm.Points[i]);return Build(p,points,"travel-from-vendor","vendor_return");
        }
        public static RouteDocument ToCorpse(CampaignPlan plan,OperationObservation current,int savedFarmIndex)
        {
            var p=Checked(plan,savedFarmIndex);if(!p.Settings.EnableRecovery||p.CorpseRoute==null)throw new InvalidOperationException("Corpse travel is disabled.");CheckObservation(p,current);
            if(!current.Farm.PlayerGhost||current.Farm.InCombat)throw new InvalidOperationException("Corpse travel requires actual ghost state without combat.");
            if(!current.CorpseKnown||!NavigationMath.InRange(current.CorpseX,0,1)||!NavigationMath.InRange(current.CorpseY,0,1))throw new InvalidOperationException("Actual corpse coordinates are required; a death snapshot cannot replace them.");
            var farm=Farm(p);double scale=p.Battle.Calibration.AxisScale;var corpse=Project(farm,current.CorpseX,current.CorpseY,scale);
            if(corpse.Distance>p.Settings.CorpseTolerance)throw new InvalidOperationException("The corpse is outside the recorded farm recovery corridor.");
            if(NavigationMath.Distance(current.Farm.X-current.CorpseX,current.Farm.Y-current.CorpseY,scale)<=p.Settings.CorpseTolerance)return null;
            var grave=p.CorpseRoute.Points[0];if(NavigationMath.Distance(current.Farm.X-grave.X,current.Farm.Y-grave.Y,scale)>p.Settings.JoinTolerance)throw new InvalidOperationException("Ghost position cannot join the recorded graveyard route.");
            var points=new List<Waypoint>();Add(points,current.Farm.X,current.Farm.Y);foreach(var point in p.CorpseRoute.Points)Add(points,point);for(int i=0;i<=corpse.Segment;i++)Add(points,farm.Points[i]);Add(points,corpse.X,corpse.Y);Add(points,current.CorpseX,current.CorpseY);
            return Build(p,points,"travel-to-corpse","corpse_run");
        }
        public static RouteDocument FromCorpse(CampaignPlan plan,OperationObservation current,int savedFarmIndex)
        {
            var p=Checked(plan,savedFarmIndex);CheckObservation(p,current);if(current.Farm.PlayerDead||current.Farm.PlayerGhost||current.Farm.InCombat)throw new InvalidOperationException("Farm return requires confirmed life without combat.");
            var farm=Farm(p);double scale=p.Battle.Calibration.AxisScale;int anchor=Math.Max(0,savedFarmIndex-1);var projection=Project(farm,current.Farm.X,current.Farm.Y,scale);
            if(projection.Distance>p.Settings.JoinTolerance)throw new InvalidOperationException("Resurrection position cannot safely join the recorded farm route.");
            var target=farm.Points[anchor];if(NavigationMath.Distance(current.Farm.X-target.X,current.Farm.Y-target.Y,scale)<=p.Settings.JoinTolerance)return null;
            var points=new List<Waypoint>();Add(points,current.Farm.X,current.Farm.Y);FarmToAnchor(points,farm,projection,anchor);return Build(p,points,"travel-from-corpse","vendor_return");
        }
    }

    /// <summary>Pure death recovery; all clicks and keys are guarded commands for an external owner.</summary>
    public sealed class RecoveryEngine
    {
        enum Phase{ConfirmDeath,Release,CorpseRun,Resurrect,ConfirmAlive,Rest,ReturnFarm,Completed,Stopped}
        readonly CampaignPlan plan;readonly int saved;Phase phase=Phase.ConfirmDeath;Navigator navigator;RouteDocument travelRoute;
        bool hasTime,hasSequence,healIssued,manaIssued;int sequence,deathFrames,aliveFrames;long lastNow,stateStarted,corpseStarted,readySince=-1;double corpseX,corpseY;
        public string Status{get{return phase.ToString();}}public string LastReason{get;private set;}public bool Completed{get;private set;}public int SavedFarmIndex{get{return saved;}}
        public RecoveryEngine(CampaignPlan plan,int savedFarmIndex)
        {
            if(plan==null)throw new ArgumentNullException("plan");this.plan=plan.Clone();this.plan.Validate();if(!this.plan.Settings.EnableRecovery||this.plan.CorpseRoute==null)throw new InvalidOperationException("Recovery is disabled.");if(savedFarmIndex<0||savedFarmIndex>=this.plan.FarmRoute.Points.Count)throw new ArgumentOutOfRangeException("savedFarmIndex");saved=savedFarmIndex;LastReason="Confirming death from distinct fresh observations.";
        }
        public void Stop(string reason){if(phase==Phase.Stopped)return;phase=Phase.Stopped;LastReason=String.IsNullOrWhiteSpace(reason)?"Recovery stopped by request.":reason;if(navigator!=null)navigator.Stop(LastReason);}
        public OperationCommand Step(OperationObservation observation,long nowMs)
        {
            if(phase==Phase.Stopped)return Command(OperationAction.Stop,OperationPurpose.Travel,LastReason);
            if(Completed)return Command(OperationAction.ResumeFarm,OperationPurpose.Travel,LastReason);
            if(nowMs<0||(hasTime&&nowMs<lastNow))return Fail("Recovery clock is invalid or moved backwards.");
            if(!hasTime){hasTime=true;stateStarted=nowMs;}lastNow=nowMs;
            string invalid=Unsafe(observation);if(invalid!=null)return Fail(invalid);
            if((phase==Phase.ConfirmDeath||phase==Phase.Release)&&nowMs-stateStarted>=plan.Settings.ReleaseTimeoutMs)return Fail("Release confirmation deadline elapsed.");
            if((phase==Phase.CorpseRun||phase==Phase.Resurrect)&&nowMs-corpseStarted>=plan.Settings.CorpseTimeoutMs)return Fail("Corpse recovery deadline elapsed.");
            if(phase==Phase.Resurrect&&readySince>=0&&nowMs-readySince>=plan.Settings.ResurrectTimeoutMs)return Fail("A ready resurrection dialog did not become usable before its deadline.");
            if(phase==Phase.ConfirmAlive&&nowMs-stateStarted>=plan.Settings.ResurrectTimeoutMs)return Fail("Resurrection did not produce confirmed living observations.");
            if(phase==Phase.Rest&&nowMs-stateStarted>=plan.Battle.RestTimeoutMs)return Fail("Recovery supplies or observed health and mana restoration are missing.");
            if(phase==Phase.ReturnFarm&&nowMs-stateStarted>=plan.Settings.CorpseTimeoutMs)return Fail("Recorded return to the saved farm anchor timed out.");
            var o=observation;bool distinct=!hasSequence||o.Sequence!=sequence;if(!distinct)return None("Waiting for another distinct recovery observation.");hasSequence=true;sequence=o.Sequence;
            try
            {
                switch(phase)
                {
                    case Phase.ConfirmDeath:
                        if(o.Farm.PlayerGhost)return BeginCorpse(o,nowMs);
                        if(!o.Farm.PlayerDead)return Fail("Player is alive; release is not applicable.");
                        if(o.Farm.Health!=0){deathFrames=0;return None("Death also requires known zero health.");}
                        deathFrames++;if(deathFrames<plan.Settings.DeathConfirmFrames)return None("Confirming death from another distinct zero-health frame.");
                        if(!o.ReleaseKnown)return Fail("Release dialog state is unknown.");
                        if(!o.ReleaseVisible||!o.ReleaseEnabled)return None("Waiting for a visible enabled release button.");
                        invalid=ButtonProblem(o,o.ReleaseRegion);if(invalid!=null)return Fail(invalid);
                        Enter(Phase.Release,nowMs);return Click(OperationPurpose.Release,o.ReleaseRegion,"Release the confirmed dead player once; actual ghost evidence must follow.");
                    case Phase.Release:
                        if(o.Farm.PlayerGhost)return BeginCorpse(o,nowMs);
                        if(!o.Farm.PlayerDead)return Fail("Life state changed unexpectedly before confirmed release.");
                        return None("Release was clicked once; waiting for actual ghost state.");
                    case Phase.CorpseRun:
                        if(!o.Farm.PlayerGhost)return Fail("Actual ghost state was lost during corpse travel.");
                        invalid=CorpseProblem(o);if(invalid!=null)return Fail(invalid);
                        return Travel(o,nowMs,true);
                    case Phase.Resurrect:
                        if(!o.Farm.PlayerGhost)return Fail("Actual ghost state was lost before the resurrection click.");
                        invalid=CorpseProblem(o);if(invalid!=null)return Fail(invalid);
                        if(NavigationMath.Distance(o.Farm.X-o.CorpseX,o.Farm.Y-o.CorpseY,plan.Battle.Calibration.AxisScale)>plan.Settings.CorpseTolerance)return Fail("Recorded route did not reach the actual corpse tolerance.");
                        if(!o.RecoveryDelayKnown||o.RecoveryDelayMs<0)return Fail("Corpse recovery delay is unknown.");
                        if(o.RecoveryDelayMs!=0)return None("Waiting for known corpse recovery cooldown to reach zero.");
                        if(readySince<0)readySince=nowMs;
                        if(!o.ResurrectKnown)return Fail("Resurrection dialog state is unknown.");
                        if(!o.ResurrectVisible||!o.ResurrectEnabled)return None("Waiting for the enabled corpse resurrection button.");
                        invalid=ButtonProblem(o,o.ResurrectRegion);if(invalid!=null)return Fail(invalid);
                        aliveFrames=0;Enter(Phase.ConfirmAlive,nowMs);return Click(OperationPurpose.Resurrect,o.ResurrectRegion,"Resurrect once after known zero cooldown; await two living frames.");
                    case Phase.ConfirmAlive:
                        if(o.Farm.PlayerDead||o.Farm.PlayerGhost){if(aliveFrames>0)return Fail("The player died or became a ghost again during resurrection confirmation.");return None("Waiting for actual life after the single resurrection click.");}
                        if(o.Farm.Health<=0||o.Farm.Health>100)return Fail("Positive known health is required to confirm resurrection.");
                        if(!o.ResurrectKnown)return Fail("Resurrection dialog state became unknown.");
                        if(o.ResurrectVisible){aliveFrames=0;return None("Waiting for the resurrection dialog to close.");}
                        aliveFrames++;if(aliveFrames<2)return None("Confirming life with another distinct frame.");
                        healIssued=manaIssued=false;Enter(Phase.Rest,nowMs);return None("Life confirmed; recover safely before returning to the saved farm anchor.");
                    case Phase.Rest:return Rest(o,nowMs);
                    case Phase.ReturnFarm:
                        if(o.Farm.PlayerDead||o.Farm.PlayerGhost)return Fail("Death interrupted the recorded return to farm.");
                        return Travel(o,nowMs,false);
                    default:return Fail("Unknown recovery state.");
                }
            }
            catch(Exception ex){return Fail("Recovery planning stopped: "+ex.Message);}
        }
        OperationCommand BeginCorpse(OperationObservation o,long now)
        {
            var route=TravelPlanner.ToCorpse(plan,o,saved);corpseX=o.CorpseX;corpseY=o.CorpseY;corpseStarted=now;readySince=-1;
            if(route==null){Enter(Phase.Resurrect,now);return None("Already within the known corpse tolerance; checking cooldown and dialog.");}
            travelRoute=route.Clone();navigator=new Navigator(route,plan.Battle.Calibration,false,false,0,NavigationPurpose.CorpseRun);Enter(Phase.CorpseRun,now);return None("Following the recorded corpse route with actual ghost state.");
        }
        OperationCommand Travel(OperationObservation o,long now,bool corpse)
        {
            var f=o.Farm;var c=navigator.Step(new NavigationSample{X=f.X,Y=f.Y,Facing=f.Facing,MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone,Sequence=o.Sequence,Fresh=o.Fresh&&f.Fresh,KnownCore=f.KnownCore,ReadFailures=f.ReadFailures,Dead=f.PlayerDead,Ghost=f.PlayerGhost,Combat=f.InCombat,BagFull=f.BagFull},now);
            if(c.Action==NavigationAction.Stop)return Fail(c.Reason);
            if(c.Action==NavigationAction.Completed){if(corpse){Enter(Phase.Resurrect,now);return None("Recorded corpse route complete; verify actual range, cooldown and resurrection dialog.");}return Finish();}
            if(c.Action==NavigationAction.None)return None(c.Reason);
            return new OperationCommand{Action=OperationAction.Key,Purpose=OperationPurpose.Travel,Key=plan.Battle.Movement.For(c.Action),HoldMilliseconds=c.HoldMilliseconds,Reason=c.Reason};
        }
        OperationCommand Rest(OperationObservation o,long now)
        {
            var f=o.Farm;if(f.PlayerDead||f.PlayerGhost)return Fail("Player died again during recovery rest.");
            if(f.Health<=0||f.Health>100||f.PowerType<0||f.PowerType>=255||(f.PowerType==0&&(f.Power<0||f.Power>100)))return Fail("Recovery health or applicable mana is unknown.");
            if(!f.CastingKnown)return Fail("Casting state is unknown during recovery rest.");
            if(f.Casting||f.Channeling)return None("Waiting for casting to finish before recovery supplies.");
            bool healthReady=f.Health>=plan.Battle.RecoverTo,manaReady=f.PowerType!=0||f.Power>=plan.Battle.RecoverTo;
            if(healthReady&&manaReady)
            {
                var route=TravelPlanner.FromCorpse(plan,o,saved);if(route==null)return Finish();travelRoute=route.Clone();navigator=new Navigator(route,plan.Battle.Calibration,false,false,0,NavigationPurpose.VendorReturn);Enter(Phase.ReturnFarm,now);return None("Recovery observed; follow recorded farm points back to the original anchor.");
            }
            if(!healthReady&&!healIssued){healIssued=true;return Key(0x38,"Use food once after resurrection; positive low health is allowed only in recovery rest.");}
            if(!manaReady&&!manaIssued){manaIssued=true;return Key(0x39,"Use water once and require observed mana recovery.");}
            return None("Waiting for observed health and applicable mana recovery without repeating supplies.");
        }
        internal string SafetyProblem(OperationObservation o){return Unsafe(o);}
        string Unsafe(OperationObservation o)
        {
            if(o==null||!o.Fresh||o.Farm==null||!o.Farm.Fresh||!o.Farm.KnownCore||o.Farm.ReadFailures!=0)return "Fresh known operation and core observations are required.";
            var f=o.Farm;if(o.MapId!=plan.FarmRoute.MapId||f.MapId!=plan.FarmRoute.MapId||f.MapMode!=plan.FarmRoute.MapMode||!String.Equals(f.Zone,plan.FarmRoute.Zone,StringComparison.Ordinal))return "Recovery map or zone changed.";
            if(!NavigationMath.InRange(f.X,0,1)||!NavigationMath.InRange(f.Y,0,1)||!NavigationMath.InRange(f.Facing,0,Math.PI*2))return "Recovery position or facing is unknown.";
            if(f.InCombat)return "Combat interrupted automatic recovery.";
            if(!o.IncomingResurrectionKnown)return "Incoming resurrection invitation state is unknown.";
            if(o.IncomingResurrection)return "Another player's resurrection invitation requires manual handling.";
            if(o.ConfirmationKnown&&o.ConfirmationVisible)return "A confirmation dialog requires manual handling.";
            if(phase==Phase.Release&&!o.ReleaseKnown)return "Release dialog state became unknown before ghost confirmation.";
            if(phase==Phase.CorpseRun||phase==Phase.Resurrect)
            {
                if(!f.PlayerGhost)return "Actual ghost state was lost before recovery completed.";
                string corpse=CorpseProblem(o);if(corpse!=null)return corpse;
            }
            if(phase==Phase.Resurrect&&(!o.RecoveryDelayKnown||o.RecoveryDelayMs<0))return "Corpse recovery delay became unknown.";
            if((phase==Phase.Rest||phase==Phase.ReturnFarm)&&(f.PlayerDead||f.PlayerGhost))return "Player died or became a ghost again after resurrection.";
            if((phase==Phase.CorpseRun||phase==Phase.ReturnFarm)&&TravelPlanner.DistanceFromRecordedRoute(travelRoute,f.X,f.Y,plan.Battle.Calibration.AxisScale)>plan.Settings.JoinTolerance)return "Player moved outside the recorded recovery travel corridor.";
            return null;
        }
        string CorpseProblem(OperationObservation o)
        {
            if(!o.CorpseKnown||!NavigationMath.InRange(o.CorpseX,0,1)||!NavigationMath.InRange(o.CorpseY,0,1))return "Actual corpse coordinates became unknown.";
            if(NavigationMath.Distance(o.CorpseX-corpseX,o.CorpseY-corpseY,plan.Battle.Calibration.AxisScale)>plan.Settings.CorpseTolerance)return "Corpse location changed beyond the recorded recovery tolerance.";
            return null;
        }
        string ButtonProblem(OperationObservation o,Rectangle r)
        {
            if(!o.GeometryKnown||o.LayoutToken==0||o.ClientWidth!=plan.Settings.ClientWidth||o.ClientHeight!=plan.Settings.ClientHeight)return "Client layout is unknown or differs from the confirmed configuration.";
            if(r.X<0||r.Y<0||r.Width<=0||r.Height<=0||(long)r.X+r.Width>o.ClientWidth||(long)r.Y+r.Height>o.ClientHeight)return "Dialog button rectangle is outside the confirmed client area.";
            return null;
        }
        void Enter(Phase next,long now){phase=next;stateStarted=now;}
        OperationCommand Finish(){Completed=true;phase=Phase.Completed;LastReason="Recovery complete; resume the exact saved farm index.";return Command(OperationAction.ResumeFarm,OperationPurpose.Travel,LastReason);}
        OperationCommand Fail(string why){Stop(why);return Command(OperationAction.Stop,OperationPurpose.Travel,LastReason);}
        OperationCommand None(string why){LastReason=why;return Command(OperationAction.None,OperationPurpose.Travel,why);}
        static OperationCommand Command(OperationAction action,OperationPurpose purpose,string why){return new OperationCommand{Action=action,Purpose=purpose,Reason=why};}
        static OperationCommand Click(OperationPurpose purpose,Rectangle r,string why){return new OperationCommand{Action=OperationAction.LeftClick,Purpose=purpose,Region=r,HoldMilliseconds=120,Reason=why};}
        static OperationCommand Key(ushort key,string why){return new OperationCommand{Action=OperationAction.Key,Purpose=OperationPurpose.RecoveryRest,Key=key,HoldMilliseconds=120,Reason=why};}
    }
}
