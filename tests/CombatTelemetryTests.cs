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
        Test("unknown capabilities retain NaN and false-known distinctions",delegate {
            object p=Decode(Extra());Equal(Get(p,"Capabilities"),0,"caps");Assert(Double.IsNaN((double)Get(p,"TargetX")),"unknown coordinate");
            var o=Observe(ProbePacket.Decode(Core()),p,true);Equal(Get(o,"Fresh"),true,"fresh pair");Equal(Get(o,"KnownCore"),true,"known core");Equal(Get(o,"AttackabilityKnown"),false,"unknown attackability");
            var b=Extra();b[6]=2;Checksum(b);o=Observe(ProbePacket.Decode(Core()),Decode(b),true);Equal(Get(o,"AttackabilityKnown"),true,"known false");Equal(Get(o,"TargetCanAttack"),false,"false value");
        });
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
            Reject(delegate(byte[] b){b[56]=32;});Reject(delegate(byte[] b){b[57]=1;});Reject(delegate(byte[] b){b[125]=1;});
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
        if(args.Length==2)Test("actual Lua WC3 and WP1 fixture cross decode",delegate {
            var core=ProbePacket.Decode(File.ReadAllBytes(args[0]));var extra=Decode(File.ReadAllBytes(args[1]));Equal(core.Sequence,Get(extra,"Sequence"),"same sequence");
            Equal(Get(extra,"TargetToken"),Hash("Creature-0-1-2-3-456-000001"),"GUID UTF8 hash");Equal(Get(extra,"TargetNameHash"),Hash("Target"),"complete name hash");
            Equal(Get(extra,"Portrait"),new Rectangle(560,8,64,64),"portrait physical client ROI");Equal(Get(extra,"KnownActionsMask"),15,"action evidence");
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
