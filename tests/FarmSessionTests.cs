using System;
using System.Collections.Generic;
using WoWProbe;

class FarmSessionTests
{
    static int passed,failed;static long now;static FarmObservation observation;
    sealed class Fake:IInputBackend
    {public bool Focus=true,Emergency,FailUp,FailDown,AlreadyDown;public Action<string> Callback;public List<ushort> Downs=new List<ushort>();public int Ups;void Call(string point){if(Callback!=null)Callback(point);}public bool IsTargetForeground(){Call("focus");return Focus;}public bool IsEmergencyPressed(){Call("emergency");return Emergency;}public bool IsKeyAlreadyDown(ushort key){Call("already:"+key);return AlreadyDown;}public void KeyDown(ushort key){Downs.Add(key);Call("down");if(FailDown)throw new Exception("partial down failed");}public void KeyUp(ushort key){Ups++;Call("up");if(FailUp)throw new Exception("up failed");}}
    static RouteDocument Route(){return new RouteDocument{RouteId="battle",Name="Battle",MapId=42,MapMode=2,Zone="Test",Points=new List<Waypoint>{new Waypoint{X=.2,Y=.2},new Waypoint{X=.22,Y=.2}}};}
    static BattleProfile Profile(){return new BattleProfile{Name="Test",Targets=new List<BattleTarget>{new BattleTarget{Name="Mob",NameHash=TargetCatalog.HashName("Mob"),Kind="monster"}},BindingsConfirmed=true,AllowApproach=false,Calibration=new NavigationCalibration{MapId=42,MapMode=2,Zone="Test",Confirmed=true,FacingSign=1}};}
    static FarmSession New(Fake backend,Action<string> log=null,InputFailureLatch latch=null)
    {
        now=0;observation=new FarmObservation{Fresh=true,KnownCore=true,Health=100,Power=100,PowerType=0,MapId=42,MapMode=2,Zone="Test",X=.2,Y=.2,Facing=Math.PI/2,Sequence=1,TargetExists=true,TargetDeadKnown=true,TargetHealth=100,TargetName="Mob",TargetToken=7,TargetNameHash=TargetCatalog.HashName("Mob"),TargetIdentityKnown=true,NameHashKnown=true,AttackabilityKnown=true,TargetCanAttack=true,RangeKnown=true,TargetInRange=true,RangeSlot=1,TargetPositionKnown=true,TargetX=.202,TargetY=.2,CastingKnown=true,ActionsKnown=true,KnownActionsMask=15,ReadyActionsMask=15,UsableActionsMask=15,LootEventsKnown=true,CastEventsKnown=true};
        return new FarmSession(backend,delegate{return now;},delegate{return observation;},log,latch??new InputFailureLatch());
    }
    static void Pump(FarmSession s,bool skill)
    {for(int i=0;i<150&&s.IsActive;i++){now=5000+i*100;observation.Sequence++;s.Tick();if(s.IsHolding&&(!skill||s.LastCommand.Action==FarmAction.Skill1||s.LastCommand.Action==FarmAction.Skill2||s.LastCommand.Action==FarmAction.Skill3||s.LastCommand.Action==FarmAction.Skill4))return;}throw new Exception("Expected held pulse, state="+s.EngineState+" "+s.LastReason);}
    static void Run(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static void Check(bool value){if(!value)throw new Exception("assertion failed");}
    static void Reject(Action a){try{a();}catch(InvalidOperationException){return;}catch(ArgumentException){return;}throw new Exception("Expected refusal");}
    static bool Attack(ushort key){return key>=0x31&&key<=0x34;}
    static void Until(FarmSession s,Func<bool> done)
    {for(int i=0;i<150&&s.IsActive&&!done();i++){now=5000+i*100;observation.Sequence++;s.Tick();}Check(done());}
    static void CancelDuringArming(bool fromClock)
    {
        var f=new Fake();New(f);FarmSession s=null;bool cancel=true;
        s=new FarmSession(f,delegate{if(fromClock&&cancel){cancel=false;s.Stop("clock cancelled arming");}return now;},
            delegate{if(!fromClock&&cancel){cancel=false;s.Stop("reader cancelled arming");}return observation;},null,new InputFailureLatch());
        Reject(delegate{s.Start(Route(),Profile(),false,false,0);});Check(!s.IsActive&&!s.IsHolding&&s.Status=="Stopped");
        now=10000;observation.Sequence++;s.Tick();Check(f.Downs.Count==0&&f.Ups==0);
        s.Start(Route(),Profile(),false,false,0);s.Tick();Check(s.IsActive&&s.Status=="Countdown"&&f.Downs.Count==0);s.Stop("done");
    }
    static FarmSession HeldCorpseAction(Fake f,bool skin)
    {
        var s=New(f);var p=Profile();p.Skinning=skin;s.Start(Route(),p,false,false,0);Pump(s,true);
        observation.TargetDead=true;observation.TargetHealth=0;observation.TargetCanAttack=false;
        observation.LootKnown=true;observation.Lootable=true;observation.SkinKnown=true;observation.Skinnable=true;observation.SkinSuccessKnown=true;
        now+=200;s.Tick();
        for(int i=0;i<10&&(!s.IsHolding||s.LastCommand.Action!=FarmAction.Interact);i++){now+=200;observation.Sequence++;s.Tick();}
        Check(s.IsHolding&&s.LastCommand.Action==FarmAction.Interact);if(!skin)return s;
        observation.LootOpened++;observation.LootClosed++;observation.Lootable=false;now+=10;s.Tick();
        for(int i=0;i<10&&(!s.IsHolding||s.LastCommand.Action!=FarmAction.Skin);i++){now+=200;observation.Sequence++;s.Tick();}
        Check(s.IsHolding&&s.LastCommand.Action==FarmAction.Skin);return s;
    }
    static int Main()
    {
        Run("unconfirmed bindings and target-position adapter cannot arm",delegate{var f=new Fake();var s=New(f);var p=Profile();p.BindingsConfirmed=false;Reject(delegate{s.Start(Route(),p,false,false,0);});p.BindingsConfirmed=true;p.AllowApproach=true;p.ApproachPositionConfirmed=false;Reject(delegate{s.Start(Route(),p,false,false,0);});Check(f.Downs.Count==0);});
        Run("five second countdown emits nothing",delegate{var f=new Fake{Focus=false};var s=New(f);s.Start(Route(),Profile(),false,false,0);now=4999;s.Tick();Check(f.Downs.Count==0&&s.IsActive);s.Stop("done");});
        Run("short pulse releases and waits for a newer frame",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Profile(),false,false,0);Pump(s,false);int count=f.Downs.Count;now+=251;s.Tick();for(int i=0;i<6;i++){now+=50;s.Tick();}Check(f.Ups==count&&f.Downs.Count==count);s.Stop("done");});
        Run("F12 cancels countdown",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Profile(),false,false,0);f.Emergency=true;now=100;s.Tick();Check(!s.IsActive&&f.Downs.Count==0);});
        Run("focus loss releases active pulse without resuming",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Profile(),false,false,0);Pump(s,false);f.Focus=false;now+=10;s.Tick();Check(!s.IsActive&&f.Ups==f.Downs.Count);f.Focus=true;now+=5000;s.Tick();Check(f.Ups==f.Downs.Count);});
        Run("invalid matched-packet stream cancels active input",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Profile(),false,false,0);Pump(s,false);observation.Fresh=false;s.Tick();Check(!s.IsActive&&f.Ups==f.Downs.Count);});
        Run("target swap during action callback cancels before attacking",delegate{var f=new Fake();var s=New(f,delegate(string text){if(text.Contains("Skill"))observation.TargetToken=8;});s.Start(Route(),Profile(),false,false,0);for(int i=0;i<150&&s.IsActive;i++){now=5000+i*100;observation.Sequence++;s.Tick();}Check(!s.IsActive&&!f.Downs.Exists(Attack));});
        Run("player target cannot receive a pending skill",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Profile(),false,false,0);Pump(s,true);int count=f.Downs.Count;observation.TargetIsPlayer=true;s.Tick();Check(!s.IsActive&&f.Downs.Count==count&&f.Ups==count);});
        Run("dead ghost full bag and map changes stop owned input",delegate{for(int k=0;k<4;k++){var f=new Fake();var s=New(f);s.Start(Route(),Profile(),false,false,0);Pump(s,false);if(k==0)observation.PlayerDead=true;if(k==1)observation.PlayerGhost=true;if(k==2)observation.BagFull=true;if(k==3)observation.Zone="Other";s.Tick();Check(!s.IsActive&&f.Ups==f.Downs.Count);}});
        Run("global release fault blocks replacement farming session",delegate{var f=new Fake();var latch=new InputFailureLatch();var s=New(f,null,latch);s.Start(Route(),Profile(),false,false,0);Pump(s,false);f.FailUp=true;s.Stop("stop");Check(latch.Faulted);var next=new FarmSession(f,delegate{return now;},delegate{return observation;},null,latch);Reject(delegate{next.Start(Route(),Profile(),false,false,0);});});
        Run("reentrant log cancellation never emits orphan key-down",delegate{var f=new Fake();FarmSession s=null;s=New(f,delegate(string text){if(text.StartsWith("Farm pulse"))s.Stop("callback cancel");});s.Start(Route(),Profile(),false,false,0);for(int i=0;i<150&&s.IsActive;i++){now=5000+i*100;observation.Sequence++;s.Tick();}Check(!s.IsActive&&f.Downs.Count==0&&f.Ups==0);});
        Run("logging exceptions do not prevent matching release",delegate{var f=new Fake();var s=New(f,delegate{throw new Exception("log unavailable");});s.Start(Route(),Profile(),false,false,0);Pump(s,false);s.Stop("done");Check(f.Ups==f.Downs.Count);});
        Run("pending skill rechecks range cooldown casting and full target hash after logging",delegate{
            Action<FarmObservation>[] faults={o=>o.RangeKnown=false,o=>o.TargetInRange=false,o=>o.RangeSlot=2,o=>o.ActionsKnown=false,o=>o.KnownActionsMask=0,o=>o.ReadyActionsMask=0,o=>o.UsableActionsMask=0,o=>o.CastingKnown=false,o=>o.Casting=true,o=>o.Channeling=true,o=>o.TargetNameHash++,o=>o.NameHashKnown=false};
            foreach(var fault in faults){var f=new Fake();bool changed=false;var s=New(f,delegate(string t){if(t.StartsWith("Farm pulse Skill")){changed=true;fault(observation);}});s.Start(Route(),Profile(),false,false,0);Until(s,delegate{return changed;});Check(!s.IsActive&&!f.Downs.Exists(Attack)&&f.Ups==0);}
        });
        Run("backend key-state callbacks cannot change a target or ready action before key-down",delegate{
            Action<FarmObservation>[] faults={o=>o.TargetToken++,o=>o.TargetNameHash++,o=>o.ReadyActionsMask=0,o=>o.Casting=true};
            foreach(var fault in faults){var f=new Fake();bool changed=false;var s=New(f);f.Callback=delegate(string point){if(point=="already:49"){changed=true;fault(observation);}};s.Start(Route(),Profile(),false,false,0);Until(s,delegate{return changed;});Check(!s.IsActive&&f.Downs.Count==0&&f.Ups==0);}
        });
        Run("final clock callback cannot invalidate a pending skill",delegate{var f=new Fake();New(f);bool changeAtClock=false,changed=false;var s=new FarmSession(f,delegate{if(changeAtClock){changeAtClock=false;changed=true;observation.ReadyActionsMask=0;}return now;},delegate{return observation;},delegate(string t){if(t.StartsWith("Farm pulse Skill"))changeAtClock=true;},new InputFailureLatch());s.Start(Route(),Profile(),false,false,0);Until(s,delegate{return changed;});Check(!s.IsActive&&f.Downs.Count==0&&f.Ups==0);});
        Run("reentrant Tick cannot overwrite an outer planned skill",delegate{var f=new Fake();FarmSession s=null;bool nested=false;s=New(f,delegate(string text){if(!nested&&text.StartsWith("Farm pulse Skill")){nested=true;observation.Sequence++;s.Tick();}});s.Start(Route(),Profile(),false,false,0);Until(s,delegate{return nested;});Check(s.IsHolding&&s.LastCommand.Action==FarmAction.Skill1&&f.Downs.Count==1);s.Stop("done");Check(f.Ups==1);});
        Run("callback cancellation and restart cannot send an old skill through a new countdown",delegate{var f=new Fake();FarmSession s=null;bool restarted=false;s=New(f,delegate(string text){if(!restarted&&text.StartsWith("Farm pulse Skill")){restarted=true;s.Stop("replace old run");s.Start(Route(),Profile(),false,false,0);}});s.Start(Route(),Profile(),false,false,0);Until(s,delegate{return restarted;});Check(s.IsActive&&!s.IsHolding&&s.Status=="Countdown"&&f.Downs.Count==0&&f.Ups==0);s.Stop("done");});
        Run("shared failure during pulse logging stops immediately without input",delegate{var f=new Fake();var latch=new InputFailureLatch();bool tripped=false;var s=New(f,delegate(string text){if(text.StartsWith("Farm pulse Skill")){tripped=true;latch.Fail("other input mode release failed");}},latch);s.Start(Route(),Profile(),false,false,0);Until(s,delegate{return tripped;});Check(!s.IsActive&&f.Downs.Count==0&&f.Ups==0);});
        Run("deadline overflow never releases a key that was never pressed",delegate{var f=new Fake();bool changed=false;var latch=new InputFailureLatch();var s=New(f,delegate(string text){if(text.StartsWith("Farm pulse Skill")){changed=true;now=Int64.MaxValue-50;}},latch);s.Start(Route(),Profile(),false,false,0);Until(s,delegate{return changed;});Check(!s.IsActive&&latch.Faulted&&f.Downs.Count==0&&f.Ups==0);});
        Run("loss of range knowledge during an owned skill releases and stops",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Profile(),false,false,0);Pump(s,true);observation.RangeKnown=false;now+=10;s.Tick();Check(!s.IsActive&&f.Ups==f.Downs.Count);});
        Run("a zero health target without a dead flag cannot receive a pending loot action",delegate{var f=new Fake();bool changed=false;var s=New(f,delegate(string text){if(text.StartsWith("Farm pulse Interact")){changed=true;observation.TargetDead=false;observation.TargetHealth=0;}});s.Start(Route(),Profile(),false,false,0);Pump(s,true);observation.TargetDead=true;observation.TargetHealth=0;observation.TargetCanAttack=false;observation.LootKnown=true;observation.Lootable=true;now+=200;s.Tick();for(int i=0;i<10&&!changed&&s.IsActive;i++){now+=200;observation.Sequence++;s.Tick();}Check(changed&&!s.IsActive&&!f.Downs.Contains(0x46));});
        Run("loot completion events cannot conceal a simultaneous target identity swap",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Profile(),false,false,0);Pump(s,true);observation.TargetDead=true;observation.TargetHealth=0;observation.TargetCanAttack=false;observation.LootKnown=true;observation.Lootable=true;now+=200;s.Tick();for(int i=0;i<10&&(!s.IsHolding||s.LastCommand.Action!=FarmAction.Interact);i++){now+=200;observation.Sequence++;s.Tick();}Check(s.IsHolding&&s.LastCommand.Action==FarmAction.Interact);observation.LootOpened++;observation.LootClosed++;observation.TargetToken++;now+=10;s.Tick();Check(!s.IsActive&&!s.IsHolding&&f.Ups==f.Downs.Count);});
        Run("partial key-down errors release once and share the failure latch",delegate{var f=new Fake{FailDown=true};var latch=new InputFailureLatch();var s=New(f,null,latch);s.Start(Route(),Profile(),false,false,0);Until(s,delegate{return !s.IsActive;});Check(latch.Faulted&&f.Downs.Count==1&&f.Ups==1);now+=5000;s.Tick();Check(f.Ups==1);});
        Run("user-held skill key produces no key-down or tool-owned release",delegate{var f=new Fake{AlreadyDown=true};var s=New(f);s.Start(Route(),Profile(),false,false,0);Until(s,delegate{return !s.IsActive;});Check(f.Downs.Count==0&&f.Ups==0);});
        Run("held approach releases immediately when target position becomes unknown invalid or too far",delegate{
            Action<FarmObservation>[] faults={o=>o.TargetPositionKnown=false,o=>o.TargetX=Double.NaN,o=>o.TargetY=1.2,o=>o.TargetX=.23};
            foreach(var fault in faults){var f=new Fake();var s=New(f);var p=Profile();p.AllowApproach=true;p.ApproachPositionConfirmed=true;observation.TargetInRange=false;s.Start(Route(),p,false,false,0);Pump(s,false);Check(s.LastCommand.Action==FarmAction.Forward);fault(observation);now+=10;s.Tick();Check(!s.IsActive&&!s.IsHolding&&f.Ups==f.Downs.Count);}
        });
        Run("live safety corridor is checked during an owned skill pulse",delegate{var f=new Fake();var s=New(f);s.Start(Route(),Profile(),false,false,0);Pump(s,true);observation.X=.21;now+=10;s.Tick();Check(s.IsHolding);observation.Y=.221;now+=10;s.Tick();Check(!s.IsActive&&!s.IsHolding&&f.Ups==f.Downs.Count);});
        Run("reader cancellation while arming cannot be overwritten by countdown setup",delegate{CancelDuringArming(false);});
        Run("clock cancellation while arming cannot be overwritten by countdown setup",delegate{CancelDuringArming(true);});
        Run("held loot stops immediately if loot state or event support becomes unknown",delegate{
            Action<FarmObservation>[] faults={o=>o.LootKnown=false,o=>o.LootEventsKnown=false};
            foreach(var fault in faults){var f=new Fake();var s=HeldCorpseAction(f,false);fault(observation);now+=10;s.Tick();Check(!s.IsActive&&!s.IsHolding&&f.Ups==f.Downs.Count);}
        });
        Run("held skin stops immediately if required corpse or evidence knowledge is lost",delegate{
            Action<FarmObservation>[] faults={o=>o.SkinKnown=false,o=>o.SkinSuccessKnown=false,o=>o.CastEventsKnown=false,o=>o.LootKnown=false};
            foreach(var fault in faults){var f=new Fake();var s=HeldCorpseAction(f,true);fault(observation);now+=10;s.Tick();Check(!s.IsActive&&!s.IsHolding&&f.Ups==f.Downs.Count);}
        });
        Console.WriteLine("Farm session tests: "+passed+" passed, "+failed+" failed. Fake backend only.");return failed==0?0:1;
    }
}
