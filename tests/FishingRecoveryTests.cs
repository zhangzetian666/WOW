using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using WoWProbe;
class FishingRecoveryTests
{
    static int passed,failed;
    static void Check(bool b){if(!b)throw new Exception("Unexpected fishing recovery behavior");}
    static void Test(string name,Action a){try{a();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static NavigationCalibration Map(){return new NavigationCalibration{MapId=42,MapMode=2,Zone="TEST"};}
    static RouteDocument Route(){return FishingRecoveryRoutes.Create(Map(),new List<Waypoint>{new Waypoint{X=.3,Y=.4},new Waypoint{X=.3,Y=.5},new Waypoint{X=.4,Y=.5}});}
    static OperationObservation O(){var o=FishingFixtures.Observation().Game;o.Farm.X=.3;o.Farm.Y=.4;o.Farm.PlayerDead=true;o.Farm.Health=0;o.ReleaseKnown=o.ReleaseVisible=o.ReleaseEnabled=true;o.ReleaseRegion=new Rectangle(10,20,100,30);o.ResurrectKnown=o.RecoveryDelayKnown=true;o.RecoveryDelayMs=0;o.ResurrectRegion=new Rectangle(200,20,100,30);o.CorpseKnown=true;o.CorpseX=.39;o.CorpseY=.49;return o;}
    static QuickRecoveryCommand Step(QuickRecovery e,OperationObservation o,long now){o.Sequence++;o.Farm.Sequence=o.Sequence;return e.Step(o,now);}
    static int Main()
    {
        Test("ghost recording allows resurrection before finish and keeps its independent draft",()=>{
            var f=O().Farm;f.PlayerDead=false;f.PlayerGhost=true;long now=0;var r=new FishingRouteRecorder(Map(),f,()=>now,true);now=1000;f.Sequence++;f.X+=.002;r.Observe(f,false);f.PlayerGhost=false;f.X+=.002;now=2000;r.Observe(f,true);Check(r.Finish().Count==3&&r.Draft().AllowGhost);string p=Path.GetTempFileName();try{r.Draft().Save(p);var draft=FishingRecordingDraft.Load(p);Check(draft.AllowGhost&&FishingRouteRecorder.Resume(draft,f).Count==3);}finally{File.Delete(p);}
        });
        Test("NPC recording still rejects ghost samples while corpse route preserves corners",()=>{
            var f=O().Farm;f.PlayerDead=false;f.PlayerGhost=true;bool rejected=false;try{new FishingRouteRecorder(Map(),f);}catch(InvalidOperationException){rejected=true;}Check(rejected);var r=Route();Check(r.RouteId=="recorded-ghost"&&r.RouteType=="corpse_run"&&r.Points.Count==3&&r.Points[1].Y==.5);
        });
        Test("death coordinate and reached endpoint persist across restart and reject changed route",()=>{
            var o=O();var route=Route();var cp=FishingDeathCheckpoint.From(route,o);cp.EndReached=true;string p=Path.GetTempFileName();try{cp.Save(p);var restored=FishingDeathCheckpoint.Load(p);Check(restored.EndReached&&restored.X==o.Farm.X&&restored.Death().PlayerDead&&restored.Matches(route,o.Farm));route.Points[1].X+=.01;Check(!restored.Matches(route,o.Farm));}finally{File.Delete(p);}
        });
        Test("cold ghost start uses actual corpse position and never invents a missing body",()=>{
            var o=O();o.Farm.PlayerDead=false;o.Farm.PlayerGhost=true;var cp=FishingDeathCheckpoint.From(Route(),o);Check(cp.X==o.CorpseX&&cp.Y==o.CorpseY);o.CorpseKnown=false;bool rejected=false;try{FishingDeathCheckpoint.From(Route(),o);}catch(InvalidOperationException){rejected=true;}Check(rejected);
        });
        Test("fishing recovery does not resurrect before recorded endpoint even if button appears",()=>{
            var o=O();o.Farm.PlayerDead=false;o.Farm.PlayerGhost=true;o.Farm.X=.39;o.Farm.Y=.49;o.ResurrectVisible=o.ResurrectEnabled=true;var cp=FishingDeathCheckpoint.From(Route(),o);var e=new QuickRecovery(Route(),Map(),cp.Death(),true,true);
            Check(Step(e,o,0).Action!=QuickRecoveryAction.Click&&!e.RecordedEndReached);o.Farm.X=.4;o.Farm.Y=.5;Check(Step(e,o,1000).Action==QuickRecoveryAction.Click&&e.RecordedEndReached);
        });
        Test("release ghost endpoint corpse resurrection and fishing handoff use observed state",()=>{
            var o=O();o.Farm.X=o.CorpseX;o.Farm.Y=o.CorpseY;var cp=FishingDeathCheckpoint.From(Route(),o);var e=new QuickRecovery(Route(),Map(),cp.Death(),true,true);var release=Step(e,o,0);Check(release.Release&&QuickRecovery.Matches(release,o));
            o.Farm.PlayerDead=false;o.Farm.PlayerGhost=true;o.Farm.X=.3;o.Farm.Y=.4;Check(Step(e,o,1000).Action!=QuickRecoveryAction.Stop);o.Farm.X=.4;o.Farm.Y=.5;Step(e,o,2000);Check(e.RecordedEndReached);o.Farm.X=o.CorpseX;o.Farm.Y=o.CorpseY;o.ResurrectVisible=o.ResurrectEnabled=true;var revive=Step(e,o,3000);Check(revive.Action==QuickRecoveryAction.Click&&!revive.Release&&QuickRecovery.Matches(revive,o));
            o.Farm.PlayerGhost=false;o.Farm.Health=50;Check(Step(e,o,4000).Action==QuickRecoveryAction.None);Check(Step(e,o,4200).Action==QuickRecoveryAction.Complete);var s=new FishingClientSettings{Fishing=FishingFixtures.Plan(),RandomSpots=false};s.Points.Add(new FishingPoint{X=o.Farm.X,Y=o.Farm.Y,Facing=o.Farm.Facing});var target=new FishingSpotPicker(s).Start(o.Farm);var plan=s.At(target);plan.Settings.CastDelayMinMs=plan.Settings.CastDelayMaxMs=0;Check(new FishingEngine(plan).Step(FishingFixtures.Observation(plan),0).Purpose==FishingPurpose.Cast);
        });
        Test("restarted final approach retains reached endpoint and configured movement bindings",()=>{
            var o=O();o.Farm.PlayerDead=false;o.Farm.PlayerGhost=true;o.Farm.X=o.CorpseX;o.Farm.Y=o.CorpseY;o.ResurrectVisible=o.ResurrectEnabled=true;var cp=FishingDeathCheckpoint.From(Route(),o);var e=new QuickRecovery(Route(),Map(),cp.Death(),true,true,true);Check(Step(e,o,0).Action==QuickRecoveryAction.Click);var keys=new RouteKeyBindings{Forward=87,Backward=40,TurnLeft=65,TurnRight=68};Check(FishingRecoveryRoutes.Key(38,keys)==87&&FishingRecoveryRoutes.Key(83,keys)==40&&FishingRecoveryRoutes.Key(37,keys)==65&&FishingRecoveryRoutes.Key(39,keys)==68);
        });
        Test("append supports five points without overwriting and cycles all interior segments",()=>{
            var s=new FishingClientSettings{Fishing=FishingFixtures.Plan()};foreach(var p in new[]{new PointF(.3f,.3f),new PointF(.4f,.3f),new PointF(.4f,.4f),new PointF(.35f,.45f),new PointF(.3f,.4f)})FishingPointList.Append(s,new FarmObservation{X=p.X,Y=p.Y,Facing=0});Check(s.Points.Count==5&&Math.Abs(s.Points[0].X-.3)<.000001&&FishingPointList.Describe(s.Points).Contains("5：X="));var picker=new FishingSpotPicker(s,n=>n/2);for(int i=0;i<5;i++){var p=picker.Next();var a=s.Points[i];var b=s.Points[(i+1)%5];Check(picker.Segment==i&&picker.PositionOnSegment>0&&picker.PositionOnSegment<1&&Math.Abs(p.X-(a.X+b.X)/2)<.000001);}Check(picker.CompletedLaps==0);picker.Next();Check(picker.CompletedLaps==1&&picker.Segment==0);
        });
        Test("five point settings roundtrip preserves independent NPC and corpse routes",()=>{
            var s=new FishingClientSettings{Fishing=FishingFixtures.Plan(),UseVisual=false,AutoCorpse=true,CorpseRoute=Route()};for(int i=0;i<5;i++)s.Points.Add(new FishingPoint{X=.3+i*.01,Y=.3});s.Validate(true);string p=Path.GetTempFileName();try{s.Save(p);var copy=FishingClientSettings.Load(p);Check(copy.Points.Count==5&&copy.CorpseRoute.Points.Count==3&&copy.Service.Vendor.Points.Count==0);copy.CorpseRoute.Points[0].X=.7;Check(s.CorpseRoute.Points[0].X==.3);}finally{File.Delete(p);}
        });
        Test("actual cast and reel timing messages select red and blue without coloring ordinary waits",()=>{
            var p=FishingFixtures.Plan();p.Settings.CastDelayMinMs=p.Settings.CastDelayMaxMs=300;p.Settings.ReelDelayMinMs=p.Settings.ReelDelayMaxMs=500;var o=FishingFixtures.Observation(p);var e=new FishingEngine(p);Check(FishingLogColors.For(e.Step(o,0).Reason)==Color.Red);FishingFixtures.Frame(o,300);e.Step(o,300);FishingFixtures.Frame(o,1500);o.Game.Farm.Channeling=true;o.LastBiteAtMs=1500;o.BiteConfidence=.95;Check(FishingLogColors.For(e.Step(o,1500).Reason)==Color.Blue);Check(FishingLogColors.For("等待本杆鱼讯")==SystemColors.WindowText);
        });
        Console.WriteLine("Fishing recovery tests: "+passed+" passed, "+failed+" failed. Synthetic observations; no game input.");return failed==0?0:1;
    }
}
