using System;
using System.Reflection;
using WoWProbe;

class GrindCliIntegrationTests
{
    sealed class Backend:IInputBackend
    {public bool Focus=true,Emergency;public int Down,Up;public readonly System.Collections.Generic.List<ushort> Keys=new System.Collections.Generic.List<ushort>();public Action<ushort> Released;public bool IsTargetForeground(){return Focus;}public bool IsEmergencyPressed(){return Emergency;}public bool IsKeyAlreadyDown(ushort k){return false;}public void KeyDown(ushort k){Down++;Keys.Add(k);}public void KeyUp(ushort k){Up++;if(Released!=null)Released(k);}}
    static int passed,failed;
    static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Static;
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}finally{typeof(GrindCli).GetField("input",Flags).SetValue(null,null);typeof(GrindCli).GetField("chatReader",Flags).SetValue(null,null);}}
    static void Assert(bool value){if(!value)throw new Exception("behavior differs");}
    static void Call(string name,params object[] args){try{var method=typeof(GrindCli).GetMethod(name,Flags);var values=new object[method.GetParameters().Length];for(int i=0;i<values.Length;i++)values[i]=i<args.Length?args[i]:Type.Missing;method.Invoke(null,values);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static void Reject(Action action){bool stopped=false;try{action();}catch(InvalidOperationException){stopped=true;}Assert(stopped);}
    static void Arm(Backend backend,Action<int> wait)
    {long now=0;var o=new FarmObservation{Fresh=true,KnownCore=true,MapId=42,MapMode=2,Zone="TEST"};var g=new GrindInputGuard(backend,()=>o,()=>now,ms=>{now+=ms;if(wait!=null)wait(ms);},new InputFailureLatch(),42,2,"TEST");typeof(GrindCli).GetField("input",Flags).SetValue(null,g);}
    static int Main()
    {
        Test("actual pet keyboard dispatch sends only E Q eight and never a movement tap",delegate{
            long now=0;var o=new FarmObservation{Fresh=true,KnownCore=true,MapId=42,MapMode=2,Zone="TEST",Health=100,CastingKnown=true,ChatOpenKnown=true,X=.3,Y=.4};
            var state=new QuickHunterState(()=>now);state.Pet.Configure(true,"Q");state.Pet.PlayerDied();var engine=new QuickCombat(o,750,false,null,"4","4",80,0,false,null,true,false,state);var b=new Backend();
            Func<FarmObservation> read=()=>o;QuickCommand c=null;
            Action arm=()=>{var g=new GrindInputGuard(b,()=>{if(!QuickCombat.Matches(c,o))throw new InvalidOperationException("目标状态变化，停止当前按键。");return o;},()=>now,ms=>{now+=ms;o.Sequence++;},new InputFailureLatch(),42,2,"TEST");typeof(GrindCli).GetField("input",Flags).SetValue(null,g);};
            c=engine.Step(o,now);arm();Call("PetKeyChecked",b,c,engine,state.Pet,read);Assert(now>=1000&&b.Keys.Count==1&&b.Keys[0]==69);
            now+=1600;o.Sequence++;c=engine.Step(o,now);Assert(c.Key==81);arm();Call("PetKeyChecked",b,c,engine,state.Pet,read);Assert(b.Keys.Count==2&&b.Keys[1]==81&&state.Pet.InputLocked);
            long qSent=now-80;while(now<qSent+11000){now=Math.Min(qSent+11000,now+100);o.Sequence++;c=engine.Step(o,now);if(now<qSent+11000){Assert(c.Action==QuickAction.None);Call("CancelQuickMovement",b,state.Pet);Assert(b.Keys.Count==2);}}
            Assert(c.Key==56&&!state.Pet.InputLocked);arm();Call("PetKeyChecked",b,c,engine,state.Pet,read);Assert(b.Keys.Count==3&&b.Keys[2]==56&&b.Down==b.Up);
        });
        Test("pet key preparation stops only observed movement and requires fresh stable coordinates",delegate{
            var o=new FarmObservation{Fresh=true,KnownCore=true,Health=100,CastingKnown=true,ChatOpenKnown=true,X=.3,Y=.4};int elapsed=0,stops=0;bool moving=true;
            QuickPetInput.Prepare(()=>o,ms=>{elapsed+=ms;o.Sequence++;if(moving)o.X+=.0001;},()=>{stops++;moving=false;});Assert(stops==1&&elapsed>=1100);
            o.Casting=true;Reject(()=>QuickPetInput.Prepare(()=>o,ms=>{},()=>stops++));Assert(stops==1);o.Casting=false;
            Reject(()=>QuickPetInput.Prepare(()=>o,ms=>{},()=>stops++));Assert(stops==1);
        });
        Test("movement cleanup sends no key during pet revival eleven second input lock",delegate{
            long now=0;var pet=new QuickPetRecovery(()=>now);pet.Configure(true,"Q");pet.Observe(new FarmObservation{PetStateKnown=true,PetDead=true});
            pet.InputCompleted(new QuickCommand{Action=QuickAction.HunterRevivePet},0);var b=new Backend();
            Call("CancelQuickMovement",b,pet);Assert(b.Down==0&&b.Up==0);now=10999;Call("CancelQuickMovement",b,pet);Assert(b.Down==0&&b.Up==0&&pet.InputLocked);
            now=11000;Assert(pet.InputLocked);Call("CancelQuickMovement",b,pet);Assert(b.Down==0);
        });
        Test("pet revive chat submits exact Chinese command and allows its own cast only after Enter",delegate{
            var o=new FarmObservation{Fresh=true,KnownCore=true,MapId=42,MapMode=2,Zone="TEST",Health=100,CastingKnown=true,ChatOpenKnown=true,PetStateKnown=true,PetDead=true};
            var command=new QuickCommand{Action=QuickAction.HunterRevivePet,UsePetReviveChat=true};var b=new Backend();long now=0;var typed=new System.Text.StringBuilder();
            b.Released=k=>{if(k==13){o.ChatOpen=!o.ChatOpen;if(!o.ChatOpen)o.Casting=true;}};
            var input=new GrindInputGuard(b,()=>{if(!QuickCombat.MatchesChatCommand(command,o))throw new InvalidOperationException("目标状态变化，停止当前按键。");return o;},()=>now,ms=>now+=ms,new InputFailureLatch(),42,2,"TEST");
            typeof(GrindCli).GetField("input",Flags).SetValue(null,input);typeof(GrindCli).GetField("chatReader",Flags).SetValue(null,(Func<FarmObservation>)(()=>o));
            Call("ChatChecked",null,"/cast 复活宠物",(Func<bool>)(()=>QuickCombat.MatchesChatCommand(command,o)),(Action<char>)(c=>typed.Append(c)),(Action)(()=>command.InputStarted=true));
            Assert(typed.ToString()=="/cast 复活宠物"&&!o.ChatOpen&&o.Casting&&command.InputStarted&&b.Down==2&&b.Up==2);
            Assert(!QuickCombat.MatchesChatCommand(new QuickCommand{Action=QuickAction.HunterSummonPet},o));
        });
        Test("CLI calibration helper requires an armed shared guard",delegate{Reject(()=>Call("Pulse",null,(ushort)87,100));});
        Test("CLI pulse uses the guard and releases after emergency",delegate{var b=new Backend();Arm(b,ms=>b.Emergency=true);Reject(()=>Call("Pulse",null,(ushort)87,250));b.Emergency=false;Reject(()=>Call("Pulse",null,(ushort)87,100));Assert(b.Down==1&&b.Up==1);});
        Test("CLI chat aborts and releases Enter before any character on lost focus",delegate{var b=new Backend();Arm(b,ms=>b.Focus=false);Reject(()=>Call("Chat",null,"this must never be typed"));Assert(b.Down==1&&b.Up==1);});
        Test("CLI pause observes emergency instead of sleeping through it",delegate{var b=new Backend();Arm(b,ms=>b.Emergency=true);Reject(()=>Call("Pause",1500));Assert(b.Down==0&&b.Up==0);});
        Test("invalid CLI cycle count fails before window lookup or input",delegate{Assert(GrindCli.Run(new[]{"grind","unused-route","unused-profile","0"})==2);Assert(GrindCli.Run(new[]{"grind","unused-route","unused-profile","bad"})==2);});
        Test("real chat helper completes corpse reselect across its own open chat state",delegate{
            foreach(bool owned in new[]{false,true}){
                var o=new FarmObservation{Fresh=true,KnownCore=true,MapId=42,MapMode=2,Zone="TEST",Health=100,CastingKnown=true,ChatOpenKnown=true,AttackabilityKnown=true,TargetDeadKnown=true,LootEventsKnown=true,SkinKnown=true,Skinnable=true,SkinBindingKnown=true,SkinBindingCorrect=true,SkinSuccessKnown=true,CastEventsKnown=true};
                var command=new QuickCommand{Action=QuickAction.CorpseSelect,Token=7};var b=new Backend();long now=0;var typed=new System.Text.StringBuilder();
                b.Released=k=>{if(k==13){o.ChatOpen=!o.ChatOpen;if(!o.ChatOpen){o.TargetExists=o.TargetIdentityKnown=o.TargetDead=true;o.TargetToken=7;}}};
                var input=new GrindInputGuard(b,()=>{if(!(owned?QuickCombat.MatchesChatCommand(command,o):QuickCombat.Matches(command,o)))throw new InvalidOperationException("selection context changed");return o;},()=>now,ms=>now+=ms,new InputFailureLatch(),42,2,"TEST");
                typeof(GrindCli).GetField("input",Flags).SetValue(null,input);typeof(GrindCli).GetField("chatReader",Flags).SetValue(null,(Func<FarmObservation>)(()=>o));
                if(!owned){Reject(()=>Call("ChatChecked",null,"/targetlasttarget",null,(Action<char>)(c=>typed.Append(c))));Assert(typed.Length==0&&b.Down==1&&b.Up==1);}
                else{Call("ChatChecked",null,"/targetlasttarget",null,(Action<char>)(c=>typed.Append(c)));Assert(typed.ToString()=="/targetlasttarget"&&!o.ChatOpen&&o.TargetToken==7&&b.Down==2&&b.Up==2);var corpse=new QuickCorpse(7,true,o,now);Assert(corpse.Step(o,now).Action==QuickAction.None);Assert(corpse.Step(o,now+500).Action==QuickAction.Loot);o.LootOpened=o.LootClosed=1;Assert(corpse.Step(o,now+1200).Action==QuickAction.Skin);o.SkinSuccess=1;corpse.Step(o,now+2800);Assert(corpse.Done&&corpse.Loots==1&&corpse.Skins==1);}
            }
        });
        Test("owned chat exemption never authorizes combat keys or a changed reacquire target",delegate{var o=new FarmObservation{Fresh=true,KnownCore=true,ChatOpenKnown=true,ChatOpen=true};Assert(!QuickCombat.MatchesChatCommand(new QuickCommand{Action=QuickAction.Skill},o));o.TargetExists=o.TargetIdentityKnown=true;o.TargetToken=8;Assert(!QuickCombat.MatchesChatCommand(new QuickCommand{Action=QuickAction.Reacquire,Token=7},o));o.Fresh=false;Assert(!QuickCombat.MatchesChatCommand(new QuickCommand{Action=QuickAction.CorpseSelect,Token=7},o));});
        Test("combat arriving after Enter closes interrupted return chat through a fresh safety guard",delegate{
            var o=new FarmObservation{Fresh=true,KnownCore=true,MapId=42,MapMode=2,Zone="TEST",Health=100,ChatOpenKnown=true};var b=new Backend();long now=0;var text=new System.Text.StringBuilder();
            b.Released=k=>{if(k==13){o.ChatOpen=true;o.InCombat=true;}if(k==27)o.ChatOpen=false;};
            Func<GrindInputGuard> baseGuard=()=>new GrindInputGuard(b,()=>o,()=>now,ms=>now+=ms,new InputFailureLatch(),42,2,"TEST");
            var targetGuard=new GrindInputGuard(b,()=>{if(o.InCombat)throw new InvalidOperationException("目标状态变化，停止当前按键。");return o;},()=>now,ms=>now+=ms,new InputFailureLatch(),42,2,"TEST");
            typeof(GrindCli).GetField("input",Flags).SetValue(null,targetGuard);typeof(GrindCli).GetField("chatReader",Flags).SetValue(null,(Func<FarmObservation>)(()=>o));
            Reject(()=>Call("ChatChecked",null,"/stopattack",(Func<bool>)(()=>!o.InCombat),(Action<char>)(c=>text.Append(c))));Assert(o.ChatOpen&&text.Length==0);
            Call("CloseInterruptedChat",null,baseGuard());Assert(!o.ChatOpen&&o.InCombat&&b.Down==2&&b.Up==2);
            o.ChatOpen=true;Call("CloseInterruptedChat",null,baseGuard());Assert(b.Down==2&&b.Up==2);b.Emergency=true;Reject(()=>Call("CloseInterruptedChat",null,baseGuard()));Assert(o.ChatOpen&&b.Down==2);
        });
        Console.WriteLine("Grind CLI integration tests: "+passed+" passed, "+failed+" failed. No native input backend was constructed.");return failed==0?0:1;
    }
}
