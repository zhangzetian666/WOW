using System;
using System.Drawing;
using System.IO;
using WoWProbe;
class FishingExtrasTests
{
    static int passed,failed;
    static void Check(bool yes){if(!yes)throw new Exception("Unexpected fishing behavior");}
    static void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static int Main()
    {
        Test("random cast wait is drawn once per cast including timeout recasts",()=>{
            var p=FishingFixtures.Plan();p.Settings.CastDelayMinMs=300;p.Settings.CastDelayMaxMs=1000;int calls=0;var e=new FishingEngine(p,n=>{calls++;return n-1;});var o=FishingFixtures.Observation(p);
            Check(e.Step(o,0).Action==FishingAction.None);FishingFixtures.Frame(o,999);Check(e.Step(o,999).Action==FishingAction.None&&calls==1);FishingFixtures.Frame(o,1000);Check(e.Step(o,1000).Purpose==FishingPurpose.Cast);
            FishingFixtures.Frame(o,31000);Check(e.Step(o,31000).Action==FishingAction.None&&calls==2);FishingFixtures.Frame(o,32000);Check(e.Step(o,32000).Purpose==FishingPurpose.Cast&&e.Casts==2);
        });
        Test("muted visual bite waits requested delay then reels only once",()=>{
            var p=FishingFixtures.Plan();p.Settings.ReelDelayMinMs=p.Settings.ReelDelayMaxMs=1000;var e=new FishingEngine(p);var o=FishingFixtures.Observation(p);o.AudioReady=false;o.VisualReady=true;
            Check(e.Step(o,0).Purpose==FishingPurpose.Cast);FishingFixtures.Frame(o,2000);o.LastBiteAtMs=2000;o.BiteConfidence=.9;Check(e.Step(o,2000).Action==FishingAction.None);
            FishingFixtures.Frame(o,2999);Check(e.Step(o,2999).Action==FishingAction.None);FishingFixtures.Frame(o,3000);Check(e.Step(o,3000).Purpose==FishingPurpose.Reel);
            FishingFixtures.Frame(o,3100);Check(e.Step(o,3100).Purpose!=FishingPurpose.Reel&&e.Reels==1);
        });
        Test("near-deadline reel delay is shortened rather than sent after cast expiry",()=>{
            var p=FishingFixtures.Plan();p.Settings.ReelDelayMinMs=p.Settings.ReelDelayMaxMs=1000;var e=new FishingEngine(p);var o=FishingFixtures.Observation(p);e.Step(o,0);
            FishingFixtures.Frame(o,29500);o.LastBiteAtMs=29500;o.BiteConfidence=.9;Check(e.Step(o,29500).Action==FishingAction.None);
            FishingFixtures.Frame(o,29820);Check(e.Step(o,29820).Purpose==FishingPurpose.Reel);
        });
        Test("lost visual readiness cannot authorize delayed F",()=>{
            var p=FishingFixtures.Plan();p.Settings.ReelDelayMinMs=p.Settings.ReelDelayMaxMs=500;var e=new FishingEngine(p);var o=FishingFixtures.Observation(p);e.Step(o,0);
            FishingFixtures.Frame(o,2000);o.LastBiteAtMs=2000;o.BiteConfidence=.9;e.Step(o,2000);o.AudioReady=false;FishingFixtures.Frame(o,2500);Check(e.Step(o,2500).Action==FishingAction.Stop&&e.Reels==0);
        });
        Test("three shoreline segments form one lap and interpolate wrapped facing",()=>{
            var s=new FishingClientSettings();s.Points.Add(new FishingPoint{X=.1,Y=.1,Facing=6.2});s.Points.Add(new FishingPoint{X=.2,Y=.1,Facing=.1});s.Points.Add(new FishingPoint{X=.2,Y=.2,Facing=.2});
            var pick=new FishingSpotPicker(s,n=>n/2);var a=pick.Next();Check(a.X>.1&&a.X<.2&&a.Y==.1&&(a.Facing<.2||a.Facing>6));
            var b=pick.Next();Check(b.X==.2&&b.Y>.1&&b.Y<.2);var c=pick.Next();Check(Math.Abs(c.X-c.Y)<.00001&&pick.CompletedLaps==0);pick.Next();Check(pick.CompletedLaps==1&&pick.Segment==0);
        });
        Test("bag and online timers resample only after confirmed transitions",()=>{
            var s=new FishingClientSettings{PeekBags=true,TimedSessions=true};int call=0;var t=new FishingActivityClock(s,0,n=>call++%2==0?0:n-1);
            Check(t.OnlineDue==3600000&&t.NextBag==900000);Check(!t.LogoutDue(3599999)&&t.LogoutDue(3600000));t.LogoutRequested();Check(t.OfflineDue==0);t.OfflineConfirmed(4000000);Check(t.OfflineDue==7600000);t.OfflineConfirmed(5000000);Check(t.OfflineDue==7600000);t.Online(8000000);Check(t.OnlineDue==15200000);t.BagClosed(9000000);Check(t.NextBag==9600000);
        });
        Test("whispers ignore old events and reply separately per sender only after send",()=>{
            var w=new FishingWhisperPolicy();Check(w.Pending(10,1)==null);Check(w.Pending(11,1)=="这会有个电话，稍等回你");Check(w.Pending(11,1)=="这会有个电话，稍等回你");w.Sent(11,1);Check(w.Pending(11,1)==null&&w.Pending(12,1)=="稍等");Check(w.Pending(13,2)=="这会有个电话，稍等回你");
        });
        Test("visual detector rejects still water and whole-scene changes then accepts localized splash",()=>{
            var d=new FishingSplashDetector(3.5);var r=new FishingRegion{Width=1,Height=1};using(var img=new Bitmap(96,96)){
                using(var g=Graphics.FromImage(img))g.Clear(Color.DarkBlue);for(int i=0;i<8;i++)d.Push(img,r,i*200,0,true);Check(d.LastBiteAtMs<0);
                using(var g=Graphics.FromImage(img))g.FillRectangle(Brushes.White,40,40,12,12);d.Push(img,r,1600,0,true);Check(d.LastBiteAtMs<0);
                using(var g=Graphics.FromImage(img)){g.Clear(Color.DarkBlue);g.FillRectangle(Brushes.White,44,40,12,12);}d.Push(img,r,1800,0,true);Check(d.LastBiteAtMs==1800);
                d.Reset();using(var g=Graphics.FromImage(img))g.Clear(Color.DarkBlue);for(int i=0;i<8;i++)d.Push(img,r,i*200,0,true);using(var g=Graphics.FromImage(img))g.Clear(Color.White);d.Push(img,r,1600,0,true);d.Push(img,r,1800,0,true);Check(d.LastBiteAtMs<0);
            }
        });
        Console.WriteLine("Fishing extras: "+passed+" passed, "+failed+" failed. Synthetic observations only; no game input.");return failed==0?0:1;
    }
}
