using System;
using WoWProbe;
class QuickForwardMotionTests
{
    sealed class Backend:IInputBackend
    {
        public bool Focus=true,Emergency,UserHeld,FailDown,FailUp;public int Downs,Ups;
        public bool IsTargetForeground(){return Focus;}public bool IsEmergencyPressed(){return Emergency;}public bool IsKeyAlreadyDown(ushort k){return UserHeld;}
        public void KeyDown(ushort k){Downs++;if(FailDown)throw new InvalidOperationException("partial down");}
        public void KeyUp(ushort k){Ups++;if(FailUp)throw new InvalidOperationException("failed release");}
    }
    static FarmObservation O(){return new FarmObservation{Fresh=true,KnownCore=true,Health=100,ChatOpenKnown=true,CastingKnown=true};}
    static void Check(bool b){if(!b)throw new Exception("Continuous search movement differs.");}
    static int Main()
    {
        var b=new Backend();var latch=new InputFailureLatch();var m=new QuickForwardMotion(b,()=>{},latch);
        m.Start();for(int i=0;i<100;i++){m.Observe(O());m.Start();}Check(m.Held&&b.Downs==1&&b.Ups==0);m.Stop();m.Stop();Check(b.Ups==1&&!m.Held);
        for(int fail=0;fail<10;fail++)
        {
            b=new Backend();m=new QuickForwardMotion(b,()=>{},new InputFailureLatch());m.Start();var o=O();
            if(fail==0)o.InCombat=true;if(fail==1)o.PlayerDead=true;if(fail==2)o.ChatOpen=true;if(fail==3)o.Fresh=false;if(fail==4)o.Health=79;
            if(fail==5)b.Focus=false;if(fail==6)b.Emergency=true;if(fail==7)o.Casting=true;if(fail==8)o.BagFull=true;
            if(fail==9){o.TargetExists=o.TargetIdentityKnown=o.AttackabilityKnown=o.TargetCanAttack=o.TargetDeadKnown=true;o.TargetToken=3;o.TargetReaction=4;}
            m.Observe(o);Check(!m.Held&&b.Ups==1);
        }
        b=new Backend{UserHeld=true};m=new QuickForwardMotion(b,()=>{},new InputFailureLatch());try{m.Start();throw new Exception("User-held W was taken over.");}catch(InvalidOperationException){}Check(b.Downs==0&&b.Ups==0);
        b=new Backend{FailDown=true};latch=new InputFailureLatch();m=new QuickForwardMotion(b,()=>{},latch);try{m.Start();throw new Exception("Down failure was ignored.");}catch(InvalidOperationException){}Check(b.Downs==1&&b.Ups==1&&latch.Faulted&&!m.Held);
        b=new Backend{FailUp=true};latch=new InputFailureLatch();m=new QuickForwardMotion(b,()=>{},latch);m.Start();try{m.Stop();throw new Exception("Release failure was ignored.");}catch(InvalidOperationException){}Check(latch.Faulted&&b.Ups==1);
        Console.WriteLine("Continuous search motion: one owned down across 100 scan ticks, release on 10 stop conditions, user-held rejection and partial-input failures passed. No native input.");return 0;
    }
}
