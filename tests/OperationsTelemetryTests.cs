using System;
using System.Drawing;
using System.IO;
using WoWProbe;

class OperationsTelemetryTests
{
    static int passed,failed;
    static void Test(string n,Action a){try{a();passed++;Console.WriteLine("PASS "+n);}catch(Exception e){failed++;Console.WriteLine("FAIL "+n+": "+e.Message);}}
    static void Assert(bool b,string why){if(!b)throw new Exception(why);}
    static void U16(byte[] b,int p,int n){b[p]=(byte)n;b[p+1]=(byte)(n>>8);}
    static void U32(byte[] b,int p,uint n){for(int i=0;i<4;i++)b[p+i]=(byte)(n>>(8*i));}
    static void Sum(byte[] b){int a=0,c=0;for(int i=0;i<126;i++){a=(a+b[i])%255;c=(c+a)%255;}b[126]=(byte)a;b[127]=(byte)c;}
    static byte[] Empty(){var b=new byte[128];b[0]=87;b[1]=79;b[2]=52;b[3]=1;U16(b,4,7);U32(b,26,UInt32.MaxValue);b[70]=255;U32(b,72,UInt32.MaxValue);Sum(b);return b;}
    static byte[] Full()
    {
        var b=Empty();U16(b,6,8191);U16(b,8,1+2+16+32+256);U16(b,10,200);U16(b,12,300);U16(b,14,150);U16(b,16,40);U32(b,26,0);
        U16(b,30,1920);U16(b,32,1080);U32(b,34,100);U32(b,38,5);U32(b,42,1);U32(b,46,2);U32(b,50,1);U32(b,54,3);U16(b,58,0);U16(b,60,3);b[62]=0;b[63]=1;
        U32(b,64,1001);U16(b,68,2);b[70]=0;b[71]=8+32+64;U32(b,72,7);U32(b,76,123);U16(b,80,1600);U16(b,82,800);U16(b,84,35);U16(b,86,35);
        U32(b,88,900);U32(b,92,901);b[96]=2;b[97]=1;b[101]=1;b[106]=1;Sum(b);return b;
    }
    static void Reject(Action<byte[]> change){var b=Full();change(b);Sum(b);try{OperationsPacket.Decode(b);}catch(FormatException){return;}throw new Exception("malformed packet accepted");}
    static ProbePacket Core(){var b=new byte[128];b[0]=87;b[1]=80;b[2]=49;b[3]=1;U16(b,4,7);b[6]=8+32;b[7]=223;b[8]=50;b[9]=60;b[10]=80;b[11]=0;U16(b,12,16384);U16(b,14,49151);U16(b,16,10000);U16(b,18,0);U32(b,20,42);U16(b,24,30000);U16(b,26,20000);b[122]=2;b[123]=255;b[125]=255;Sum(b);return ProbePacket.Decode(b);}
    static CombatPacket Combat(int sequence){var b=new byte[128];b[0]=87;b[1]=67;b[2]=51;b[3]=1;U16(b,4,sequence);U16(b,12,65535);U16(b,14,65535);b[16]=1;Sum(b);return CombatPacket.Decode(b);}
    static void MainTests(string[] args)
    {
        Test("unknown WO4 packet preserves unknown distinctions",delegate{var o=new OperationObservation();OperationsPacket.Decode(Empty()).ApplyTo(o);Assert(!o.GeometryKnown&&!o.InventoryKnown&&!o.ReleaseKnown&&!o.CursorKnown&&!o.RecoveryDelayKnown,"unknown became known");Assert(o.RecoveryDelayMs==-1&&o.Item!=null&&!o.Item.Known,"unknown sentinels lost");});
        Test("WO4 legal frame decodes actual merchant identity inventory and physical regions",delegate{var o=new OperationObservation();OperationsPacket.Decode(Full()).ApplyTo(o);Assert(o.Sequence==7&&o.ReleaseVisible&&o.ReleaseEnabled&&o.ReleaseRegion==new Rectangle(200,300,150,40),"release ROI");Assert(o.MerchantOpen&&o.MerchantTargetToken==900&&o.MerchantNameHash==901,"merchant identity");Assert(o.InventoryKnown&&o.InventoryEventsKnown&&o.InventoryCycleKnown&&o.Item.Known&&o.Item.ItemId==1001&&o.Item.UnitPrice==7,"item metadata");Assert(o.Item.Region==new Rectangle(1600,800,35,35)&&o.SellTabKnown&&o.SellTab,"sell tab/physical item ROI");});
        Test("packet rejects wrong length magic version checksum and unsupported bits",delegate{foreach(var b in new[]{new byte[127],new byte[128]}){try{OperationsPacket.Decode(b);throw new Exception("accepted");}catch(FormatException){}}var x=Full();x[10]++;try{OperationsPacket.Decode(x);throw new Exception("bad checksum");}catch(FormatException){}Reject(delegate(byte[] b){b[3]=2;});Reject(delegate(byte[] b){b[7]|=32;});Reject(delegate(byte[] b){b[9]|=2;});});
        Test("flags require known capabilities and enabled visible buttons",delegate{Reject(delegate(byte[] b){U16(b,6,8190);});Reject(delegate(byte[] b){U16(b,8,2+16+32+256);});Reject(delegate(byte[] b){U16(b,6,8191-32);});Reject(delegate(byte[] b){U16(b,6,8191-4096);});});
        Test("unknown and invisible rectangles cannot hide coordinates or exceed client",delegate{Reject(delegate(byte[] b){U16(b,10,1900);});Reject(delegate(byte[] b){U16(b,18,1);});Reject(delegate(byte[] b){U16(b,80,1900);});Reject(delegate(byte[] b){U16(b,6,8191-128);});Reject(delegate(byte[] b){U16(b,32,0);});});
        Test("recovery delay is explicit bounded or unknown",delegate{Reject(delegate(byte[] b){U32(b,26,3600001);});Reject(delegate(byte[] b){U32(b,26,UInt32.MaxValue);});Reject(delegate(byte[] b){U16(b,6,8191-4);});});
        Test("open merchant requires npc identity and closed merchant cannot claim identity",delegate{Reject(delegate(byte[] b){U32(b,88,0);});Reject(delegate(byte[] b){U16(b,8,1+2+32+256);});});
        Test("inventory geometry occupancy and current page are independently validated",delegate{Reject(delegate(byte[] b){b[96]=41;});Reject(delegate(byte[] b){b[101]|=4;});Reject(delegate(byte[] b){U16(b,60,4);});Reject(delegate(byte[] b){U16(b,58,3);});Reject(delegate(byte[] b){b[63]=3;});Reject(delegate(byte[] b){b[101]=0;});});
        Test("item metadata cannot sell unknown or incoherent item claims",delegate{Reject(delegate(byte[] b){b[70]=8;});Reject(delegate(byte[] b){U32(b,64,0);});Reject(delegate(byte[] b){U16(b,68,0);});Reject(delegate(byte[] b){U32(b,76,0);});Reject(delegate(byte[] b){b[71]|=2;b[71]&=223;});Reject(delegate(byte[] b){b[71]|=4;b[71]&=191;});});
        Test("empty inventory slots have canonical safe metadata",delegate{var b=Full();b[71]=8+16+32+64;U32(b,64,0);U16(b,68,0);b[70]=255;U32(b,72,0);b[101]=0;Sum(b);var o=new OperationObservation();OperationsPacket.Decode(b).ApplyTo(o);Assert(o.Item.Known&&o.Item.Empty&&!o.SlotOccupied(0,1),"empty slot");Reject(delegate(byte[] x){x[71]|=16;});});
        Test("observation and item clones isolate arrays and farm state",delegate{var o=new OperationObservation{Farm=new FarmObservation{TargetName="before"}};OperationsPacket.Decode(Full()).ApplyTo(o);var c=o.Clone();c.Item.Count=99;c.BagSizes[0]=1;c.Occupancy[0]=0;c.Farm.TargetName="after";Assert(o.Item.Count==2&&o.BagSizes[0]==2&&o.SlotOccupied(0,1)&&o.Farm.TargetName=="before","shared mutation");try{o.SlotOccupied(4,1);throw new Exception("slot out of range");}catch(ArgumentOutOfRangeException){}try{new OperationObservation().SlotOccupied(0,1);throw new Exception("unknown inventory");}catch(InvalidOperationException){} });
        Test("all three sequences and explicit freshness gate operations",delegate{var core=Core();var extra=Combat(7);var ops=OperationsPacket.Decode(Full());var o=OperationObservation.FromPackets(core,extra,ops,true);Assert(o.Fresh&&o.CorpseKnown&&o.CorpseX==core.CorpseX&&o.FreeSlotsKnown&&o.FreeSlots==0&&o.MapId==42,"original core facts were discarded");Assert(!OperationObservation.FromPackets(core,Combat(8),ops,true).Fresh,"mismatched combat");var b=Full();U16(b,4,8);Sum(b);Assert(!OperationObservation.FromPackets(core,extra,OperationsPacket.Decode(b),true).Fresh,"mismatched operations");Assert(!OperationObservation.FromPackets(core,extra,null,true).Fresh&&!OperationObservation.FromPackets(core,null,ops,true).Fresh&&!OperationObservation.FromPackets(core,extra,ops,false).Fresh,"missing packet or stale caller");});
        if(args.Length==3)Test("actual Lua WO4 matches original WP1 and WC3 frames",delegate{var o=OperationObservation.FromPackets(ProbePacket.Decode(File.ReadAllBytes(args[0])),CombatPacket.Decode(File.ReadAllBytes(args[1])),OperationsPacket.Decode(File.ReadAllBytes(args[2])),true);Assert(o.Fresh&&o.Sequence==o.Farm.Sequence&&o.InventoryKnown&&o.MerchantOpen,"matched Lua fixtures");Assert(o.MerchantTargetToken!=o.Farm.TargetToken&&o.Item.ItemId==1001&&o.Item.Count==2&&o.Item.Known,"npc identity or metadata fixture mismatch");});
        if(args.Length==3)Test("three physical grids decode at exact live screen offsets",delegate{var core=File.ReadAllBytes(args[0]);var combat=File.ReadAllBytes(args[1]);var operations=File.ReadAllBytes(args[2]);using(var image=new Bitmap(100*8,12*8))using(var g=Graphics.FromImage(image))using(var a=PixelCodec.Encode(core,8))using(var b=PixelCodec.Encode(combat,8))using(var c=PixelCodec.Encode(operations,8)){g.DrawImageUnscaled(a,0,0);g.DrawImageUnscaled(b,34*8,0);g.DrawImageUnscaled(c,68*8,0);var p=PixelCodec.Decode(image,68*8+4,4,8);for(int i=0;i<128;i++)Assert(p[i]==operations[i],"WO4 pixel byte "+i);Assert(ProbePacket.Decode(PixelCodec.Decode(image,4,4,8)).Sequence==OperationsPacket.Decode(p).Sequence,"physical sequence");}});
    }
    static int Main(string[] args){MainTests(args);Console.WriteLine("Operations telemetry tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;}
}
