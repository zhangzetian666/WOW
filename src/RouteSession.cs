using System;

namespace WoWProbe
{
    public sealed class InputFailureLatch
    {
        readonly object gate=new object();bool faulted;string reason="";
        public bool Faulted{get{lock(gate)return faulted;}}
        public string Reason{get{lock(gate)return reason;}}
        public void Fail(string message){lock(gate){if(!faulted)reason=message;faulted=true;}}
    }
    public sealed class RouteKeyBindings
    {
        public ushort Forward=0x57,Backward=0x53,TurnLeft=0x51,TurnRight=0x45,StrafeLeft=0x41,StrafeRight=0x44,Jump=0x20;
        public void Validate()
        {
            ushort[] keys={Forward,Backward,TurnLeft,TurnRight,StrafeLeft,StrafeRight,Jump};
            for(int i=0;i<keys.Length;i++)
            {
                if(keys[i]!=0x57&&keys[i]!=0x53&&keys[i]!=0x51&&keys[i]!=0x45&&keys[i]!=0x41&&keys[i]!=0x44&&keys[i]!=0x20)
                    throw new ArgumentException("Only W,S,A,D,Q,E and Space are movement keys in this release.");
                for(int j=0;j<i;j++)if(keys[i]==keys[j])throw new ArgumentException("Movement key bindings must be distinct.");
            }
        }
        public RouteKeyBindings Clone(){return (RouteKeyBindings)MemberwiseClone();}
        public ushort For(NavigationAction action)
        {
            switch(action){case NavigationAction.Forward:return Forward;case NavigationAction.TurnLeft:return TurnLeft;case NavigationAction.TurnRight:return TurnRight;case NavigationAction.StrafeLeft:return StrafeLeft;case NavigationAction.StrafeRight:return StrafeRight;case NavigationAction.Jump:return Jump;default:throw new ArgumentException("Not a movement pulse.");}
        }
    }

    /// <summary>Coordinates a bounded live movement session. Native input is injected, never implicit.</summary>
    public sealed class RouteSession
    {
        readonly object gate=new object();
        readonly IInputBackend backend;
        readonly Func<long> clock;
        readonly Func<NavigationSample> reader;
        readonly Action<string> log;
        readonly InputFailureLatch inputSafety;
        Navigator navigator;
        RouteDocument original;
        RouteKeyBindings bindings;
        string fingerprint,status="Idle",reason="";
        bool running,holding,ownsKey,faulted,armed,awaitObservation,reverse,loop;
        ushort ownedKey;
        long due,releaseAt,lastTime;
        int releaseSequence;
        NavigationCommand lastCommand;
        public RouteSession(IInputBackend backend,Func<long> clock,Func<NavigationSample> reader,Action<string> log,InputFailureLatch inputSafety=null)
        {if(backend==null||clock==null||reader==null)throw new ArgumentNullException("session dependencies");this.backend=backend;this.clock=clock;this.reader=reader;this.log=log;this.inputSafety=inputSafety??new InputFailureLatch();}
        public string Status{get{lock(gate)return status;}}
        public string LastReason{get{lock(gate)return reason;}}
        public bool IsActive{get{lock(gate)return running;}}
        public bool IsHolding{get{lock(gate)return holding;}}
        public int TargetIndex{get{lock(gate)return navigator==null?0:navigator.TargetIndex;}}
        public NavigationCommand LastCommand{get{lock(gate)return lastCommand==null?null:new NavigationCommand{Action=lastCommand.Action,HoldMilliseconds=lastCommand.HoldMilliseconds,TargetIndex=lastCommand.TargetIndex,Reason=lastCommand.Reason};}}
        public RouteCheckpoint CurrentCheckpoint
        {
            get{lock(gate){if(original==null)return null;return new RouteCheckpoint{RouteFingerprint=fingerprint,NextIndex=navigator.TargetIndex,Reverse=reverse,Loop=loop,Completed=navigator.Completed,MapId=original.MapId,MapMode=original.MapMode,Zone=original.Zone};}}
        }

        public void Start(RouteDocument route,NavigationCalibration calibration,RouteKeyBindings keys,bool reverse,bool loop,int startIndex)
        {
            lock(gate)
            {
                if(faulted||inputSafety.Faulted)throw new InvalidOperationException("输入发送或释放曾失败。请手动松开按键，检查游戏状态并重启工具后再试。");
                if(running||ownsKey)throw new InvalidOperationException("A route session is already active.");
                if(route==null||calibration==null||keys==null)throw new ArgumentNullException("route settings");
                route.Validate();calibration.Validate();keys.Validate();
                if(!calibration.Confirmed)throw new InvalidOperationException("Actual-game calibration has not been confirmed.");
                if(startIndex<0||startIndex>=route.Points.Count)throw new ArgumentOutOfRangeException("startIndex","A completed route cannot automatically resume.");
                var source=route.Clone();var initial=reader();string rejected=SampleProblem(initial,source);
                if(rejected!=null)throw new InvalidOperationException(rejected);
                var engine=new Navigator(source,calibration.Clone(),reverse,loop,startIndex);
                long time=clock();if(time<0)throw new InvalidOperationException("Invalid monotonic clock.");
                due=checked(time+5000);lastTime=time;original=source;navigator=engine;fingerprint=route.Fingerprint();bindings=keys.Clone();
                this.reverse=reverse;this.loop=loop;running=true;armed=false;holding=ownsKey=awaitObservation=false;lastCommand=null;status="Countdown";reason="Five-second countdown; focus the game window.";
                WriteLog("Route armed: "+source.RouteId+" reverse="+reverse+" index="+startIndex);
            }
        }

        public void Tick()
        {
            lock(gate)
            {
                if(!running||faulted)return;
                try
                {
                    if(inputSafety.Faulted){Fault(new InvalidOperationException(inputSafety.Reason));return;}
                    long time=clock();if(time<lastTime){StopCore("Monotonic clock moved backwards.");return;}lastTime=time;
                    if(backend.IsEmergencyPressed()){StopCore("Emergency stop.");return;}
                    NavigationSample sample=reader();string problem=SampleProblem(sample,original);
                    if(problem!=null){StopCore(problem);return;}
                    if(!armed&&time<due)return;
                    if(!backend.IsTargetForeground()){StopCore("Target window is not foreground.");return;}
                    armed=true;
                    if(holding)
                    {
                        if(time>=releaseAt)
                        {
                            Release();if(faulted)return;
                            awaitObservation=true;releaseSequence=sample.Sequence;status="WaitingForFrame";
                        }
                        return;
                    }
                    if(awaitObservation&&sample.Sequence==releaseSequence)return;
                    awaitObservation=false;
                    NavigationCommand command=navigator.Step(sample,time);lastCommand=command;
                    if(command.Action==NavigationAction.Stop){StopCore(command.Reason);return;}
                    if(command.Action==NavigationAction.Completed){running=false;status="Completed";reason=command.Reason;WriteLog("Route completed.");return;}
                    if(command.Action==NavigationAction.None){status="WaitingForFrame";reason=command.Reason;return;}
                    if(command.HoldMilliseconds<60||command.HoldMilliseconds>250)throw new InvalidOperationException("Navigator requested an unbounded key pulse.");
                    ushort key=bindings.For(command.Action);
                    if(backend.IsKeyAlreadyDown(key)){StopCore("Key is already held by the user.");return;}
                    // Recheck the exact input destination immediately before the key-down call.
                    if(!backend.IsTargetForeground()||backend.IsEmergencyPressed()){StopCore("Input destination changed before pulse.");return;}
                    WriteLog("Pulse "+command.Action+" "+command.HoldMilliseconds+"ms at index "+command.TargetIndex);
                    if(!running||faulted||inputSafety.Faulted)return;
                    string latestProblem=SampleProblem(reader(),original);
                    if(latestProblem!=null){StopCore(latestProblem);return;}
                    if(!backend.IsTargetForeground()||backend.IsEmergencyPressed()){StopCore("Input destination changed before pulse.");return;}
                    if(!running||faulted||inputSafety.Faulted)return;
                    long pressTime=clock();if(!running||faulted||inputSafety.Faulted)return;if(pressTime<lastTime){StopCore("Monotonic clock moved backwards.");return;}lastTime=pressTime;
                    ownedKey=key;ownsKey=holding=true;releaseAt=checked(pressTime+command.HoldMilliseconds);status="Holding";reason=command.Reason;
                    backend.KeyDown(key);
                }
                catch(Exception ex){Fault(ex);}
            }
        }

        public void Stop(string why)
        {lock(gate){if(faulted)return;StopCore(String.IsNullOrEmpty(why)?"Stopped by user.":why);}}
        void StopCore(string why)
        {
            running=false;status="Stopped";reason=why;
            if(navigator!=null&&!navigator.Completed)navigator.Stop(why);
            Release();WriteLog("Route stopped: "+why);
        }
        void Release()
        {
            holding=false;
            if(!ownsKey)return;
            ownsKey=false;
            try{backend.KeyUp(ownedKey);WriteLog("Released owned key "+ownedKey);}
            catch(Exception ex){faulted=true;running=false;status="Faulted";reason="Key release failed: "+ex.Message;inputSafety.Fail(reason);WriteLog(reason);}
        }
        void Fault(Exception ex)
        {faulted=true;running=false;reason="Input stopped after exception: "+ex.Message;inputSafety.Fail(reason);Release();status="Faulted";WriteLog(reason);}
        void WriteLog(string text){if(log!=null)try{log(text);}catch{/* Logging must never delay or prevent a matching key release. */}}
        static string SampleProblem(NavigationSample s,RouteDocument route)
        {
            if(s==null||!s.Fresh)return "Navigation data is stale or invalid.";
            if(!s.KnownCore||s.ReadFailures!=0)return "Navigation state is unknown or an API read failed.";
            if(s.Dead||s.Ghost)return "Player is dead or in ghost form.";
            if(s.Combat)return "Combat interrupted route navigation.";
            if(s.BagFull)return "Full bags interrupted route navigation.";
            if(s.MapId!=route.MapId||s.MapMode!=route.MapMode||!String.Equals(s.Zone,route.Zone,StringComparison.Ordinal))return "Map or zone changed.";
            if(!Finite(s.X)||!Finite(s.Y)||s.X<0||s.X>1||s.Y<0||s.Y>1||!Finite(s.Facing)||s.Facing<0||s.Facing>2*Math.PI)return "Position or facing is unavailable.";
            return null;
        }
        static bool Finite(double x){return !Double.IsNaN(x)&&!Double.IsInfinity(x);}
    }

    public sealed class RouteTelemetry
    {
        public ProbePacket Packet;
        public bool Fresh;
        public NavigationSample Navigation(){return NavigationSample.FromPacket(Packet,Fresh);}
    }
    public static class CalibrationObservation
    {
        public static double North(RouteDocument route,RouteTelemetry telemetry)
        {
            if(route==null||telemetry==null)throw new InvalidOperationException("请先载入路线并建立状态读取。");
            var state=telemetry.Navigation();
            if(!state.Fresh||!state.KnownCore||state.ReadFailures!=0||state.Dead||state.Ghost||state.Combat)throw new InvalidOperationException("请先建立有效状态读取，并在脱战存活时手动面向正北。");
            if(state.MapId!=route.MapId||state.MapMode!=route.MapMode||!String.Equals(state.Zone,route.Zone,StringComparison.Ordinal))throw new InvalidOperationException("当前游戏地图与这条路线不一致，不能记录该路线的北向标定。");
            return state.Facing;
        }
    }
}
