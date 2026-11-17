using System;
using System.Collections.Generic;
using WoWProbe;
class MouseLookTests
{
    static int Main()
    {
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
        try{MouseLookInput.Run(161,0,()=>{throw new Exception("Input started before validation.");},()=>false,()=>{},()=>{},(a,b)=>{},ms=>{});throw new Exception("Expected bounds rejection.");}catch(ArgumentOutOfRangeException){}
        Console.WriteLine("Mouse look tests: relative displacement, owned-button cleanup on 3 failures, held-user-button rejection and bounds passed. No native input.");return 0;
    }
}
