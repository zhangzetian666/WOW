using System;

namespace WoWProbe
{
    // The decision engine never sends input. This session owns only bounded keyboard pulses.
    public sealed class FishingSession
    {
        readonly object gate=new object();readonly IInputBackend backend;readonly Func<long> clock;readonly Func<FishingObservation> reader;readonly Action<string> log;readonly InputFailureLatch safety;
        FishingPlan plan;FishingEngine engine;FishingCommand command;bool running,armed,holding,ownsKey,waiting,ticking,starting,faulted;ushort key;int releasedSequence;long due,releaseAt,lastTime,generation,castDeadline=-1;string status="Idle",reason="";
        public FishingSession(IInputBackend backend,Func<long> clock,Func<FishingObservation> reader,Action<string> log,InputFailureLatch safety)
        {if(backend==null||clock==null||reader==null||safety==null)throw new ArgumentNullException("fishing session dependencies");this.backend=backend;this.clock=clock;this.reader=reader;this.log=log;this.safety=safety;}
        public bool IsActive{get{lock(gate)return running;}}public bool IsHolding{get{lock(gate)return holding;}}public string Status{get{lock(gate)return status;}}public string LastReason{get{lock(gate)return reason;}}public string EngineStatus{get{lock(gate)return engine==null?"Idle":engine.Status;}}
        public FishingCheckpoint CurrentCheckpoint{get{lock(gate)return engine==null?null:engine.CurrentCheckpoint;}}
        public int Casts{get{lock(gate)return engine==null?0:engine.Casts;}}public int Reels{get{lock(gate)return engine==null?0:engine.Reels;}}public int ConfirmedCatches{get{lock(gate)return engine==null?0:engine.ConfirmedCatches;}}public int VendorVisits{get{lock(gate)return engine==null?0:engine.VendorVisits;}}
        public FishingCommand LastCommand{get{lock(gate){if(command==null)return null;return new FishingCommand{Action=command.Action,Purpose=command.Purpose,Key=command.Key,HoldMilliseconds=command.HoldMilliseconds,ExpectedTargetToken=command.ExpectedTargetToken,ExpectedNameHash=command.ExpectedNameHash,ExpectedBiteAtMs=command.ExpectedBiteAtMs,Reason=command.Reason};}}}
        public void Start(FishingPlan value){Start(value,null,false);}
        public void Start(FishingPlan value,FishingCheckpoint checkpoint,bool confirmed)
        {
            lock(gate)
            {
                if(starting||running||ownsKey)throw new InvalidOperationException("Fishing session is already active or being prepared.");starting=true;long ticket=generation;
                try{
                    if(safety.Faulted||faulted)throw new InvalidOperationException("Input failure is latched. Inspect released keys and restart the tool.");
                    var p=value==null?null:value.Clone();if(p==null)throw new ArgumentNullException("plan");p.Validate();
                    var saved=checkpoint==null?null:new FishingCheckpoint{SchemaVersion=checkpoint.SchemaVersion,PlanFingerprint=checkpoint.PlanFingerprint,Phase=checkpoint.Phase,RouteIndex=checkpoint.RouteIndex,VendorSlotsBefore=checkpoint.VendorSlotsBefore,VendorInventoryRevision=checkpoint.VendorInventoryRevision,VendorTargetToken=checkpoint.VendorTargetToken,VendorNameHash=checkpoint.VendorNameHash,MerchantObserved=checkpoint.MerchantObserved};
                    if(!p.Settings.BindingsConfirmed||!p.Settings.AutoLootConfirmed||!p.Settings.AudioConfirmed||!p.Calibration.Confirmed||(p.Settings.EnableVendor&&!p.Settings.AutoSellConfirmed))throw new InvalidOperationException("Confirm fishing/reel bindings, Auto Loot, audio, current-map facing calibration and enabled vendor auto-sell before starting.");
                    long before=clock();if(ticket!=generation||safety.Faulted)throw new InvalidOperationException("Fishing preparation was cancelled.");
                    var o=reader();if(ticket!=generation||safety.Faulted)throw new InvalidOperationException("Fishing preparation was cancelled.");o=o==null?null:o.Clone();
                    long now=clock();if(ticket!=generation||safety.Faulted)throw new InvalidOperationException("Fishing preparation was cancelled.");if(before<0||now<before)throw new InvalidOperationException("Fishing monotonic clock moved backwards.");
                    string problem=GlobalProblem(p,o,now);if(problem!=null)throw new InvalidOperationException(problem);
                    var candidate=new FishingEngine(p);if(saved!=null)candidate.Restore(saved,o,now,confirmed);long countdown=checked(now+5000);if(ticket!=generation||safety.Faulted)throw new InvalidOperationException("Fishing preparation was cancelled.");
                    plan=p;engine=candidate;command=null;castDeadline=-1;due=countdown;lastTime=now;generation++;running=true;armed=holding=ownsKey=waiting=false;status="Countdown";reason="Five-second fishing countdown; focus the game window.";long armedTicket=generation;WriteLog("Fishing session armed; five-second countdown.");
                    Continue(armedTicket);
                }finally{starting=false;}
            }
        }
        public void Tick()
        {
            lock(gate)
            {
                if(!running||faulted||ticking)return;ticking=true;long ticket=generation;
                try{
                    if(!Continue(ticket))return;long now=clock();if(!Continue(ticket))return;if(now<lastTime){StopCore("Fishing monotonic clock moved backwards.");return;}lastTime=now;
                    bool emergency=backend.IsEmergencyPressed();if(!Continue(ticket))return;if(emergency){StopCore("Emergency stop.");return;}
                    if(!ModifiersReleased(ticket))return;
                    FishingObservation o;if(!ReadCurrent(ticket,out o,out now))return;string problem=GlobalProblem(plan,o,now);if(problem!=null){StopCore(problem);return;}
                    if(!armed&&now<due)return;bool foreground=backend.IsTargetForeground();if(!Continue(ticket))return;if(!foreground){StopCore("Target window lost foreground.");return;}armed=true;
                    if(holding){problem=CommandProblem(o,command,false,now);if(problem!=null){StopCore(problem);return;}if(now>=releaseAt){Release();if(!Continue(ticket))return;waiting=true;releasedSequence=o.Game.Sequence;status="WaitingForFrame";}return;}
                    if(waiting&&o.Game.Sequence==releasedSequence)return;waiting=false;
                    var next=engine.Step(o,now);command=next;if(next.Action==FishingAction.Stop){StopCore(next.Reason);return;}if(next.Action==FishingAction.Completed){running=false;status="Completed";reason=next.Reason;return;}
                    if(next.Action==FishingAction.None){status="WaitingForFrame";reason=next.Reason;return;}
                    if(next.Action!=FishingAction.Key||next.HoldMilliseconds<(next.Purpose==FishingPurpose.RestoreFacing?20:60)||next.HoldMilliseconds>250)throw new InvalidOperationException("Unbounded or non-keyboard fishing command rejected.");
                    if(next.Purpose==FishingPurpose.Cast)castDeadline=checked(now+plan.Settings.CastTimeoutMs);
                    WriteLog("Fishing pulse "+next.Purpose+" VK="+next.Key+" "+next.HoldMilliseconds+"ms");if(!Continue(ticket))return;
                    if(!ReadCurrent(ticket,out o,out now))return;problem=GlobalProblem(plan,o,now)??CommandProblem(o,next,true,now);if(problem!=null){StopCore(problem);return;}
                    foreground=backend.IsTargetForeground();if(!Continue(ticket))return;emergency=backend.IsEmergencyPressed();if(!Continue(ticket))return;if(!foreground||emergency){StopCore("Fishing input destination or emergency state changed.");return;}
                    bool down=backend.IsKeyAlreadyDown(next.Key);if(!Continue(ticket))return;if(down){StopCore("The user is already holding this fishing key.");return;}
                    long press;if(!ReadCurrent(ticket,out o,out press))return;problem=GlobalProblem(plan,o,press)??CommandProblem(o,next,true,press);if(problem!=null){StopCore(problem);return;}
                    if(!ModifiersReleased(ticket))return;
                    long deadline=checked(press+next.HoldMilliseconds);lastTime=press;key=next.Key;ownsKey=holding=true;releaseAt=deadline;status="Holding";reason=next.Reason;backend.KeyDown(key);
                }catch(Exception ex){Fault(ex);}finally{ticking=false;}
            }
        }
        bool ModifiersReleased(long ticket)
        {
            foreach(ushort modifier in new ushort[]{0x10,0x11,0x12})
            {
                bool held=backend.IsKeyAlreadyDown(modifier);if(!Continue(ticket))return false;
                if(held){StopCore("A user-held modifier stopped fishing.");return false;}
            }
            return true;
        }
        bool ReadCurrent(long ticket,out FishingObservation observation,out long now)
        {
            observation=reader();now=lastTime;if(!Continue(ticket))return false;observation=observation==null?null:observation.Clone();
            // The UI producer can publish a newer capture while this callback runs.
            // Check it against a clock read after the snapshot, never Tick's old time.
            now=clock();if(!Continue(ticket))return false;if(now<0||now<lastTime){StopCore("Fishing monotonic clock moved backwards.");return false;}lastTime=now;return true;
        }
        string CommandProblem(FishingObservation o,FishingCommand c,bool before,long now)
        {
            if(c==null)return null;var g=o.Game;var f=g.Farm;
            if(c.Purpose==FishingPurpose.Reel&&!o.SignalReady)return "Fishing audio became unavailable during reel input.";
            if(c.Purpose==FishingPurpose.Reel&&before&&(castDeadline<0||now>=castDeadline))return "The authorized fishing cast window elapsed before reel input.";
            if(c.Purpose==FishingPurpose.Reel&&before&&(c.ExpectedBiteAtMs<0||o.LastBiteAtMs!=c.ExpectedBiteAtMs||!NavigationMath.InRange(o.BiteConfidence,.70,1)||now<o.LastBiteAtMs||now-o.LastBiteAtMs>900+plan.Settings.ReelDelayMaxMs))return "The fishing bite candidate changed or became stale before reel input.";
            if((c.Purpose==FishingPurpose.Cast||c.Purpose==FishingPurpose.RestoreFacing)&&(NavigationMath.Distance(f.X-plan.Spot.X,f.Y-plan.Spot.Y,plan.Calibration.AxisScale)>plan.Spot.Tolerance))return "Fishing cast or facing input is allowed only at the recorded fishing spot.";
            if(c.Purpose==FishingPurpose.Cast&&before&&(Math.Abs(NavigationMath.Normalize(plan.Facing-f.Facing))>.12||f.Casting||f.Channeling))return "Fishing facing or the current cast changed before pending cast input.";
            if(c.Purpose==FishingPurpose.Travel&&(!plan.Settings.EnableVendor||plan.VendorRoute==null||FarmEngine.DistanceFromRoute(plan.VendorRoute,plan.Calibration.AxisScale,f.X,f.Y)>.006))return "Fishing travel left its recorded route corridor.";
            if(c.ExpectedTargetToken!=0&&(!f.TargetExists||!f.TargetIdentityKnown||f.TargetToken!=c.ExpectedTargetToken||!f.NameHashKnown||f.TargetNameHash!=c.ExpectedNameHash||!f.AttackabilityKnown||f.TargetCanAttack||f.TargetIsPlayer||!f.TargetDeadKnown||f.TargetDead))return "Saved fishing merchant identity changed before/during interaction.";
            if(c.Purpose==FishingPurpose.OpenMerchant||c.Purpose==FishingPurpose.CloseMerchant)
            {
                if(!g.MerchantKnown)return "Fishing merchant window state became unknown.";
                if(g.MerchantOpen&&(g.MerchantTargetToken!=c.ExpectedTargetToken||g.MerchantNameHash!=c.ExpectedNameHash))return "Fishing merchant window identity changed before/during interaction.";
                if(before&&c.Purpose==FishingPurpose.CloseMerchant&&!g.MerchantOpen)return "Fishing merchant is already closed; no Escape key is authorized.";
                if(before&&c.Purpose==FishingPurpose.OpenMerchant&&g.MerchantOpen)return "Fishing merchant is already open; no duplicate interaction is authorized.";
            }
            return null;
        }
        static string GlobalProblem(FishingPlan p,FishingObservation o,long now)
        {if(now<0||o==null||o.CapturedAtMs<0||now<o.CapturedAtMs||now-o.CapturedAtMs>=900)return "Fishing observation cache is stale or invalid.";return FishingEngine.ValidateLiveObservation(p,o);}
        bool Continue(long ticket){if(ticket!=generation||!running||faulted)return false;if(safety.Faulted){Fault(new InvalidOperationException(safety.Reason));return false;}return true;}
        public void Stop(string why){lock(gate){if(faulted)return;StopCore(String.IsNullOrEmpty(why)?"Fishing stopped by user.":why);}}
        void StopCore(string why){generation++;running=false;status="Stopped";reason=why;if(engine!=null)engine.Stop(why);Release();WriteLog("Fishing stopped: "+why);}
        void Release(){holding=false;if(!ownsKey)return;ownsKey=false;try{backend.KeyUp(key);WriteLog("Fishing released owned key "+key);}catch(Exception ex){faulted=true;running=false;status="Faulted";reason="Fishing key release failed: "+ex.Message;safety.Fail(reason);WriteLog(reason);}}
        void Fault(Exception ex){generation++;faulted=true;running=false;reason="Fishing input failure: "+ex.Message;safety.Fail(reason);Release();status="Faulted";WriteLog(reason);}
        void WriteLog(string message){if(log!=null)try{log(message);}catch{}}
    }
}
