using System;
using System.Collections.Generic;
using WoWProbe;

class RouteSessionTests
{
    static int passed,failed;
    static long now;
    static NavigationSample sample;
    sealed class Fake : IInputBackend
    {
        public bool Focus=true,Emergency,Held,FailDown,FailUp;
        public readonly List<string> Events=new List<string>();
        public bool IsTargetForeground(){return Focus;}
        public bool IsEmergencyPressed(){return Emergency;}
        public bool IsKeyAlreadyDown(ushort key){return Held;}
        public void KeyDown(ushort key){Events.Add("down:"+key);if(FailDown)throw new Exception("partial down");}
        public void KeyUp(ushort key){Events.Add("up:"+key);if(FailUp)throw new Exception("up failure");}
    }
    static void Run(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static void Check(bool b){if(!b)throw new Exception("assertion failed");}
    static void Reject(Action a){try{a();}catch(ArgumentException){return;}catch(InvalidOperationException){return;}throw new Exception("expected refusal");}
    static ProbePacket Packet(uint map,string zone)
    {
        var b=new byte[128];b[0]=87;b[1]=80;b[2]=49;b[3]=1;b[4]=1;b[7]=95;b[8]=80;b[9]=30;b[10]=255;b[12]=51;b[13]=51;b[14]=51;b[15]=51;b[16]=16;b[17]=39;b[18]=10;
        for(int k=0;k<4;k++)b[20+k]=(byte)(map>>(8*k));b[24]=b[25]=b[26]=b[27]=255;byte[] name=System.Text.Encoding.UTF8.GetBytes(zone);b[41]=(byte)name.Length;name.CopyTo(b,74);b[122]=2;b[123]=255;b[125]=255;
        int a=0,c=0;for(int i=0;i<126;i++){a=(a+b[i])%255;c=(c+a)%255;}b[126]=(byte)a;b[127]=(byte)c;return ProbePacket.Decode(b);
    }
    static RouteDocument Route(){return new RouteDocument {RouteId="session",Name="Session",MapId=42,MapMode=2,Zone="Test",Points=new List<Waypoint>{new Waypoint{X=.2,Y=.2},new Waypoint{X=.22,Y=.2}}};}
    static NavigationCalibration Calibration(){return new NavigationCalibration{MapId=42,MapMode=2,Zone="Test",Confirmed=true,FacingSign=1};}
    static RouteSession New(Fake backend)
    {
        now=0;sample=new NavigationSample{X=.2,Y=.2,Facing=Math.PI/2,MapId=42,MapMode=2,Zone="Test",Sequence=1,Fresh=true,KnownCore=true};
        return new RouteSession(backend,delegate{return now;},delegate{return sample;},null);
    }
    static void Pump(RouteSession s)
    {
        for(int i=0;i<12 && !s.IsHolding;i++){now=5000+i*200;sample.Sequence++;s.Tick();}
        Check(s.IsHolding);
    }
    static int Main()
    {
        Run("unconfirmed live calibration cannot arm",delegate{var f=new Fake();var s=New(f);var c=Calibration();c.Confirmed=false;Reject(delegate{s.Start(Route(),c,new RouteKeyBindings(),false,false,0);});Check(f.Events.Count==0);});
        Run("countdown emits no input and permits switching focus",delegate{var f=new Fake{Focus=false};var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);now=4999;s.Tick();Check(f.Events.Count==0&&s.IsActive);f.Focus=true;Pump(s);Check(f.Events.Count==1);s.Stop("done");});
        Run("one pulse always releases at its deadline",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);Pump(s);now+=251;s.Tick();Check(f.Events.Count==2&&!s.IsHolding);s.Stop("done");});
        Run("next pulse waits for observation after release",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);Pump(s);now+=251;s.Tick();for(int i=0;i<10;i++){now+=50;s.Tick();}Check(f.Events.Count==2);s.Stop("done");});
        Run("F12 cancels countdown without key events",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);f.Emergency=true;now=100;s.Tick();Check(!s.IsActive&&f.Events.Count==0);});
        Run("focus loss releases active key and never resumes",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);Pump(s);f.Focus=false;now+=10;s.Tick();Check(!s.IsActive&&f.Events.Count==2);f.Focus=true;now+=5000;s.Tick();Check(f.Events.Count==2);});
        Run("stale snapshot cancels while counting down",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);sample.Fresh=false;now=50;s.Tick();Check(!s.IsActive&&f.Events.Count==0);});
        Run("read error despite fresh data releases active key",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);Pump(s);sample.ReadFailures=8;s.Tick();Check(!s.IsActive&&f.Events.Count==2);});
        Run("map and zone changes release active key",delegate{foreach(bool zone in new[]{true,false}){var f=new Fake();var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);Pump(s);if(zone)sample.Zone="Other";else sample.MapMode=3;s.Tick();Check(!s.IsActive&&f.Events.Count==2);}});
        Run("death ghost combat and full bags stop without recovery input",delegate{for(int i=0;i<4;i++){var f=new Fake();var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);Pump(s);if(i==0)sample.Dead=true;if(i==1)sample.Ghost=true;if(i==2)sample.Combat=true;if(i==3)sample.BagFull=true;s.Tick();Check(!s.IsActive&&f.Events.Count==2);}});
        Run("manually held key is not released by this tool",delegate{var f=new Fake{Held=true};var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);for(int i=0;i<12&&s.IsActive;i++){now=5000+i*200;sample.Sequence++;s.Tick();}Check(!s.IsActive&&f.Events.Count==0);});
        Run("partial key-down failure attempts release and latches fault",delegate{var f=new Fake{FailDown=true};var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);for(int i=0;i<12&&s.IsActive;i++){now=5000+i*200;sample.Sequence++;s.Tick();}Check(s.Status=="Faulted"&&f.Events.Count==2);Reject(delegate{s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);});});
        Run("key-up exception latches fault without repeated input",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);Pump(s);f.FailUp=true;now+=251;s.Tick();s.Tick();Check(s.Status=="Faulted"&&f.Events.Count==2);});
        Run("backward clock releases key",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);Pump(s);now=1;s.Tick();Check(!s.IsActive&&f.Events.Count==2);});
        Run("stop is idempotent and preserves resumable checkpoint",delegate{var f=new Fake();var s=New(f);var r=Route();s.Start(r,Calibration(),new RouteKeyBindings(),false,false,0);Pump(s);s.Stop("manual");s.Stop("again");var c=s.CurrentCheckpoint;Check(f.Events.Count==2&&c.RouteFingerprint==r.Fingerprint()&&c.NextIndex>=0&&!c.Completed);});
        Run("invalid duplicate bindings and completed checkpoint refuse arming",delegate{var f=new Fake();var s=New(f);var k=new RouteKeyBindings{TurnLeft=0x57};Reject(delegate{s.Start(Route(),Calibration(),k,false,false,0);});Reject(delegate{s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,2);});Check(f.Events.Count==0);});
        Run("logging errors cannot prevent matching key release",delegate{var f=new Fake();New(f);var s=new RouteSession(f,delegate{return now;},delegate{return sample;},delegate{throw new Exception("log unavailable");});s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);Pump(s);s.Stop("done");Check(f.Events.Count==2);});
        Run("release failure blocks replacement sessions through shared host latch",delegate{
            var f=new Fake();New(f);var latch=new InputFailureLatch();var first=new RouteSession(f,delegate{return now;},delegate{return sample;},null,latch);
            first.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);Pump(first);f.FailUp=true;now+=251;first.Tick();Check(latch.Faulted);
            var replacement=new RouteSession(f,delegate{return now;},delegate{return sample;},null,latch);
            Reject(delegate{replacement.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);});Check(f.Events.Count==2);
        });
        Run("reentrant log stop cannot send key-down after cancellation",delegate{
            var f=new Fake();New(f);RouteSession s=null;
            s=new RouteSession(f,delegate{return now;},delegate{return sample;},delegate(string text){if(text.StartsWith("Pulse "))s.Stop("log cancelled");});
            s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);
            for(int i=0;i<12&&s.IsActive;i++){now=5000+i*200;sample.Sequence++;s.Tick();}
            Check(!s.IsActive&&f.Events.Count==0);
        });
        Run("north measurement must come from the route's current healthy map",delegate{
            var route=Route();var good=Packet(42,"Test");Check(CalibrationObservation.North(route,new RouteTelemetry{Packet=good,Fresh=true})==good.Facing);
            Reject(delegate{CalibrationObservation.North(route,new RouteTelemetry{Packet=Packet(43,"Test"),Fresh=true});});
            Reject(delegate{CalibrationObservation.North(route,new RouteTelemetry{Packet=Packet(42,"Other"),Fresh=true});});
            Reject(delegate{CalibrationObservation.North(route,new RouteTelemetry{Packet=good,Fresh=false});});
        });
        Run("reentrant clock cancellation at final preflight emits no input",delegate{
            var f=new Fake();New(f);RouteSession s=null;long observed=-1;int calls=0;
            s=new RouteSession(f,delegate{if(observed!=now){observed=now;calls=0;}calls++;if(calls==2)s.Stop("clock cancelled");return now;},delegate{return sample;},null);
            s.Start(Route(),Calibration(),new RouteKeyBindings(),false,false,0);
            for(int i=0;i<12&&s.IsActive;i++){now=5000+i*200;sample.Sequence++;s.Tick();}Check(!s.IsActive&&f.Events.Count==0);
        });
        Console.WriteLine("Route session tests: "+passed+" passed, "+failed+" failed. No native input.");return failed==0?0:1;
    }
}
