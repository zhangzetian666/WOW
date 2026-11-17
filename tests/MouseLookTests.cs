using System;
using System.Collections.Generic;
using WoWProbe;
class MouseLookTests
{
    static int Main()
    {
        if(!MouseLookInput.TurnObserved(6.26,.05)||MouseLookInput.TurnObserved(1,1)||MouseLookInput.TurnObserved(1,1+2*Math.PI)||MouseLookInput.TurnObserved(Double.NaN,1)||MouseLookInput.TurnObserved(1,Double.PositiveInfinity))throw new Exception("Wrapped or unknown facing was misclassified.");
        var events=new List<string>();int x=0,y=0;
        MouseLookInput.Run(50,-18,()=>{},()=>false,()=>events.Add("down"),()=>events.Add("up"),(a,b)=>{x+=a;y+=b;},ms=>{});
        if(x!=50||y!=-18||String.Join(",",events.ToArray())!="down,up")throw new Exception("Movement or button ownership differs.");
        for(int failure=0;failure<3;failure++)
        {
            events.Clear();int checks=0;int fail=failure;
            try{MouseLookInput.Run(32,0,()=>{if(fail==0&&++checks==3)throw new InvalidOperationException();},()=>false,()=>{events.Add("down");if(fail==1)throw new InvalidOperationException();},()=>events.Add("up"),(a,b)=>{if(fail==2)throw new InvalidOperationException();},ms=>{});throw new Exception("Expected failure.");}catch(InvalidOperationException){}
            if(String.Join(",",events.ToArray())!="down,up")throw new Exception("Owned button leaked on failure.");
        }
        events.Clear();try{MouseLookInput.Run(20,0,()=>{},()=>true,()=>events.Add("down"),()=>events.Add("up"),(a,b)=>{},ms=>{});throw new Exception("Expected held-button rejection.");}catch(InvalidOperationException){}
        if(events.Count!=0)throw new Exception("User's button changed.");
        double facing=1;int keys=0;
        if(!MouseLookInput.TurnWithFallback(()=>facing,()=>facing+=.4,()=>keys++)||keys!=0)throw new Exception("Working mouse turn unexpectedly used the keyboard.");
        if(!MouseLookInput.TurnWithFallback(()=>facing,()=>{},()=>{keys++;facing+=.4;})||keys!=1)throw new Exception("Ignored mouse turn did not use verified keyboard fallback.");
        if(MouseLookInput.TurnWithFallback(()=>facing,()=>{},()=>keys++)||keys!=2)throw new Exception("Two ignored controls were accepted as a turn.");
        try{MouseLookInput.TurnWithFallback(()=>facing,()=>{throw new InvalidOperationException();},()=>keys++);throw new Exception("Expected aborted mouse input.");}catch(InvalidOperationException){}
        if(keys!=2)throw new Exception("Failed mouse safety check triggered another input.");
        try{MouseLookInput.Run(161,0,()=>{throw new Exception("Input started before validation.");},()=>false,()=>{},()=>{},(a,b)=>{},ms=>{});throw new Exception("Expected bounds rejection.");}catch(ArgumentOutOfRangeException){}
        Console.WriteLine("Mouse look tests: relative displacement, owned-button cleanup, held-user-button rejection, bounds and observed keyboard fallback passed. No native input.");return 0;
    }
}
