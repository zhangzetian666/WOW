using System;
using WoWProbe;

class FishingAudioFormatTests
{
    static int passed,failed;
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    static void Near(float actual,double expected){Check(Math.Abs(actual-expected)<.00001,"sample differs: "+actual+" expected "+expected);}
    static void Reject(Action action){bool rejected=false;try{action();}catch(FormatException){rejected=true;}catch(ArgumentException){rejected=true;}Check(rejected,"invalid audio accepted");}
    static void Put16(byte[] b,int at,int value){b[at]=(byte)value;b[at+1]=(byte)(value>>8);}
    static void Put32(byte[] b,int at,uint value){for(int n=0;n<4;n++)b[at+n]=(byte)(value>>(8*n));}
    static byte[] Format(int tag,int channels,int bits,int sampleRate=48000)
    {var b=new byte[tag==65534?40:18];Put16(b,0,tag);Put16(b,2,channels);Put32(b,4,(uint)sampleRate);Put32(b,8,(uint)(sampleRate*channels*bits/8));Put16(b,12,channels*bits/8);Put16(b,14,bits);if(tag==65534){Put16(b,16,22);Put16(b,18,bits);new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray().CopyTo(b,24);}return b;}
    static byte[] Bytes(params double[] values){var b=new byte[values.Length*8];for(int n=0;n<values.Length;n++)BitConverter.GetBytes(values[n]).CopyTo(b,n*8);return b;}
    static int Main()
    {
        Test("PCM16 sign and stereo downmix retain actual sample rate",delegate{var f=FishingAudioFormat.Parse(Format(1,2,16,44100));Check(f.SampleRate==44100&&f.Channels==2&&f.BlockAlign==4,"format metadata");var a=f.Decode(new byte[]{0,128,0,128,255,127,0,0,0,64,0,192},3,false);Near(a[0],-1);Near(a[1],32767.0/65536);Near(a[2],0);});
        Test("PCM24 sign extension handles minimum maximum and midpoint",delegate{var f=FishingAudioFormat.Parse(Format(1,1,24));var a=f.Decode(new byte[]{0,0,128,255,255,127,0,0,64},3,false);Near(a[0],-1);Near(a[1],8388607.0/8388608);Near(a[2],.5);});
        Test("PCM32 normalizes signed full scale",delegate{var f=FishingAudioFormat.Parse(Format(1,1,32));var a=f.Decode(new byte[]{0,0,0,128,255,255,255,127,0,0,0,64},3,false);Near(a[0],-1);Near(a[1],2147483647.0/2147483648);Near(a[2],.5);});
        Test("float32 stereo is averaged and bounded",delegate{var b=new byte[16];BitConverter.GetBytes(.75f).CopyTo(b,0);BitConverter.GetBytes(-.25f).CopyTo(b,4);BitConverter.GetBytes(3f).CopyTo(b,8);BitConverter.GetBytes(2f).CopyTo(b,12);var a=FishingAudioFormat.Parse(Format(3,2,32)).Decode(b,2,false);Near(a[0],.25);Near(a[1],1);});
        Test("float64 downmix preserves quiet input",delegate{var a=FishingAudioFormat.Parse(Format(3,2,64)).Decode(Bytes(.0002,-.0001,.5,-.5),2,false);Near(a[0],.00005);Near(a[1],0);});
        Test("extensible PCM accepts 24 valid bits in a 32 bit container",delegate{var b=Format(65534,1,32);Put16(b,18,24);var a=FishingAudioFormat.Parse(b).Decode(new byte[]{0,0,0,64},1,false);Near(a[0],.5);});
        Test("extensible IEEE float subtype is not decoded as PCM",delegate{var b=Format(65534,1,32);new Guid("00000003-0000-0010-8000-00aa00389b71").ToByteArray().CopyTo(b,24);var a=FishingAudioFormat.Parse(b).Decode(BitConverter.GetBytes(.125f),1,false);Near(a[0],.125);});
        Test("silent endpoint packets produce zeros without dereferencing a null buffer",delegate{var f=FishingAudioFormat.Parse(Format(3,2,32));var a=f.Decode(null,120,true);Check(a.Length==120,"silent frame count");foreach(float sample in a)Near(sample,0);});
        Test("empty packet remains empty",delegate{var a=FishingAudioFormat.Parse(Format(1,1,16)).Decode(new byte[0],0,false);Check(a.Length==0,"empty audio invented samples");});
        Test("short long null and oversized non-silent packets are rejected",delegate{var f=FishingAudioFormat.Parse(Format(1,2,16));Reject(()=>f.Decode(new byte[3],1,false));Reject(()=>f.Decode(new byte[5],1,false));Reject(()=>f.Decode(null,1,false));Reject(()=>f.Decode(new byte[0],Int32.MaxValue,false));Reject(()=>f.Decode(null,-1,true));});
        Test("nonfinite float values cannot become a sound match",delegate{var f=FishingAudioFormat.Parse(Format(3,1,32));Reject(()=>f.Decode(BitConverter.GetBytes(float.NaN),1,false));Reject(()=>f.Decode(BitConverter.GetBytes(float.PositiveInfinity),1,false));var g=FishingAudioFormat.Parse(Format(3,1,64));Reject(()=>g.Decode(Bytes(double.NaN),1,false));});
        Test("unsupported encoding bit widths and extensible subtypes are rejected",delegate{Reject(()=>FishingAudioFormat.Parse(Format(2,1,16)));Reject(()=>FishingAudioFormat.Parse(Format(1,1,8)));Reject(()=>FishingAudioFormat.Parse(Format(3,1,16)));var b=Format(65534,1,32);Guid.NewGuid().ToByteArray().CopyTo(b,24);Reject(()=>FishingAudioFormat.Parse(b));});
        Test("truncated or inconsistent format descriptions are rejected",delegate{Reject(()=>FishingAudioFormat.Parse(new byte[17]));var b=Format(65534,2,32);Array.Resize(ref b,39);Reject(()=>FishingAudioFormat.Parse(b));b=Format(1,2,16);Put16(b,12,2);Reject(()=>FishingAudioFormat.Parse(b));b=Format(1,2,16);Put32(b,8,123);Reject(()=>FishingAudioFormat.Parse(b));b=Format(65534,1,32);Put16(b,18,33);Reject(()=>FishingAudioFormat.Parse(b));});
        Test("sample rate and channel bounds reject impossible endpoint formats",delegate{Reject(()=>FishingAudioFormat.Parse(Format(1,0,16)));Reject(()=>FishingAudioFormat.Parse(Format(1,33,16)));Reject(()=>FishingAudioFormat.Parse(Format(1,1,16,1)));Reject(()=>FishingAudioFormat.Parse(Format(1,1,16,500000)));});
        Test("discontinuity and timestamp flags are explicitly marked unreliable",delegate{var f=FishingAudioFormat.Parse(Format(1,1,16));var a=f.DecodePacket(new byte[]{0,0},1,1,500);Check(a.Discontinuous&&a.EndedAtMs==500&&a.SampleRate==48000,"discontinuity not exposed");a=f.DecodePacket(new byte[]{0,0},1,4,501);Check(a.Discontinuous,"timestamp error lost");a=f.DecodePacket(null,1,2,502);Check(!a.Discontinuous&&a.Samples.Length==1,"ordinary silence treated as a gap");Near(a.Samples[0],0);});
        Test("unknown packet flags and negative packet time are rejected",delegate{var f=FishingAudioFormat.Parse(Format(1,1,16));Reject(()=>f.DecodePacket(new byte[]{0,0},1,8,0));Reject(()=>f.DecodePacket(new byte[]{0,0},1,0,-1));});
        Test("decoding does not mutate caller bytes",delegate{var b=new byte[]{0,128,255,127};var copy=(byte[])b.Clone();FishingAudioFormat.Parse(Format(1,1,16)).Decode(b,2,false);Check(Convert.ToBase64String(b)==Convert.ToBase64String(copy),"input was mutated");});
        Console.WriteLine("Fishing audio format tests: "+passed+" passed, "+failed+" failed. Synthetic bytes only; no audio device was started.");return failed==0?0:1;
    }
}
