using System;
using System.Drawing;
using WoWProbe;

class GrindSafetyTests
{
    sealed class Backend:IInputBackend
    {
        public bool Foreground=true,Emergency,Held,FailDown,FailUp;public int Downs,Ups;
        public bool IsTargetForeground(){return Foreground;}public bool IsEmergencyPressed(){return Emergency;}public bool IsKeyAlreadyDown(ushort k){return Held;}
        public void KeyDown(ushort k){Downs++;if(FailDown)throw new Exception("partial down");}public void KeyUp(ushort k){Ups++;if(FailUp)throw new Exception("up failed");}
    }
    static int passed,failed;
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static void Assert(bool b){if(!b)throw new Exception("behavior differs");}
    static void Reject(Action action){bool stopped=false;try{action();}catch(InvalidOperationException){stopped=true;}Assert(stopped);}
    static FarmObservation Observation(){return new FarmObservation{Fresh=true,KnownCore=true,MapId=42,MapMode=2,Zone="TEST",X=.5,Y=.5,Facing=0};}
    static GrindInputGuard Guard(Backend b,Func<FarmObservation> read,Action<int> wait,InputFailureLatch latch=null)
    {long now=0;return new GrindInputGuard(b,read,()=>now,ms=>{now+=ms;if(wait!=null)wait(ms);},latch??new InputFailureLatch(),42,2,"TEST");}
    static int Main()
    {
        Test("focus loss rejects the first auxiliary key",delegate{var b=new Backend{Foreground=false};var g=Guard(b,Observation,null);Reject(()=>g.Pulse(87,100));Assert(b.Downs==0&&b.Ups==0);});
        Test("F12 during calibration releases once and cannot restart",delegate{var b=new Backend();var g=Guard(b,Observation,ms=>b.Emergency=true);Reject(()=>g.Pulse(87,250));b.Emergency=false;Reject(()=>g.Pulse(87,100));Assert(b.Downs==1&&b.Ups==1);});
        Test("stale telemetry during a pulse releases owned input",delegate{var b=new Backend();var o=Observation();var g=Guard(b,()=>o,ms=>o.Fresh=false);Reject(()=>g.Pulse(87,250));Assert(b.Downs==1&&b.Ups==1);});
        Test("a map change blocks later chat characters",delegate{var b=new Backend();var o=Observation();var g=Guard(b,()=>o,null);int typed=0;Reject(()=>g.TypeText("abc",c=>{typed++;o.MapId=43;}));Assert(typed==1);});
        Test("user held keys never become tool ownership",delegate{var b=new Backend{Held=true};var g=Guard(b,Observation,null);Reject(()=>g.Pulse(87,100));Assert(b.Downs==0&&b.Ups==0);});
        Test("partial key-down failure releases once and latches input fault",delegate{var b=new Backend{FailDown=true};var latch=new InputFailureLatch();var g=Guard(b,Observation,null,latch);Reject(()=>g.Pulse(87,100));Assert(b.Downs==1&&b.Ups==1&&latch.Faulted);});
        Test("key release failure latches and blocks further input",delegate{var b=new Backend{FailUp=true};var latch=new InputFailureLatch();var g=Guard(b,Observation,null,latch);Reject(()=>g.Pulse(87,100));Reject(()=>g.Pulse(87,100));Assert(b.Downs==1&&b.Ups==1&&latch.Faulted);});
        Test("mouse focus loss after move prevents button down",delegate{var b=new Backend();var g=Guard(b,Observation,null);int buttons=0;Reject(()=>g.Click(new Point(10,10),false,p=>true,p=>b.Foreground=false,(right,up)=>buttons++));Assert(buttons==0);});
        Test("focus loss in the final client-bound check prevents mouse down",delegate{var b=new Backend();var g=Guard(b,Observation,null);int checks=0,buttons=0;Reject(()=>g.Click(new Point(10,10),false,p=>{if(++checks==2)b.Foreground=false;return true;},p=>{},(right,up)=>buttons++));Assert(buttons==0);});
        Test("a read callback cannot hide a shared input fault before key down",delegate{var b=new Backend();var latch=new InputFailureLatch();int reads=0;var g=Guard(b,()=>{if(++reads==2)latch.Fail("callback fault");return Observation();},null,latch);Reject(()=>g.Pulse(87,100));Assert(b.Downs==0&&b.Ups==0);});
        Test("mouse emergency during hold releases the same button",delegate{var b=new Backend();var g=Guard(b,Observation,ms=>b.Emergency=true);int downs=0,ups=0;Reject(()=>g.Click(new Point(10,10),true,p=>true,p=>{},(right,up)=>{Assert(right);if(up)ups++;else downs++;}));Assert(downs==1&&ups==1);});
        Test("points outside current client bounds cannot move the cursor",delegate{var b=new Backend();var g=Guard(b,Observation,null);int moved=0;Reject(()=>g.Click(new Point(-1,0),false,p=>false,p=>moved++,(right,up)=>{}));Assert(moved==0);});
        Test("normal bounded pulse completes and releases once",delegate{var b=new Backend();var g=Guard(b,Observation,null);g.Pulse(87,250);Assert(b.Downs==1&&b.Ups==1);});
        Test("arbitrary friendly target is not a verified spirit healer",delegate{var o=Observation();o.PlayerGhost=true;o.TargetExists=true;Assert(!GrindRecoveryPolicy.IsHealer(o));});
        Test("spirit healer requires fresh ghost identity and full name hash",delegate{var o=Observation();o.PlayerGhost=true;o.TargetExists=true;o.TargetIdentityKnown=true;o.TargetToken=12;o.NameHashKnown=true;o.TargetNameHash=TargetCatalog.HashName("灵魂医者");o.TargetName="灵魂医者";o.AttackabilityKnown=true;Assert(GrindRecoveryPolicy.IsHealer(o));o.Fresh=false;Assert(!GrindRecoveryPolicy.IsHealer(o));o.Fresh=true;o.TargetIsPlayer=true;Assert(!GrindRecoveryPolicy.IsHealer(o));});
        Test("an emergency stop cannot become an automatic death retry",delegate{var o=Observation();o.PlayerDead=true;Assert(!GrindRecoveryPolicy.ShouldRevive("Stopped","Emergency stop.",o));Assert(GrindRecoveryPolicy.ShouldRevive("Stopped","Player death or ghost state stopped farming.",o));o.Fresh=false;Assert(!GrindRecoveryPolicy.ShouldRevive("Stopped","Player death or ghost state stopped farming.",o));});
        Test("skill range cannot authorize a healer approach with secret position",delegate{var o=Observation();o.PlayerGhost=true;o.TargetExists=true;o.TargetIdentityKnown=true;o.TargetToken=12;o.NameHashKnown=true;o.TargetNameHash=TargetCatalog.HashName("灵魂医者");o.AttackabilityKnown=true;o.RangeKnown=true;o.TargetInRange=true;Assert(!GrindRecoveryPolicy.CanApproachHealer(o));o.TargetPositionKnown=true;o.TargetX=.51;o.TargetY=.5;Assert(GrindRecoveryPolicy.CanApproachHealer(o));o.TargetY=Double.NaN;Assert(!GrindRecoveryPolicy.CanApproachHealer(o));});
        Test("auxiliary auto-attack cannot target a player friendly or unknown unit",delegate{var o=Observation();o.TargetExists=true;o.TargetIdentityKnown=true;o.TargetToken=8;o.AttackabilityKnown=true;o.TargetCanAttack=true;o.TargetDeadKnown=true;Assert(GrindRecoveryPolicy.CanAttack(o));o.TargetIsPlayer=true;Assert(!GrindRecoveryPolicy.CanAttack(o));o.TargetIsPlayer=false;o.TargetCanAttack=false;Assert(!GrindRecoveryPolicy.CanAttack(o));o.TargetCanAttack=true;o.AttackabilityKnown=false;Assert(!GrindRecoveryPolicy.CanAttack(o));});
        Console.WriteLine("Grind safety tests: "+passed+" passed, "+failed+" failed. Fake input only.");return failed==0?0:1;
    }
}
