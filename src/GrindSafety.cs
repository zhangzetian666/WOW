using System;
using System.Drawing;

namespace WoWProbe
{
    // CLI auxiliary input shares one cancellation state across calibration, chat and revival.
    // Actual Windows operations are supplied only by GrindCli; tests use no native backend.
    public sealed class GrindInputGuard
    {
        readonly IInputBackend backend;readonly Func<FarmObservation> reader;readonly Func<long> clock;readonly Action<int> wait;readonly InputFailureLatch safety;
        readonly uint mapId;readonly int mapMode;readonly string zone;bool stopped;long lastTime=-1;string reason="";
        public GrindInputGuard(IInputBackend backend,Func<FarmObservation> reader,Func<long> clock,Action<int> wait,InputFailureLatch safety,uint mapId,int mapMode,string zone)
        {if(backend==null||reader==null||clock==null||wait==null||safety==null)throw new ArgumentNullException("grind input dependencies");this.backend=backend;this.reader=reader;this.clock=clock;this.wait=wait;this.safety=safety;this.mapId=mapId;this.mapMode=mapMode;this.zone=zone;}
        void Stop(string message){stopped=true;reason=message;throw new InvalidOperationException(message);}
        public void Ensure()
        {
            if(stopped)throw new InvalidOperationException(reason);
            try{
                if(safety.Faulted)Stop("Input failure is latched: "+safety.Reason);
                if(backend.IsEmergencyPressed())Stop("Emergency stop during CLI operation.");
                if(!backend.IsTargetForeground())Stop("Target window lost foreground during CLI operation.");
                long now=clock();if(now<0||now<lastTime)Stop("CLI monotonic clock moved backwards.");lastTime=now;
                var o=reader();if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0)Stop("CLI observations are unavailable, unknown or stale.");
                if(o.MapId!=mapId||o.MapMode!=mapMode||!String.Equals(o.Zone,zone,StringComparison.Ordinal))Stop("CLI map identity changed.");
                if(backend.IsEmergencyPressed()||!backend.IsTargetForeground())Stop("Input destination or emergency state changed during CLI checks.");
                if(safety.Faulted)Stop("Input failure was latched during CLI checks: "+safety.Reason);
            }catch(Exception ex){stopped=true;reason=ex.Message;throw new InvalidOperationException(reason,ex);}
        }
        public void Wait(int milliseconds)
        {if(milliseconds<0||milliseconds>60000)throw new ArgumentOutOfRangeException("milliseconds");Ensure();for(int left=milliseconds;left>0;){int slice=Math.Min(25,left);wait(slice);left-=slice;Ensure();}}
        public void Pulse(ushort key,int milliseconds)
        {
            if(milliseconds<1||milliseconds>5000)throw new ArgumentOutOfRangeException("milliseconds");Ensure();
            if(backend.IsKeyAlreadyDown(key))Stop("The user is already holding this CLI key.");Ensure();
            bool owned=false;
            try{owned=true;try{backend.KeyDown(key);}catch(Exception ex){safety.Fail("CLI key-down failed: "+ex.Message);Stop("CLI key-down failed: "+ex.Message);}Wait(milliseconds);}
            finally{if(owned)try{backend.KeyUp(key);}catch(Exception ex){safety.Fail("CLI key release failed: "+ex.Message);Stop("CLI key release failed: "+ex.Message);}}
        }
        public void TypeText(string text,Action<char> send)
        {if(text==null||send==null)throw new ArgumentNullException("text input");foreach(char ch in text){Ensure();try{send(ch);}catch(Exception ex){safety.Fail("CLI text input failed: "+ex.Message);Stop("CLI text input failed: "+ex.Message);}Ensure();}}
        public void Click(Point point,bool right,Func<Point,bool> allowed,Action<Point> move,Action<bool,bool> button)
        {
            if(allowed==null||move==null||button==null)throw new ArgumentNullException("mouse input");Ensure();if(!allowed(point))Stop("CLI click lies outside the current client.");
            try{move(point);}catch(Exception ex){safety.Fail("CLI cursor movement failed: "+ex.Message);Stop("CLI cursor movement failed: "+ex.Message);}Ensure();if(!allowed(point))Stop("CLI client bounds changed before click.");Ensure();
            bool owned=false;
            try{owned=true;try{button(right,false);}catch(Exception ex){safety.Fail("CLI mouse down failed: "+ex.Message);Stop("CLI mouse down failed: "+ex.Message);}Wait(70);}
            finally{if(owned)try{button(right,true);}catch(Exception ex){safety.Fail("CLI mouse release failed: "+ex.Message);Stop("CLI mouse release failed: "+ex.Message);}}
        }
    }
    public static class GrindRecoveryPolicy
    {
        public static bool TransientObservation(string message){return message=="CLI observations are unavailable, unknown or stale."||message=="屏幕状态已超时。";}
        public static bool IsHealer(FarmObservation o)
        {return o!=null&&o.Fresh&&o.KnownCore&&o.PlayerGhost&&o.TargetExists&&o.TargetIdentityKnown&&o.TargetToken!=0&&o.NameHashKnown&&o.TargetNameHash==TargetCatalog.HashName("灵魂医者")&&!o.TargetIsPlayer&&o.AttackabilityKnown&&!o.TargetCanAttack;}
        public static bool ShouldRevive(string status,string reason,FarmObservation o)
        {return status=="Stopped"&&reason=="Player death or ghost state stopped farming."&&o!=null&&o.Fresh&&o.KnownCore&&(o.PlayerDead||o.PlayerGhost);}
        public static bool CanApproachHealer(FarmObservation o)
        {return IsHealer(o)&&o.TargetPositionKnown&&!Double.IsNaN(o.TargetX)&&!Double.IsNaN(o.TargetY)&&o.TargetX>=0&&o.TargetX<=1&&o.TargetY>=0&&o.TargetY<=1;}
        public static bool CanAttack(FarmObservation o)
        {return o!=null&&o.Fresh&&o.KnownCore&&!o.PlayerDead&&!o.PlayerGhost&&o.TargetExists&&o.TargetIdentityKnown&&o.TargetToken!=0&&o.AttackabilityKnown&&o.TargetCanAttack&&!o.TargetIsPlayer&&o.TargetDeadKnown&&!o.TargetDead;}
    }
}
