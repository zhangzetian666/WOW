using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace WoWProbe
{
    public sealed class FishingSoundSnapshot
    {
        public long LastBiteAtMs=-1,LastBagVoiceAtMs=-1;
        public double BiteConfidence,BagVoiceConfidence;
    }
    public sealed class FishingSoundDetector
    {
        sealed class Template{public bool Bite;public int Frames;public double[] Values;public double Norm;}
        readonly object gate=new object();readonly List<Template> templates=new List<Template>();
        readonly List<double[]> history=new List<double[]>();readonly float[] ring=new float[320];
        readonly double[] taper=new double[320];readonly int[] starts=new int[16],ends=new int[16];
        readonly double biteThreshold,voiceThreshold;int maximumFrames,write,rate;long canonicalCount,sourceCount,lastEnded=-1;
        double sourcePosition,streamStart;float previous;FishingSoundSnapshot result=new FishingSoundSnapshot();
        public FishingSoundDetector(string templateDirectory)
        {
            var data=FishingPerceptionFiles.Read(templateDirectory,"sound-templates.json");
            RouteJson.Keys(data,"schema_version,sample_rate,frame_size,hop_size,bands,bite_threshold,voice_threshold,templates",null);
            if(RouteJson.Integer(data,"schema_version",1,1)!=1||RouteJson.Integer(data,"sample_rate",16000,16000)!=16000||
                RouteJson.Integer(data,"frame_size",320,320)!=320||RouteJson.Integer(data,"hop_size",160,160)!=160||RouteJson.Integer(data,"bands",16,16)!=16)throw new FormatException("Unsupported fishing audio feature format.");
            biteThreshold=RouteJson.Number(data,"bite_threshold");voiceThreshold=RouteJson.Number(data,"voice_threshold");
            if(biteThreshold<.5||biteThreshold>.99||voiceThreshold<.5||voiceThreshold>.99)throw new FormatException("Invalid fishing audio match threshold.");
            var list=data["templates"] as List<object>;if(list==null||list.Count<1||list.Count>16)throw new FormatException("Invalid fishing audio template count.");
            bool bite=false,voice=false;var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var value in list)
            {
                var t=RouteJson.Object(value);RouteJson.Keys(t,"kind,name,frames,values",null);string kind=RouteJson.String(t,"kind"),name=RouteJson.String(t,"name");
                if((kind!="bite"&&kind!="bag_voice")||name.Length<1||name.Length>80||!names.Add(name))throw new FormatException("Invalid or duplicate fishing template identity.");
                int frames=RouteJson.Integer(t,"frames",10,200);var values=t["values"] as List<object>;
                if(values==null||values.Count!=frames*16)throw new FormatException("Fishing audio template dimensions disagree.");
                var temp=new Template{Bite=kind=="bite",Frames=frames,Values=new double[frames*16]};
                for(int i=0;i<values.Count;i++){double n=FishingPerceptionFiles.Number(values[i]);if(n< -20||n>20)throw new FormatException("Fishing feature value outside bounds.");temp.Values[i]=n;}
                for(int band=0;band<16;band++){double mean=0;for(int i=0;i<frames;i++)mean+=temp.Values[i*16+band];mean/=frames;for(int i=0;i<frames;i++)temp.Values[i*16+band]-=mean;}
                double norm=0;foreach(double n in temp.Values)norm+=n*n;temp.Norm=Math.Sqrt(norm);if(temp.Norm<.01)throw new FormatException("Fishing audio template has no temporal variation.");
                templates.Add(temp);maximumFrames=Math.Max(maximumFrames,frames);bite|=temp.Bite;voice|=!temp.Bite;
            }
            if(!bite||!voice)throw new FormatException("Both bite and bag-voice references are required.");
            for(int i=0;i<320;i++)taper[i]=.54-.46*Math.Cos(2*Math.PI*i/319);
            for(int i=0;i<16;i++){starts[i]=(int)Math.Ceiling(100*Math.Pow(78,i/16.0)*512/16000);ends[i]=(int)Math.Ceiling(100*Math.Pow(78,(i+1)/16.0)*512/16000);}
        }
        public void Push(float[] mono,int sampleRate,long endedAtMs)
        {
            lock(gate)
            {
                if(mono==null||mono.Length==0||sampleRate<8000||sampleRate>192000||mono.Length>sampleRate*2||endedAtMs<0){ResetCore();throw new ArgumentException("Invalid fishing audio block, rate or timestamp.");}
                foreach(float n in mono)if(Single.IsNaN(n)||Single.IsInfinity(n)||Math.Abs(n)>16){ResetCore();throw new ArgumentException("Invalid fishing audio sample.");}
                double duration=mono.Length*1000.0/sampleRate;
                if(lastEnded>=0&&(rate!=sampleRate||endedAtMs<lastEnded||Math.Abs(endedAtMs-lastEnded-duration)>120))ResetCore();
                if(rate==0){rate=sampleRate;streamStart=endedAtMs-duration;}
                long first=sourceCount,last=first+mono.Length-1;double step=rate/16000.0;
                while(sourcePosition<=last)
                {
                    long lower=(long)Math.Floor(sourcePosition);double fraction=sourcePosition-lower;
                    if(fraction>1e-8&&lower>=last)break;
                    int index=(int)(lower-first);float left=index<0?previous:mono[index];float right=fraction<=1e-8?left:mono[index+1];
                    Add((float)(left+(right-left)*fraction));sourcePosition+=step;
                }
                sourceCount+=mono.Length;previous=mono[mono.Length-1];lastEnded=endedAtMs;
            }
        }
        void Add(float value)
        {
            ring[write]=value;write=(write+1)%320;canonicalCount++;
            if(canonicalCount<320||(canonicalCount-320)%160!=0)return;
            double rms=0;var real=new double[512];var imaginary=new double[512];
            for(int i=0;i<320;i++){double n=ring[(write+i)%320];rms+=n*n;real[i]=n*taper[i];}rms=Math.Sqrt(rms/320);
            Transform(real,imaginary);var features=new double[16];
            for(int band=0;band<16;band++){double sum=0;for(int k=starts[band];k<ends[band];k++)sum+=real[k]*real[k]+imaginary[k]*imaginary[k];features[band]=Math.Log10(1e-10+sum/(ends[band]-starts[band]));}
            history.Add(features);if(history.Count>maximumFrames)history.RemoveAt(0);if(rms<.0035)return;
            double end=streamStart+canonicalCount*1000.0/16000;
            foreach(var t in templates)
            {
                if(history.Count<t.Frames)continue;int offset=history.Count-t.Frames;double dot=0,variance=0;
                for(int band=0;band<16;band++)
                {
                    double sum=0,square=0;for(int i=0;i<t.Frames;i++){double x=history[offset+i][band];sum+=x;square+=x*x;dot+=x*t.Values[i*16+band];}
                    variance+=Math.Max(0,square-sum*sum/t.Frames);
                }
                if(variance<.0001)continue;double score=Math.Max(0,Math.Min(1,dot/(t.Norm*Math.Sqrt(variance))));
                long at=(long)Math.Round(end-(t.Frames-1)*10-20);if(at<0)continue;
                if(t.Bite&&score>=biteThreshold&&(result.LastBiteAtMs<0||at-result.LastBiteAtMs>=1000)){result.LastBiteAtMs=at;result.BiteConfidence=score;}
                if(!t.Bite&&score>=voiceThreshold&&(result.LastBagVoiceAtMs<0||at-result.LastBagVoiceAtMs>=2000)){result.LastBagVoiceAtMs=at;result.BagVoiceConfidence=score;}
            }
        }
        static void Transform(double[] real,double[] imaginary)
        {
            int n=real.Length;for(int i=1,j=0;i<n;i++){int bit=n>>1;for(;(j&bit)!=0;bit>>=1)j^=bit;j^=bit;if(i<j){double t=real[i];real[i]=real[j];real[j]=t;}}
            for(int size=2;size<=n;size<<=1){double angle=-2*Math.PI/size,cr=Math.Cos(angle),ci=Math.Sin(angle);for(int start=0;start<n;start+=size){double wr=1,wi=0;for(int j=0;j<size/2;j++){int a=start+j,b=a+size/2;double tr=wr*real[b]-wi*imaginary[b],ti=wr*imaginary[b]+wi*real[b];real[b]=real[a]-tr;imaginary[b]=imaginary[a]-ti;real[a]+=tr;imaginary[a]+=ti;double next=wr*cr-wi*ci;wi=wr*ci+wi*cr;wr=next;}}}
        }
        void ResetCore(){history.Clear();Array.Clear(ring,0,ring.Length);write=rate=0;canonicalCount=sourceCount=0;sourcePosition=streamStart=0;previous=0;lastEnded=-1;result=new FishingSoundSnapshot();}
        public void Reset(){lock(gate)ResetCore();}
        public FishingSoundSnapshot Snapshot(){lock(gate)return new FishingSoundSnapshot{LastBiteAtMs=result.LastBiteAtMs,LastBagVoiceAtMs=result.LastBagVoiceAtMs,BiteConfidence=result.BiteConfidence,BagVoiceConfidence=result.BagVoiceConfidence};}
    }
    public sealed class FishingBagTextDetector
    {
        readonly bool[] reference;readonly int width,height,count;
        public FishingBagTextDetector(string templateDirectory)
        {
            var data=FishingPerceptionFiles.Read(templateDirectory,"bag-text.json");RouteJson.Keys(data,"schema_version,width,height,mask",null);RouteJson.Integer(data,"schema_version",1,1);
            width=RouteJson.Integer(data,"width",16,256);height=RouteJson.Integer(data,"height",4,64);var mask=data["mask"] as List<object>;
            if(mask==null||mask.Count!=width*height)throw new FormatException("Fishing text template dimensions disagree.");reference=new bool[mask.Count];
            for(int i=0;i<reference.Length;i++){double n=FishingPerceptionFiles.Number(mask[i]);if(n!=0&&n!=1)throw new FormatException("Fishing text mask must be binary.");reference[i]=n==1;if(reference[i])count++;}
            if(count<20||count>reference.Length*.85)throw new FormatException("Fishing text template lacks a specific glyph shape.");
        }
        public double Score(Bitmap clientImage)
        {
            if(clientImage==null)throw new ArgumentNullException("clientImage");if(clientImage.Width<320||clientImage.Height<240||clientImage.Width>8192||clientImage.Height>8192)throw new ArgumentException("Unsupported fishing client image dimensions.");
            var roi=new Rectangle((int)(clientImage.Width*.30),(int)(clientImage.Height*.08),(int)(clientImage.Width*.40),(int)(clientImage.Height*.14));
            var red=new bool[roi.Width*roi.Height];var rows=new int[roi.Height];BitmapData data=null;
            try{
                data=clientImage.LockBits(roi,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);var row=new byte[roi.Width*4];
                for(int y=0;y<roi.Height;y++){Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,row.Length);for(int x=0;x<roi.Width;x++){int b=row[x*4],g=row[x*4+1],r=row[x*4+2];bool yes=r>=32&&r>g*1.5+10&&r>b*1.25+8&&r-g>=15;red[y*roi.Width+x]=yes;if(yes)rows[y]++;}}
            }finally{if(data!=null)clientImage.UnlockBits(data);}
            double best=0;int gap=Math.Max(1,clientImage.Height/360),start=-1,last=-1;
            for(int y=0;y<=roi.Height+gap;y++)
            {
                bool occupied=y<roi.Height&&rows[y]>0;if(occupied){if(start<0)start=y;last=y;}
                if(start>=0&&(!occupied&&y-last>gap)){best=Math.Max(best,LineScore(red,roi.Width,start,last,clientImage.Width,clientImage.Height));start=last=-1;}
            }
            return best;
        }
        double LineScore(bool[] mask,int stride,int top,int bottom,int clientWidth,int clientHeight)
        {
            int left=stride,right=-1;for(int y=top;y<=bottom;y++)for(int x=0;x<stride;x++)if(mask[y*stride+x]){left=Math.Min(left,x);right=Math.Max(right,x);}
            int w=right-left+1,h=bottom-top+1;if(w<clientWidth*.015||w>clientWidth*.18||h<clientHeight*.004||h>clientHeight*.045||w/(double)h<4||w/(double)h>12)return 0;
            int observed=0,intersection=0;for(int y=0;y<height;y++)for(int x=0;x<width;x++){int sx=left+Math.Min(w-1,(int)((x+.5)*w/width)),sy=top+Math.Min(h-1,(int)((y+.5)*h/height));bool on=mask[sy*stride+sx];if(on)observed++;if(on&&reference[y*width+x])intersection++;}
            if(observed>reference.Length*.88||observed<reference.Length*.12)return 0;
            return observed==0?0:2.0*intersection/(observed+count);
        }
    }
    internal static class FishingPerceptionFiles
    {
        internal static Dictionary<string,object> Read(string directory,string name)
        {if(String.IsNullOrWhiteSpace(directory))throw new ArgumentException("Fishing template directory is required.");string path=Path.Combine(directory,name);if(new FileInfo(path).Length>512*1024)throw new FormatException("Fishing template exceeds size limit.");return RouteJson.Object(RouteFiles.Load(path));}
        internal static double Number(object value){return RouteJson.Number(new Dictionary<string,object>{{"v",value}},"v");}
    }
}
