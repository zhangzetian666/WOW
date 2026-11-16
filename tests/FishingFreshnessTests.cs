using System;
using System.Reflection;
using WoWProbe;

class FishingFreshnessTests
{
    sealed class Fake:IInputBackend{public int Events;public bool IsTargetForeground(){return true;}public bool IsEmergencyPressed(){return false;}public bool IsKeyAlreadyDown(ushort key){return false;}public void KeyDown(ushort key){Events++;}public void KeyUp(ushort key){Events++;}}
    [STAThread] static int Main()
    {
        int failures=0;bool sourceFresh=true;int sourceReads=0;
        using(var form=new FishingForm(()=>{sourceReads++;throw new Exception("input monitor must not capture game data");},()=>{throw new Exception("native backend must not be requested");},null,null,null,new InputFailureLatch(),()=>sourceFresh))
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var field=typeof(FishingForm).GetField("cached",flags);var read=typeof(FishingForm).GetMethod("ReadCached",flags);
            field.SetValue(form,FishingFixtures.Observation(FishingFixtures.Plan(),100));
            var current=(FishingObservation)read.Invoke(form,null);if(!current.Game.Fresh||!current.Game.Farm.Fresh)failures++;
            sourceFresh=false;var expired=(FishingObservation)read.Invoke(form,null);if(expired.Game.Fresh||expired.Game.Farm.Fresh)failures++;
            if(sourceReads!=0)failures++;
            sourceFresh=true;var detector=new FishingSoundDetector(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),"assets","fishing"));typeof(FishingSoundDetector).GetField("result",flags).SetValue(detector,new FishingSoundSnapshot{LastBiteAtMs=0,BiteConfidence=.99});var capture=new WindowsFishingAudioCapture((samples,rate,end,gap)=>{},()=>0);typeof(WindowsFishingAudioCapture).GetField("running",flags).SetValue(capture,true);typeof(WindowsFishingAudioCapture).GetField("lastPacketMs",flags).SetValue(capture,0L);typeof(FishingForm).GetField("audio",flags).SetValue(form,capture);typeof(FishingForm).GetField("sounds",flags).SetValue(form,detector);typeof(FishingForm).GetField("lastAudibleMs",flags).SetValue(form,0L);var old=FishingFixtures.Observation();old.LastBiteAtMs=0;old.BiteConfidence=.99;field.SetValue(form,old);detector.Reset();var reset=(FishingObservation)read.Invoke(form,null);if(reset.LastBiteAtMs!=-1||reset.BiteConfidence!=0)failures++;
            var p=FishingFixtures.Plan();p.Calibration.Confirmed=true;p.Settings.BindingsConfirmed=p.Settings.AutoLootConfirmed=p.Settings.AudioConfirmed=true;var backend=new Fake();var session=new FishingSession(backend,()=>0,()=>FishingFixtures.Observation(p),null,new InputFailureLatch());session.Start(p);session.Stop("A user-held modifier stopped fishing.");typeof(FishingForm).GetField("session",flags).SetValue(form,session);typeof(FishingForm).GetField("stopHandled",flags).SetValue(form,false);
            var hotkey=System.Windows.Forms.Message.Create(form.Handle,0x0312,new IntPtr(900),IntPtr.Zero);typeof(FishingForm).GetMethod("WndProc",flags).Invoke(form,new object[]{hotkey});if((bool)typeof(FishingForm).GetField("hotkeyStart",flags).GetValue(form)||backend.Events!=0)failures++;
            form.Close();
        }
        Console.WriteLine("Fishing source freshness tests: "+(5-failures)+" passed, "+failures+" failed. Memory-only parent/audio freshness and safe hotkey pause; no audio capture or input.");return failures==0?0:1;
    }
}
