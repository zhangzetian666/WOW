using System;
using System.Reflection;
using WoWProbe;

class GrindCliIntegrationTests
{
    sealed class Backend:IInputBackend
    {public bool Focus=true,Emergency;public int Down,Up;public bool IsTargetForeground(){return Focus;}public bool IsEmergencyPressed(){return Emergency;}public bool IsKeyAlreadyDown(ushort k){return false;}public void KeyDown(ushort k){Down++;}public void KeyUp(ushort k){Up++;}}
    static int passed,failed;
    static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Static;
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}finally{typeof(GrindCli).GetField("input",Flags).SetValue(null,null);}}
    static void Assert(bool value){if(!value)throw new Exception("behavior differs");}
    static void Call(string name,params object[] args){try{typeof(GrindCli).GetMethod(name,Flags).Invoke(null,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static void Reject(Action action){bool stopped=false;try{action();}catch(InvalidOperationException){stopped=true;}Assert(stopped);}
    static void Arm(Backend backend,Action<int> wait)
    {long now=0;var o=new FarmObservation{Fresh=true,KnownCore=true,MapId=42,MapMode=2,Zone="TEST"};var g=new GrindInputGuard(backend,()=>o,()=>now,ms=>{now+=ms;if(wait!=null)wait(ms);},new InputFailureLatch(),42,2,"TEST");typeof(GrindCli).GetField("input",Flags).SetValue(null,g);}
    static int Main()
    {
        Test("CLI calibration helper requires an armed shared guard",delegate{Reject(()=>Call("Pulse",null,(ushort)87,100));});
        Test("CLI pulse uses the guard and releases after emergency",delegate{var b=new Backend();Arm(b,ms=>b.Emergency=true);Reject(()=>Call("Pulse",null,(ushort)87,250));b.Emergency=false;Reject(()=>Call("Pulse",null,(ushort)87,100));Assert(b.Down==1&&b.Up==1);});
        Test("CLI chat aborts and releases Enter before any character on lost focus",delegate{var b=new Backend();Arm(b,ms=>b.Focus=false);Reject(()=>Call("Chat",null,"this must never be typed"));Assert(b.Down==1&&b.Up==1);});
        Test("CLI pause observes emergency instead of sleeping through it",delegate{var b=new Backend();Arm(b,ms=>b.Emergency=true);Reject(()=>Call("Pause",1500));Assert(b.Down==0&&b.Up==0);});
        Test("invalid CLI cycle count fails before window lookup or input",delegate{Assert(GrindCli.Run(new[]{"grind","unused-route","unused-profile","0"})==2);Assert(GrindCli.Run(new[]{"grind","unused-route","unused-profile","bad"})==2);});
        Console.WriteLine("Grind CLI integration tests: "+passed+" passed, "+failed+" failed. No native input backend was constructed.");return failed==0?0:1;
    }
}
