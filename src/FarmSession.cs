using System;

namespace WoWProbe
{
    /// <summary>Owns bounded live pulses; the farming engine and simulator never construct this class.</summary>
    public sealed class FarmSession
    {
        readonly object gate=new object();
        readonly IInputBackend backend;
        readonly Func<long> clock;
        readonly Func<FarmObservation> reader;
        readonly Action<string> log;
        readonly InputFailureLatch safety;
        FarmEngine engine;
        RouteDocument route;
        BattleProfile profile;
        FarmCommand command;
        string fingerprint,status="Idle",reason="";
        bool running,holding,ownsKey,armed,waiting,faulted,reverse,loop,ticking,starting;
        ushort key;
        long due,releaseAt,lastTime,generation;
        int releaseSequence;
        uint lootOpenAtSend,lootCloseAtSend,skinAtSend,targetHashAtSend;

        public FarmSession(IInputBackend backend,Func<long> clock,Func<FarmObservation> reader,Action<string> log,InputFailureLatch safety)
        {
            if(backend==null||clock==null||reader==null||safety==null)throw new ArgumentNullException("session dependencies");
            this.backend=backend;this.clock=clock;this.reader=reader;this.log=log;this.safety=safety;
        }
        public bool IsActive{get{lock(gate)return running;}}
        public bool IsHolding{get{lock(gate)return holding;}}
        public string Status{get{lock(gate)return status;}}
        public string LastReason{get{lock(gate)return reason;}}
        public string EngineState{get{lock(gate)return engine==null?"Idle":engine.Status;}}
        public int RouteIndex{get{lock(gate)return engine==null?0:engine.RouteIndex;}}
        public FarmCommand LastCommand
        {get{lock(gate)return command==null?null:new FarmCommand{Action=command.Action,HoldMilliseconds=command.HoldMilliseconds,ExpectedTargetToken=command.ExpectedTargetToken,RequireAliveTarget=command.RequireAliveTarget,RequireDeadTarget=command.RequireDeadTarget,Reason=command.Reason};}}
        public RouteCheckpoint CurrentCheckpoint
        {
            get{lock(gate){if(engine==null)return null;int index=engine.Completed?route.Points.Count:Math.Min(route.Points.Count-1,Math.Max(0,engine.RouteIndex));
                return new RouteCheckpoint{RouteFingerprint=fingerprint,NextIndex=index,Completed=engine.Completed,MapId=route.MapId,MapMode=route.MapMode,Zone=route.Zone,Reverse=reverse,Loop=loop};}}
        }
        public void Start(RouteDocument route,BattleProfile profile,bool reverse,bool loop,int startIndex)
        {
            lock(gate)
            {
                if(starting)throw new InvalidOperationException("A farming session is already being armed.");
                starting=true;long startTicket=generation;
                try
                {
                    if(safety.Faulted||faulted)throw new InvalidOperationException("Input failure is latched. Release keys manually, inspect the game and restart the tool.");
                    if(running||ownsKey)throw new InvalidOperationException("A farming session is already active.");
                    if(route==null||profile==null)throw new ArgumentNullException("farming settings");
                    RouteDocument r=route.Clone();r.Validate();BattleProfile p=profile.Clone();p.Validate();
                    if(!p.BindingsConfirmed||!p.Calibration.Confirmed)throw new InvalidOperationException("Live key bindings and current-map calibration must be confirmed.");
                    if(p.AllowApproach&&!p.ApproachPositionConfirmed)throw new InvalidOperationException("Live approach requires a confirmed target-position source.");
                    if(startIndex<0||startIndex>=r.Points.Count)throw new ArgumentOutOfRangeException("startIndex");
                    FarmObservation initial=reader();string problem=GlobalProblem(initial,r,p);
                    if(problem!=null)throw new InvalidOperationException(problem);
                    if(initial.InCombat)throw new InvalidOperationException("Cannot arm during unexpected combat.");
                    FarmEngine candidate=new FarmEngine(r,p,reverse,loop,startIndex);
                    long time=clock();if(time<0)throw new InvalidOperationException("Invalid monotonic clock.");
                    if(startTicket!=generation||safety.Faulted||faulted)throw new InvalidOperationException("Farming arming was cancelled or input safety failed.");
                    long countdown=checked(time+5000);
                    this.route=r;this.profile=p;engine=candidate;fingerprint=r.Fingerprint();this.reverse=reverse;this.loop=loop;
                    due=countdown;lastTime=time;generation++;
                    running=true;armed=holding=ownsKey=waiting=false;command=null;status="Countdown";reason="Five-second countdown; focus the game window.";
                    WriteLog("Farm session armed at route index "+startIndex);
                }
                finally{starting=false;}
            }
        }
        public void Tick()
        {
            lock(gate)
            {
                // Monitor callbacks and log callbacks can reenter on the same thread. Never run two planners at once.
                if(!running||faulted||ticking)return;
                ticking=true;long ticket=generation;
                try
                {
                    if(!Continue(ticket))return;
                    long time=clock();if(!Continue(ticket))return;
                    if(time<lastTime){StopCore("Monotonic clock moved backwards.");return;}lastTime=time;
                    bool emergency=backend.IsEmergencyPressed();if(!Continue(ticket))return;
                    if(emergency){StopCore("Emergency stop.");return;}
                    FarmObservation current=reader();if(!Continue(ticket))return;
                    string problem=LiveProblem(current);if(problem!=null){StopCore(problem);return;}
                    if(!armed&&time<due)return;
                    bool foreground=backend.IsTargetForeground();if(!Continue(ticket))return;
                    if(!foreground){StopCore("Target window lost foreground.");return;}armed=true;
                    if(holding)
                    {
                        problem=LiveProblem(current)??TargetProblem(current,command,false,targetHashAtSend,profile);
                        if(problem!=null){StopCore(problem);return;}
                        bool completion=CompletedInteraction(current);
                        if(time>=releaseAt||completion||(command.RequireAliveTarget&&current.TargetDeadKnown&&current.TargetDead))
                        {
                            Release();if(!Continue(ticket))return;
                            waiting=true;releaseSequence=current.Sequence;status="WaitingForFrame";
                        }
                        return;
                    }
                    if(waiting&&current.Sequence==releaseSequence)return;waiting=false;
                    FarmCommand next=engine.Step(current,time);command=next;uint expectedHash=current.TargetNameHash;
                    if(next.Action==FarmAction.Stop){StopCore(next.Reason);return;}
                    if(next.Action==FarmAction.Completed){running=false;status="Completed";reason=next.Reason;WriteLog("Farm cycle completed.");return;}
                    if(next.Action==FarmAction.None){status="WaitingForFrame";reason=next.Reason;return;}
                    if(next.HoldMilliseconds<60||next.HoldMilliseconds>250)throw new InvalidOperationException("Unbounded farming pulse rejected.");
                    ushort selected=Key(next.Action,profile.Movement);
                    WriteLog("Farm pulse "+next.Action+" "+next.HoldMilliseconds+"ms token="+next.ExpectedTargetToken);
                    if(!Continue(ticket))return;
                    FarmObservation latest=reader();if(!Continue(ticket))return;
                    problem=LiveProblem(latest)??TargetProblem(latest,next,true,expectedHash,profile);
                    if(problem!=null){StopCore(problem);return;}
                    foreground=backend.IsTargetForeground();if(!Continue(ticket))return;
                    emergency=backend.IsEmergencyPressed();if(!Continue(ticket))return;
                    if(!foreground||emergency){StopCore("Input destination or emergency state changed before the pulse.");return;}
                    bool alreadyDown=backend.IsKeyAlreadyDown(selected);if(!Continue(ticket))return;
                    if(alreadyDown){StopCore("The user is already holding this key.");return;}
                    long pressTime=clock();if(!Continue(ticket))return;
                    if(pressTime<lastTime){StopCore("Monotonic clock moved backwards.");return;}
                    // Log, backend and clock callbacks may invalidate the action. Read once more after all of them.
                    latest=reader();if(!Continue(ticket))return;
                    problem=LiveProblem(latest)??TargetProblem(latest,next,true,expectedHash,profile);
                    if(problem!=null){StopCore(problem);return;}
                    // An overflow cannot create ownership of a key that was never sent.
                    long deadline=checked(pressTime+next.HoldMilliseconds);lastTime=pressTime;
                    key=selected;ownsKey=holding=true;releaseAt=deadline;status="Holding";reason=next.Reason;targetHashAtSend=expectedHash;
                    lootOpenAtSend=latest.LootOpened;lootCloseAtSend=latest.LootClosed;skinAtSend=latest.SkinSuccess;
                    backend.KeyDown(key);
                }
                catch(Exception ex){Fault(ex);}
                finally{ticking=false;}
            }
        }
        public void Stop(string why){lock(gate){if(faulted)return;StopCore(String.IsNullOrEmpty(why)?"Stopped by user.":why);}}
        void StopCore(string why)
        {generation++;running=false;status="Stopped";reason=why;if(engine!=null&&!engine.Completed)engine.Stop(why);Release();WriteLog("Farm stopped: "+why);}
        void Release()
        {
            holding=false;if(!ownsKey)return;ownsKey=false;
            try{backend.KeyUp(key);WriteLog("Farm released owned key "+key);}
            catch(Exception ex){faulted=true;running=false;status="Faulted";reason="Key release failed: "+ex.Message;safety.Fail(reason);WriteLog(reason);}
        }
        void Fault(Exception ex)
        {generation++;faulted=true;running=false;reason="Farm input failure: "+ex.Message;safety.Fail(reason);Release();status="Faulted";WriteLog(reason);}
        bool Continue(long ticket)
        {
            if(ticket!=generation||!running||faulted)return false;
            if(safety.Faulted){Fault(new InvalidOperationException(safety.Reason));return false;}
            return true;
        }
        void WriteLog(string text){if(log!=null)try{log(text);}catch{}}
        string LiveProblem(FarmObservation o)
        {
            string problem=GlobalProblem(o,route,profile);if(problem!=null)return problem;
            if(o.InCombat&&!engine.EncounterActive)return "Unexpected combat occurred before a saved target was locked.";
            return null;
        }
        bool CompletedInteraction(FarmObservation o)
        {return command!=null&&((command.Action==FarmAction.Interact&&o.LootEventsKnown&&o.LootOpened!=lootOpenAtSend&&o.LootClosed!=lootCloseAtSend)||(command.Action==FarmAction.Skin&&o.SkinSuccessKnown&&o.SkinSuccess!=skinAtSend));}
        internal static string TargetProblem(FarmObservation o,FarmCommand c,bool before,uint expectedHash,BattleProfile p)
        {
            if(c==null)return null;
            if(before&&(o.Casting||o.Channeling))return "Casting or channeling changed before pending input.";
            if(c.ExpectedTargetToken==0)return null;
            if(!o.TargetExists||!o.TargetIdentityKnown||o.TargetToken!=c.ExpectedTargetToken||!o.NameHashKnown||expectedHash==0||o.TargetNameHash!=expectedHash)return "Locked target identity or full-name hash changed.";
            if(o.TargetIsPlayer)return "A player cannot receive this farming action.";
            if(!o.TargetDeadKnown||!o.AttackabilityKnown)return "Locked target death or attackability became unknown.";
            if(c.RequireDeadTarget&&!o.TargetDead)return "Loot and skin input requires an explicitly confirmed dead target.";
            bool skill=c.Action>=FarmAction.Skill1&&c.Action<=FarmAction.Skill4;
            if(c.RequireAliveTarget)
            {
                if(o.TargetDead){if(before)return "Pending live-target action no longer has a live target.";return null;}
                if(!o.TargetCanAttack)return "Locked live target is no longer attackable.";
                if(!o.RangeKnown||o.RangeSlot!=p.RangeSlot)return "Configured range observation became unknown or changed slots.";
                if(skill&&!o.TargetInRange)return "Locked skill target moved out of range.";
                if(before&&(o.TargetHealth<=0||o.TargetHealth>100))return "Pending live-target action lacks known positive target health.";
                if(before&&skill)
                {
                    int mask=1<<((int)c.Action-(int)FarmAction.Skill1);
                    if(!o.ActionsKnown||(o.KnownActionsMask&mask)==0||(o.ReadyActionsMask&mask)==0||(o.UsableActionsMask&mask)==0)return "Pending skill is no longer known, ready and usable.";
                }
                if(!skill&&(!o.TargetPositionKnown||!Finite(o.TargetX)||!Finite(o.TargetY)||o.TargetX<0||o.TargetX>1||o.TargetY<0||o.TargetY>1||NavigationMath.Distance(o.TargetX-o.X,o.TargetY-o.Y,p.Calibration.AxisScale)>0.02))return "Approach lost its verified target position or distance limit.";
            }
            if(c.Action==FarmAction.Interact)
            {
                if(!o.LootKnown||!o.LootEventsKnown)return "Loot state or event support became unknown.";
                if(before&&!o.Lootable)return "Pending loot no longer has a lootable corpse.";
            }
            if(c.Action==FarmAction.Skin)
            {
                if(!o.SkinKnown||!o.SkinSuccessKnown||!o.CastEventsKnown||!o.LootKnown)return "Skinning corpse state or dedicated evidence support became unknown.";
                if(before&&(!o.Skinnable||o.Lootable))return "Pending skin no longer has a looted, skinnable corpse.";
            }
            return null;
        }
        internal static string GlobalProblem(FarmObservation o,RouteDocument route,BattleProfile p)
        {
            if(o==null||!o.Fresh)return "Matching combat observations are unavailable or stale.";
            if(!o.KnownCore||o.ReadFailures!=0)return "Required core state is unknown or an API read failed.";
            if(o.PlayerDead||o.PlayerGhost)return "Player death or ghost state stopped farming.";
            if(o.BagFull)return "Full bags stopped farming.";
            if(o.Health<0||o.Health>100||o.Health<=p.CriticalHealth)return "Health is unknown or critical.";
            if(o.PowerType<0||o.PowerType>=255||(o.PowerType==0&&(o.Power<0||o.Power>100)))return "Required power type or mana is unknown.";
            if(!o.CastingKnown)return "Casting state became unknown.";
            if(o.MapId!=route.MapId||o.MapMode!=route.MapMode||!String.Equals(o.Zone,route.Zone,StringComparison.Ordinal))return "Farming map identity or zone changed.";
            if(!Finite(o.X)||!Finite(o.Y)||o.X<0||o.X>1||o.Y<0||o.Y>1||!Finite(o.Facing)||o.Facing<0||o.Facing>Math.PI*2)return "Player position or facing is invalid.";
            if(FarmEngine.DistanceFromRoute(route,p.Calibration.AxisScale,o.X,o.Y)>0.02)return "Player moved outside the calibrated 0.02 safety corridor around the recorded route.";
            return null;
        }
        static bool Finite(double d){return !Double.IsNaN(d)&&!Double.IsInfinity(d);}
        static ushort Key(FarmAction action,RouteKeyBindings b)
        {
            switch(action)
            {
                case FarmAction.Forward:return b.Forward;case FarmAction.TurnLeft:return b.TurnLeft;case FarmAction.TurnRight:return b.TurnRight;case FarmAction.StrafeLeft:return b.StrafeLeft;case FarmAction.StrafeRight:return b.StrafeRight;case FarmAction.Jump:return b.Jump;
                case FarmAction.SelectTarget:return 9;case FarmAction.Skill1:return 0x31;case FarmAction.Skill2:return 0x32;case FarmAction.Skill3:return 0x33;case FarmAction.Skill4:return 0x34;case FarmAction.Interact:return 0x46;case FarmAction.Skin:return 0x35;case FarmAction.Heal:return 0x38;case FarmAction.Mana:return 0x39;
                default:throw new ArgumentException("Unsupported farm input action.");
            }
        }
    }
}
