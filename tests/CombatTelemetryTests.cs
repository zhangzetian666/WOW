using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using WoWProbe;

class CombatTelemetryTests
{
    static int passed, failed;
    static Type Packet { get { return typeof(ProbePacket).Assembly.GetType("WoWProbe.CombatPacket"); } }
    static Type Observation { get { return typeof(ProbePacket).Assembly.GetType("WoWProbe.FarmObservation"); } }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Equal(object a, object b, string message) { Assert(Object.Equals(a,b),message+": "+a+" != "+b); }
    static object Get(object p,string name) { var f=p.GetType().GetField(name);return f!=null?f.GetValue(p):p.GetType().GetProperty(name).GetValue(p,null); }
    static object Decode(byte[] b) { Assert(Packet!=null,"WC3 CombatPacket is missing");return Packet.GetMethod("Decode").Invoke(null,new object[]{b}); }
    static object Observe(ProbePacket core,object extra,bool fresh) { Assert(Observation!=null,"FarmObservation is missing");return Observation.GetMethod("FromPackets").Invoke(null,new object[]{core,extra,fresh}); }
    static void Test(string name,Action action) { try { action();passed++;Console.WriteLine("PASS "+name); } catch(Exception e) { failed++;Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message); } }
    static void Checksum(byte[] b) { int a=0,s=0;for(int i=0;i<126;i++){a=(a+b[i])%255;s=(s+a)%255;}b[126]=(byte)a;b[127]=(byte)s; }
    static void U16(byte[] b,int i,int n) { b[i]=(byte)n;b[i+1]=(byte)(n>>8); }
    static void U32(byte[] b,int i,uint n) { for(int j=0;j<4;j++)b[i+j]=(byte)(n>>(8*j)); }
    static byte[] Extra() { var b=new byte[128];b[0]=87;b[1]=67;b[2]=51;b[3]=1;U16(b,4,3);U16(b,12,65535);U16(b,14,65535);b[16]=1;Checksum(b);return b; }
    static byte[] Core() { var b=new byte[128];b[0]=87;b[1]=80;b[2]=49;b[3]=1;U16(b,4,3);b[6]=8;b[7]=95;b[8]=50;b[9]=30;b[10]=25;b[11]=0;U16(b,12,20000);U16(b,14,30000);U16(b,16,10000);U16(b,18,2);U32(b,20,42);U16(b,24,65535);U16(b,26,65535);b[122]=1;b[123]=255;b[125]=255;Checksum(b);return b; }
    static void Reject(Action<byte[]> change) { var b=Extra();change(b);Checksum(b);try{Decode(b);}catch(TargetInvocationException e){if(e.InnerException is FormatException)return;throw;}throw new Exception("Invalid WC3 packet accepted"); }
    static int Main(string[] args)
    {
        Test("WC3 v3 reports target scoped range feedback without changing hover flags",delegate{
            var b=Extra();b[3]=3;b[6]=1;U32(b,8,123);b[125]=4;Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);
            Equal(Get(o,"RangeErrorSupported"),true,"v3 capability");Equal(Get(o,"RangeErrorActive"),true,"too far feedback");Equal(Get(o,"TargetHoverKnown"),false,"independent hover");
            Reject(v=>{v[3]=3;v[125]=4;});Reject(v=>{v[3]=2;v[6]=1;U32(v,8,123);v[125]=4;});
        });
        Test("WC3 v2 adds independent five and six ranges while v1 remains readable",delegate{
            var b=Extra();b[3]=2;b[123]=48;b[124]=32;Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);
            Equal(Get(o,"ExtendedActionRanges"),true,"v2 capability");Equal(Get(o,"ActionRangeKnownMask"),48,"both readable");Equal(Get(o,"ActionsInRangeMask"),32,"six only in range");Equal(Get(o,"RangedActionsMask"),0,"range extension is not action type");
            o=Observe(ProbePacket.Decode(Core()),Decode(Extra()),true);Equal(Get(o,"ExtendedActionRanges"),false,"v1 supported");
            Reject(v=>{v[3]=2;v[124]=16;});Reject(v=>{v[3]=2;v[123]=64;});
        });
        Test("target hover needs identity and does not invent geometry",delegate{var b=Extra();b[6]=1;U32(b,8,123);b[125]=3;Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"TargetHoverKnown"),true,"hover known");Equal(Get(o,"TargetHovered"),true,"same target");Equal(Get(o,"PortraitKnown"),false,"no rectangle invented");b[125]=1;Checksum(b);Equal(Get(Decode(b),"TargetHovered"),false,"known mismatch");Reject(x=>x[125]=1);Reject(x=>x[125]=2);});
        Test("per-action range and repeat state decode independently and reject contradictory masks",delegate{var b=Extra();b[121]=0x46;b[122]=0x4f;b[123]=4;b[124]=3;Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"ActionRangeKnownMask"),6,"range known");Equal(Get(o,"ActionsInRangeMask"),4,"in range");Equal(Get(o,"RepeatingMask"),4,"active auto shot");Equal(Get(o,"RangedActionsMask"),4,"ranged type");Equal(Get(o,"MeleeActionsMask"),3,"melee type");Reject(x=>x[121]=0x40);Reject(x=>x[122]=0x40);Reject(x=>{x[123]=4;x[124]=4;});Reject(x=>x[123]=16);Reject(x=>x[125]=1);});
        Test("click to move distinguishes unknown disabled and enabled without a target",delegate{foreach(int flags in new[]{0,1,3}){var b=Extra();b[120]=(byte)flags;Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"ClickToMoveKnown"),flags!=0,"known");Equal(Get(o,"ClickToMoveEnabled"),flags==3,"enabled");}Reject(x=>x[120]=2);Reject(x=>x[120]=4);});
        Test("interaction binding distinguishes legacy unknown wrong and correct current character",delegate{foreach(int flags in new[]{0,1,3}){var b=Extra();b[119]=(byte)flags;Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"InteractBindingKnown"),flags!=0,"known");Equal(Get(o,"InteractBindingCorrect"),flags==3,"F interact");}Reject(x=>x[119]=2);Reject(x=>x[119]=4);});
        Test("stealth flags distinguish unknown visible and stealthed without changing legacy packets",delegate{foreach(int flags in new[]{0,1,3}){var b=Extra();b[118]=(byte)flags;Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"StealthKnown"),flags!=0,"known");Equal(Get(o,"Stealthed"),flags==3,"stealthed");}Reject(x=>x[118]=2);});
        Test("melee evidence decodes and rejects unknown or inconsistent counters",()=>{var b=Extra();b[109]=1;U32(b,110,123);U32(b,114,7);Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"MeleeHitToken"),(uint)123,"hit identity");Equal(Get(o,"MeleeHits"),(uint)7,"hit count");Reject(v=>U32(v,110,123));Reject(v=>{v[109]=1;U32(v,114,1);});});
        Test("unknown capabilities retain NaN and false-known distinctions",delegate {
            object p=Decode(Extra());Equal(Get(p,"Capabilities"),0,"caps");Assert(Double.IsNaN((double)Get(p,"TargetX")),"unknown coordinate");
            var o=Observe(ProbePacket.Decode(Core()),p,true);Equal(Get(o,"Fresh"),true,"fresh pair");Equal(Get(o,"KnownCore"),true,"known core");Equal(Get(o,"AttackabilityKnown"),false,"unknown attackability");
            var b=Extra();b[6]=2;Checksum(b);o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"AttackabilityKnown"),true,"known false");Equal(Get(o,"TargetCanAttack"),false,"false value");
        });
        Test("target reaction is preserved from matched core telemetry",delegate {var b=Core();b[123]=4;Checksum(b);var o=Observe(ProbePacket.Decode(b),Decode(Extra()),true);Equal(Get(o,"TargetReaction"),4,"neutral target");b[123]=255;Checksum(b);o=Observe(ProbePacket.Decode(b),Decode(Extra()),true);Equal(Get(o,"TargetReaction"),255,"unknown reaction");});
        Test("sequence mismatch and absent packets cannot be fresh",delegate {
            var b=Extra();U16(b,4,4);Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"Fresh"),false,"mismatched sequence");Equal(Get(o,"AttackabilityKnown"),false,"mismatched extension invalid");
            Equal(Get(Observe(ProbePacket.Decode(Core()),null,true),"Fresh"),false,"absent extension");Equal(Get(Observe(null,Decode(Extra()),true),"Fresh"),false,"absent core");
            Equal(Get(Observe(ProbePacket.Decode(Core()),Decode(Extra()),false),"Fresh"),false,"stale source");
        });
        Test("fresh observation also requires known player position facing and map",delegate {
            foreach(int missing in new[]{1,2,64}) {
                var b=Core();b[7]=(byte)(b[7]&~missing);
                if(missing==1){U16(b,12,65535);U16(b,14,65535);}
                if(missing==2)U16(b,16,65535);
                if(missing==64){U32(b,20,0);b[122]=0;}
                Checksum(b);var o=Observe(ProbePacket.Decode(b),Decode(Extra()),true);
                Equal(Get(o,"Fresh"),false,"missing core capability "+missing);
            }
        });
        Test("unknown bag knowledge blocks freshness and zero free slots marks full",delegate {
            var b=Core();b[7]=(byte)(b[7]&~16);U16(b,18,65535);Checksum(b);
            var o=Observe(ProbePacket.Decode(b),Decode(Extra()),true);Equal(Get(o,"KnownCore"),true,"core boolean contract stays15");Equal(Get(o,"Fresh"),false,"missing bag capability");
            b=Core();b[125]=(byte)(b[125]&~128);Checksum(b);o=Observe(ProbePacket.Decode(b),Decode(Extra()),true);Equal(Get(o,"Fresh"),false,"unknown full-bag flag");
            b=Core();U16(b,18,0);Checksum(b);o=Observe(ProbePacket.Decode(b),Decode(Extra()),true);Equal(Get(o,"BagFull"),true,"known zero free slots");
            b=Core();b[6]|=128;Checksum(b);o=Observe(ProbePacket.Decode(b),Decode(Extra()),true);Equal(Get(o,"BagFull"),true,"explicit observed full flag");
        });
        Test("portrait references reject excessive capture sizes",delegate {
            Reject(delegate(byte[] b){b[56]=1;U16(b,52,513);U16(b,54,64);});
            Reject(delegate(byte[] b){b[56]=1;U16(b,52,64);U16(b,54,513);});
        });
        Test("core knowledge requires death ghost combat and target-exists validity",delegate {
            foreach(int mask in new[]{1,2,4,8}) { var b=Core();b[125]=(byte)(255-mask);b[6]=(byte)(b[6]&b[125]);Checksum(b);Equal(Get(Observe(ProbePacket.Decode(b),Decode(Extra()),true),"KnownCore"),false,"known core bit "+mask); }
            var absent=Core();absent[125]=7;absent[6]=0;Checksum(absent);var o=Observe(ProbePacket.Decode(absent),Decode(Extra()),true);Equal(Get(o,"TargetExists"),false,"unknown exists must stay false");
        });
        Test("decoder rejects flags without known capabilities and inconsistent masks",delegate {
            foreach(int f in new[]{1,2,4,8,16,32,64,128})Reject(delegate(byte[] b){b[7]=(byte)f;});
            Reject(delegate(byte[] b){b[17]=1;});Reject(delegate(byte[] b){b[6]=128;});Reject(delegate(byte[] b){b[6]=128;b[17]=1;b[18]=2;});Reject(delegate(byte[] b){b[6]=128;b[17]=1;b[19]=2;});
            Reject(delegate(byte[] b){b[17]=16;});Reject(delegate(byte[] b){b[16]=0;});Reject(delegate(byte[] b){b[16]=5;});
        });
        Test("decoder rejects partial coordinates unknown fields and malformed evidence",delegate {
            Reject(delegate(byte[] b){U16(b,12,1);});Reject(delegate(byte[] b){b[6]=8;});Reject(delegate(byte[] b){b[6]=1;});Reject(delegate(byte[] b){U32(b,8,123);});
            Reject(delegate(byte[] b){U32(b,44,123);});Reject(delegate(byte[] b){b[56]=4;});Reject(delegate(byte[] b){U32(b,20,1000);});
            Reject(delegate(byte[] b){b[6]=64;b[7]=96;U32(b,20,1000);});Reject(delegate(byte[] b){b[6]=64;b[7]=32;});
            foreach(int offset in new[]{24,28,32,36,40})Reject(delegate(byte[] b){U32(b,offset,1);});
            Reject(delegate(byte[] b){b[56]=64;});Reject(delegate(byte[] b){b[57]=1;});Reject(delegate(byte[] b){b[125]=1;});
            Reject(delegate(byte[] b){b[56]=1;U16(b,52,1);});Reject(delegate(byte[] b){U16(b,52,1);U16(b,54,1);});
            var corrupt=Extra();corrupt[1]++;try{Decode(corrupt);throw new Exception("bad checksum accepted");}catch(TargetInvocationException e){Assert(e.InnerException is FormatException,"wrong exception");}
        });
        Test("position observations require a modern known same player map",delegate {
            var b=Extra();b[6]=8;U16(b,12,10000);U16(b,14,20000);Checksum(b);var x=Decode(b);
            var c=Core();c[6]=8;Checksum(c);var o=Observe(ProbePacket.Decode(c),x,true);Equal(Get(o,"TargetPositionKnown"),true,"modern target position");
            c[122]=2;Checksum(c);o=Observe(ProbePacket.Decode(c),x,true);Equal(Get(o,"TargetPositionKnown"),false,"legacy map cannot interpret target map");Assert(Double.IsNaN((double)Get(o,"TargetX")),"unknown target coordinate sentinel");
        });
        Test("target absence prevents extension target claims",delegate {
            var b=Extra();b[6]=7;b[7]=5;U32(b,8,1);b[56]=4;U32(b,44,5);Checksum(b);
            var c=Core();c[6]=0;Checksum(c);var o=Observe(ProbePacket.Decode(c),Decode(b),true);Equal(Get(o,"TargetExists"),false,"absent target");Equal(Get(o,"TargetIdentityKnown"),false,"absent identity");Equal(Get(o,"RangeKnown"),false,"absent range");
        });
        Test("optional target-death evidence survives current target absence",delegate {
            var b=Extra();b[56]=32;U32(b,57,123);U32(b,61,7);Checksum(b);
            var c=Core();c[6]=0;Checksum(c);var o=Observe(ProbePacket.Decode(c),Decode(b),true);
            Equal(Get(o,"DeathEventsKnown"),true,"death source");Equal(Get(o,"DeathToken"),123u,"latched token");Equal(Get(o,"TargetDeaths"),7u,"death revision");
            Reject(delegate(byte[] x){x[56]=32;U32(x,57,123);});Reject(delegate(byte[] x){x[56]=32;U32(x,61,7);});
        });
        Test("optional chat XP skin binding and empty food remain explicit and canonical",delegate {
            var b=Extra();b[65]=3;b[74]=1;U32(b,66,123);U32(b,70,4);b[75]=3;b[76]=1;Checksum(b);
            var c=Core();c[6]=0;Checksum(c);var o=Observe(ProbePacket.Decode(c),Decode(b),true);
            Equal(Get(o,"ChatOpen"),true,"focused chat");Equal(Get(o,"KillCredits"),4u,"XP credit");Equal(Get(o,"SkinBindingCorrect"),true,"current skin action");Equal(Get(o,"FoodCount"),0,"known empty food");
            Reject(delegate(byte[] x){x[65]=2;});Reject(delegate(byte[] x){x[75]=2;});Reject(delegate(byte[] x){x[76]=1;U16(x,77,65535);});Reject(delegate(byte[] x){x[74]=1;U32(x,70,1);});
        });
        Test("optional ownership combat and victim flags distinguish free targets from contested ones",delegate {var b=Extra();b[79]=31;Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"TargetClaimKnown"),true,"claim known");Equal(Get(o,"TargetClaimedByOther"),true,"claimed");Equal(Get(o,"TargetCombatKnown"),true,"combat known");Equal(Get(o,"TargetInCombat"),true,"fighting");Equal(Get(o,"TargetTargetsPlayer"),false,"other victim");b[79]=61;Checksum(b);o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"TargetClaimedByOther"),false,"not claimed");Equal(Get(o,"TargetTargetsPlayer"),true,"self victim");foreach(byte flags in new byte[]{2,8,32,64})Reject(x=>x[79]=flags);});
        Test("goods dialog exposes only a visible bounded rectangle with canonical flags",delegate{var b=Extra();b[80]=7;U16(b,81,20);U16(b,83,30);U16(b,85,120);U16(b,87,30);Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"GossipGoodsVisible"),true,"goods visible");Equal(Get(o,"GossipGoodsRegion"),new Rectangle(20,30,120,30),"goods rect");Reject(x=>x[80]=4);Reject(x=>x[80]=7);Reject(x=>x[81]=1);Reject(x=>x[125]=1);});
        Test("durability repair geometry cost and money remain explicit validated observations",delegate{var b=Extra();b[89]=3;b[92]=31;U16(b,93,20);U16(b,95,30);U16(b,97,24);U16(b,99,24);U32(b,101,50);U32(b,105,100);Checksum(b);var o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"GearKnown"),true,"gear known");Equal(Get(o,"GearRed"),true,"broken gear");Equal(Get(o,"RepairButtonEnabled"),true,"repair enabled");Equal(Get(o,"RepairCost"),50u,"cost");Equal(Get(o,"Money"),100u,"money");Reject(x=>x[89]=2);Reject(x=>{x[89]=3;U16(x,90,1);});Reject(x=>x[92]=4);Reject(x=>U32(x,101,1));Reject(x=>x[125]=1);});
        if(args.Length==2)Test("actual Lua WC3 and WP1 fixture cross decode",delegate {
            var core=ProbePacket.Decode(File.ReadAllBytes(args[0]));var extra=Decode(File.ReadAllBytes(args[1]));Equal(core.Sequence,Get(extra,"Sequence"),"same sequence");
            Equal(Get(extra,"TargetToken"),Hash("Creature-0-1-2-3-456-000001"),"GUID UTF8 hash");Equal(Get(extra,"TargetNameHash"),Hash("Target"),"complete name hash");
            Equal(Get(extra,"Portrait"),new Rectangle(832,8,64,64),"portrait physical client ROI");Equal(Get(extra,"KnownActionsMask"),15,"action evidence");
            foreach(string field in new[]{"LootOpened","LootClosed","CastSuccess","CastFailed","SkinSuccess"})Equal(Get(extra,field),1u,"counter evidence "+field);
        });
        if(args.Length==2)Test("paired pixel image uses exact live capture geometry",delegate {
            byte[] a=File.ReadAllBytes(args[0]),b=File.ReadAllBytes(args[1]);
            using(var first=PixelCodec.Encode(a,8))using(var second=PixelCodec.Encode(b,8))using(var combined=new Bitmap(66*8,12*8)){
                using(var graphics=Graphics.FromImage(combined)){graphics.Clear(Color.Black);graphics.DrawImageUnscaled(first,0,0);graphics.DrawImageUnscaled(second,34*8,0);}
                byte[] readA=PixelCodec.Decode(combined,4,4,8),readB=PixelCodec.Decode(combined,34*8+4,4,8);
                for(int i=0;i<128;i++){Equal(readA[i],a[i],"core byte "+i);Equal(readB[i],b[i],"extension byte "+i);}
                Equal(ProbePacket.Decode(readA).Sequence,Get(Decode(readB),"Sequence"),"paired pixel sequence");
            }
        });
        Console.WriteLine("Combat telemetry tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static uint Hash(string s) { uint h=5381;foreach(byte b in System.Text.Encoding.UTF8.GetBytes(s))h=unchecked(h*33+b);return h==0?1:h; }
}
