using System;
using WoWProbe;

class GrindHealerApproachTests
{
    static int passed,failed;
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static void Assert(bool condition,string reason){if(!condition)throw new Exception(reason);}
    static FarmObservation Healer(){return new FarmObservation{Fresh=true,KnownCore=true,MapId=42,MapMode=2,Zone="TEST",X=.5,Y=.5,Facing=0,PlayerGhost=true,TargetExists=true,TargetIdentityKnown=true,TargetToken=12,NameHashKnown=true,TargetNameHash=TargetCatalog.HashName("灵魂医者"),TargetDeadKnown=true,AttackabilityKnown=true,TargetPositionKnown=true,TargetX=.5,TargetY=.49};}
    static BattleProfile Profile(){return new BattleProfile{Calibration=new NavigationCalibration{MapId=42,MapMode=2,Zone="TEST",FacingSign=1}};}
    static bool Run(FarmObservation o,Action<ushort,int> pulse,Action<int> wait=null,int maximum=2,Func<FarmObservation> read=null)
    {return new GrindHealerApproach(read??(()=>o),pulse,wait??(ms=>{})).Run(Profile(),o.TargetToken,o.TargetX,o.TargetY,.001,maximum);}
    static int Main()
    {
        Test("arrival requires no movement or turn input",delegate{var o=Healer();o.Y=o.TargetY;int pulses=0;Assert(Run(o,(key,ms)=>pulses++),"already at healer must succeed");Assert(pulses==0,"arrival emitted movement");});
        Test("exhausted approach returns false instead of authorizing interaction",delegate{var o=Healer();int pulses=0;Assert(!Run(o,(key,ms)=>pulses++,null,3),"unreached healer was treated as reached");Assert(pulses==3,"pulse bound was not respected");});
        Test("unit swap after movement prevents all subsequent movement",delegate{var o=Healer();int pulses=0;Assert(!Run(o,(key,ms)=>{pulses++;o.TargetToken++;}),"swapped healer authorized approach");Assert(pulses==1,"movement continued after unit swap");});
        Test("healer coordinate mutation after movement stops the approach",delegate{var o=Healer();int pulses=0;Assert(!Run(o,(key,ms)=>{pulses++;o.TargetY-=.002;}),"changed healer position was accepted");Assert(pulses==1,"continued towards stale coordinates");});
        Test("waiting callback target loss prevents the next pulse",delegate{var o=Healer();int pulses=0;Assert(!Run(o,(key,ms)=>pulses++,ms=>o.TargetExists=false),"lost healer authorized approach");Assert(pulses==1,"movement continued after target loss");});
        Test("final allowed pulse may prove actual arrival",delegate{var o=Healer();int pulses=0;Assert(Run(o,(key,ms)=>{pulses++;o.Y=o.TargetY;},null,1),"arrival on last pulse was missed");Assert(pulses==1,"arrival did not respect pulse bound");});
        Test("turn callbacks cannot conceal target knowledge loss before forward input",delegate{var o=Healer();o.TargetX=.51;o.TargetY=.5;int forward=0,turn=0;Assert(!Run(o,(key,ms)=>{if(key==Profile().Movement.Forward)forward++;else turn++;o.TargetPositionKnown=false;}),"secret target position was accepted");Assert(turn==1&&forward==0,"forward input used an unverified target position");});
        Test("freshness map death and player guards all block before first pulse",delegate{foreach(Action<FarmObservation> mutate in new Action<FarmObservation>[] {o=>o.Fresh=false,o=>o.ReadFailures=1,o=>o.MapId++,o=>o.PlayerGhost=false,o=>o.TargetIsPlayer=true,o=>o.TargetDead=true,o=>o.TargetDeadKnown=false,o=>o.X=Double.NaN}){var o=Healer();mutate(o);int pulses=0;Assert(!Run(o,(key,ms)=>pulses++),"invalid healer observation was accepted");Assert(pulses==0,"unsafe observation emitted input");}});
        Test("a final pre-pulse read catches a changed unit before movement",delegate{var o=Healer();int reads=0,pulses=0;var token=o.TargetToken;var approach=new GrindHealerApproach(()=>{reads++;if(reads==2)o.TargetToken++;return o;},(key,ms)=>pulses++,ms=>{});Assert(!approach.Run(Profile(),token,o.TargetX,o.TargetY,.001,3),"pre-pulse identity change was ignored");Assert(pulses==0,"stale plan emitted movement");});
        Test("each approach pulse stays within the guarded input bound",delegate{var o=Healer();int pulses=0;Run(o,(key,ms)=>{Assert(ms>=60&&ms<=250,"unbounded approach pulse");pulses++;});Assert(pulses>0,"no movement was planned");});
        Console.WriteLine("Grind healer approach tests: "+passed+" passed, "+failed+" failed. Pure fake input only.");return failed==0?0:1;
    }
}
