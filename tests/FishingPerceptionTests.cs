using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;
using WoWProbe;

class FishingPerceptionTests
{
    static int passed,failed;
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static void Assert(bool value,string why){if(!value)throw new Exception(why);}
    static void Reject(Action action){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(FormatException){rejected=true;}Assert(rejected,"expected invalid input rejection");}
    static string Root(){return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));}
    static string Assets(){return Path.Combine(Root(),"assets","fishing");}
    static float[] Tone(int rate,double gain){var a=new float[(int)(rate*.3)];for(int i=0;i<a.Length;i++){double t=i/(double)rate;int f=t<.1?1000:t<.2?2000:3500;a[i]=(float)(gain*Math.Sin(2*Math.PI*f*t));}return a;}
    static void Feed(FishingSoundDetector d,float[] samples,int rate,int chunk,long offset=0){for(int i=0;i<samples.Length;i+=chunk){int n=Math.Min(chunk,samples.Length-i);var block=new float[n];Array.Copy(samples,i,block,0,n);d.Push(block,rate,offset+(long)Math.Round((i+n)*1000.0/rate));}}
    static void Synthetic(Action<FishingSoundDetector> test){string dir=Path.Combine(Path.GetTempPath(),"wowprobe-fishing-perception-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);try{File.Copy(Path.Combine(Root(),"tests","fixtures","fishing-synthetic-template.json"),Path.Combine(dir,"sound-templates.json"));test(new FishingSoundDetector(dir));}finally{string full=Path.GetFullPath(dir),parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);Guid id;if(Path.GetDirectoryName(full)!=parent||!Path.GetFileName(full).StartsWith("wowprobe-fishing-perception-",StringComparison.Ordinal)||!Guid.TryParseExact(Path.GetFileName(full).Substring("wowprobe-fishing-perception-".Length),"N",out id))throw new Exception("Unsafe temporary cleanup target");Directory.Delete(full,true);}}
    static Bitmap TextImage(bool bottom,bool rectangle,int width=1280,int height=720){var b=new Bitmap(width,height);using(var g=Graphics.FromImage(b)){g.Clear(Color.Black);if(rectangle)g.FillRectangle(Brushes.DarkRed,width*.474f,height*.125f,width*.052f,height*.016f);else using(var reference=new Bitmap(Path.Combine(Assets(),"bag-text-reference.png"))){int w=(int)Math.Round(56*width/1176.0),h=(int)Math.Round(8*height/664.0);for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(reference.GetPixel(x*reference.Width/w,y*reference.Height/h).R>127)b.SetPixel((width-w)/2+x,(int)(height*(bottom?.8:.125))+y,Color.FromArgb(110,20,25));}}return b;}
    static int Main(string[] args)
    {
        if(args.Length==3&&args[0]=="--replay"){try{return Replay(args[1],args[2]);}catch(Exception ex){Console.WriteLine("FISHING_REPLAY_FAILED "+ex.Message);return 1;}}
        Test("fixed reference assets load without a native input or audio device",delegate{var d=new FishingSoundDetector(Assets());Assert(d.Snapshot().LastBiteAtMs==-1,"new detector retained a bite");new FishingBagTextDetector(Assets());});
        Test("a short reference event survives arbitrary audio chunk boundaries",delegate{Synthetic(d=>{Feed(d,Tone(16000,.3),16000,137);Assert(d.Snapshot().LastBiteAtMs>=0&&d.Snapshot().BiteConfidence>=.74,"tone event was not recognized");});});
        Test("audio gain and 48kHz resampling preserve the event",delegate{Synthetic(d=>{Feed(d,Tone(48000,.12),48000,503);Assert(d.Snapshot().LastBiteAtMs>=0,"resampled quiet event was not recognized");});});
        Test("silence and a stationary sine do not become a bite",delegate{var d=new FishingSoundDetector(Assets());Feed(d,new float[32000],16000,480);var a=new float[32000];for(int i=0;i<a.Length;i++)a[i]=(float)(.3*Math.Sin(2*Math.PI*1000*i/16000));Feed(d,a,16000,480,2000);Assert(d.Snapshot().LastBiteAtMs==-1&&d.Snapshot().LastBagVoiceAtMs==-1,"negative audio produced an event");});
        Test("one-second debounce merges nearby peaks and permits a later new event",delegate{Synthetic(d=>{var a=new float[27200];var tone=Tone(16000,.3);Array.Copy(tone,0,a,0,tone.Length);Array.Copy(tone,0,a,8000,tone.Length);Array.Copy(tone,0,a,19200,tone.Length);long previous=-1;int events=0;for(int i=0;i<a.Length;i+=160){var block=new float[Math.Min(160,a.Length-i)];Array.Copy(a,i,block,0,block.Length);d.Push(block,16000,(i+block.Length)/16);long current=d.Snapshot().LastBiteAtMs;if(current!=previous){events++;previous=current;}}Assert(events==2,"nearby peaks became duplicate or missing events");});});
        Test("uncorrelated wide-band noise does not match reference sounds",delegate{var d=new FishingSoundDetector(Assets());var random=new Random(19);var a=new float[48000];for(int i=0;i<a.Length;i++)a[i]=(float)((random.NextDouble()*2-1)*.3);Feed(d,a,16000,137);Assert(d.Snapshot().LastBiteAtMs==-1&&d.Snapshot().LastBagVoiceAtMs==-1,"noise produced a reference event");});
        Test("nonfinite audio and unsupported rates are rejected",delegate{var d=new FishingSoundDetector(Assets());Reject(()=>d.Push(new[]{Single.NaN},16000,1));Reject(()=>d.Push(new float[10],1,1));Reject(()=>d.Push(new float[10],16000,-1));});
        Test("reset and a discontinuity cannot retain an old bite",delegate{Synthetic(d=>{Feed(d,Tone(16000,.3),16000,320);Assert(d.Snapshot().LastBiteAtMs>=0,"setup event missing");d.Reset();Assert(d.Snapshot().LastBiteAtMs==-1,"reset retained an event");Feed(d,Tone(16000,.3),16000,320);d.Push(new float[320],16000,5000);Assert(d.Snapshot().LastBiteAtMs==-1,"gap retained an event");});});
        Test("snapshot copies cannot mutate detector state",delegate{var d=new FishingSoundDetector(Assets());var s=d.Snapshot();s.LastBiteAtMs=99;Assert(d.Snapshot().LastBiteAtMs==-1,"snapshot leaked state");});
        Test("specific top-centre glyph shape matches at both supported scales",delegate{var d=new FishingBagTextDetector(Assets());foreach(int width in new[]{1280,1920})using(var b=TextImage(false,false,width,width*9/16))Assert(d.Score(b)>.8,"reference text shape was missed");});
        Test("the same glyphs in chat and arbitrary red bars are rejected",delegate{var d=new FishingBagTextDetector(Assets());using(var b=TextImage(true,false))Assert(d.Score(b)<.5,"chat was treated as top error text");using(var b=TextImage(false,true))Assert(d.Score(b)<.8,"arbitrary red rectangle was accepted");});
        Test("empty images and missing assets fail explicitly",delegate{var d=new FishingBagTextDetector(Assets());using(var b=new Bitmap(1280,720))Assert(d.Score(b)==0,"blank image had text");Reject(()=>d.Score(null));try{new FishingSoundDetector(Path.Combine(Assets(),"missing"));throw new Exception("missing assets were accepted");}catch(IOException){} });
        Console.WriteLine("Fishing perception tests: "+passed+" passed, "+failed+" failed. No native audio or input.");return failed==0?0:1;
    }
    static int Replay(string directory,string report)
    {
        var audio=new List<object>();foreach(string name in new[]{"splash","bag"})
        {
            var d=new FishingSoundDetector(Assets());float[] samples=Wave(Path.Combine(directory,name+".wav"));var bites=new List<object>();var voices=new List<object>();long lastBite=-1,lastVoice=-1;
            for(int i=0;i<samples.Length;i+=137){var block=new float[Math.Min(137,samples.Length-i)];Array.Copy(samples,i,block,0,block.Length);d.Push(block,16000,(long)Math.Round((i+block.Length)/16.0));var s=d.Snapshot();if(s.LastBiteAtMs!=lastBite){bites.Add(new{at_ms=s.LastBiteAtMs,confidence=s.BiteConfidence,detected_at_ms=(i+block.Length)/16});lastBite=s.LastBiteAtMs;}if(s.LastBagVoiceAtMs!=lastVoice){voices.Add(new{at_ms=s.LastBagVoiceAtMs,confidence=s.BagVoiceConfidence,detected_at_ms=(i+block.Length)/16});lastVoice=s.LastBagVoiceAtMs;}}
            audio.Add(new{reference=name,samples=samples.Length,rate=16000,bites=bites,bag_voice=voices});
        }
        double earliestText=-1;int falseText=0;var images=new List<object>();var text=new FishingBagTextDetector(Assets());foreach(string name in new[]{"splash","bag"})
        {string folder=Path.Combine(directory,name+"-frames");string[] files=Directory.GetFiles(folder,"*.png");Array.Sort(files,StringComparer.Ordinal);for(int i=0;i<files.Length;i++)using(var source=new Bitmap(files[i])){var crop=new Rectangle(52,22,1176,664);using(var client=source.Clone(crop,System.Drawing.Imaging.PixelFormat.Format32bppArgb)){double score=text.Score(client);if(score>=.8){if(name=="splash")falseText++;else if(earliestText<0)earliestText=i*200+100;}images.Add(new{reference=name,frame=i,time_ms=i*200+100,score=score});}}}
        var result=new{kind="reference_replay_not_live_accuracy",game_validation="pending",audio=audio,text=images};string json=new JavaScriptSerializer().Serialize(result);File.WriteAllText(report,json,new UTF8Encoding(false));Console.WriteLine(json);Assert(earliestText>=0&&earliestText<=500,"visible early bag text was not recognized in time for audio fusion");Assert(falseText==0,"splash frames falsely matched bag text");return 0;
    }
    static float[] Wave(string path)
    {
        using(var stream=new BinaryReader(File.OpenRead(path))){Assert(new string(stream.ReadChars(4))=="RIFF","invalid WAV");stream.ReadUInt32();Assert(new string(stream.ReadChars(4))=="WAVE","invalid WAV format");int format=0,channels=0,rate=0,bits=0;while(stream.BaseStream.Position+8<=stream.BaseStream.Length){string id=new string(stream.ReadChars(4));uint bytes=stream.ReadUInt32();long end=stream.BaseStream.Position+bytes;if(end>stream.BaseStream.Length||bytes>16*1024*1024)throw new Exception("invalid WAV chunk");if(id=="fmt "){format=stream.ReadUInt16();channels=stream.ReadUInt16();rate=stream.ReadInt32();stream.ReadInt32();stream.ReadUInt16();bits=stream.ReadUInt16();}else if(id=="data"){Assert(format==1&&channels==1&&rate==16000&&bits==16,"WAV replay expects 16k mono PCM16");var a=new float[bytes/2];for(int i=0;i<a.Length;i++)a[i]=stream.ReadInt16()/32768f;return a;}stream.BaseStream.Position=end+(bytes%2);}}throw new Exception("WAV data not found");
    }
}
