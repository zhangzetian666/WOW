using System;
using System.Drawing;
using System.Drawing.Drawing2D;
namespace WoWProbe
{
    // Frame-local visual evidence; images never leave memory or write to disk.
    public sealed class FishingSplashDetector
    {
        const int Side=96;readonly double sensitivity;float[] previous;double mean=.001,variance=.000001;long cast=-1,last=-1;int frames,hits;bool fired;
        public long LastBiteAtMs=-1;public double Confidence,MotionFraction,WhiteChange;public bool Ready;
        public FishingSplashDetector(double sensitivity){this.sensitivity=sensitivity;}
        public void Reset(){previous=null;cast=last=-1;frames=hits=0;fired=Ready=false;LastBiteAtMs=-1;Confidence=0;mean=.001;variance=.000001;}
        public void Push(Bitmap image,FishingRegion region,long now,long castStarted,bool fishing)
        {
            Ready=false;if(image==null||region==null||!region.Valid||now<0)return;
            if(castStarted!=cast){Reset();cast=castStarted;}
            if(last>=0&&(now<=last||now-last>600)){previous=null;frames=hits=0;}last=now;
            var rect=new Rectangle((int)(image.Width*region.X),(int)(image.Height*region.Y),Math.Max(1,(int)(image.Width*region.Width)),Math.Max(1,(int)(image.Height*region.Height)));
            if(!new Rectangle(Point.Empty,image.Size).Contains(rect))return;
            using(var small=new Bitmap(Side,Side))
            {
                using(var g=Graphics.FromImage(small)){g.InterpolationMode=InterpolationMode.Bilinear;g.DrawImage(image,new Rectangle(0,0,Side,Side),rect,GraphicsUnit.Pixel);}
                var pixels=new float[Side*Side];int changes=0,whites=0;double sx=0,sy=0,sxx=0,syy=0;
                for(int y=0;y<Side;y++)for(int x=0;x<Side;x++)
                {
                    var c=small.GetPixel(x,y);int i=y*Side+x;float value=(c.R*3+c.G*6+c.B)/10f;pixels[i]=value;
                    if(previous==null)continue;double diff=value-previous[i];if(Math.Abs(diff)>30)changes++;
                    if(diff>35&&Math.Min(c.R,Math.Min(c.G,c.B))>150&&Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))<65)
                    {whites++;sx+=x;sy+=y;sxx+=x*x;syy+=y*y;}
                }
                Ready=true;frames++;MotionFraction=changes/(double)pixels.Length;WhiteChange=whites/(double)pixels.Length;
                double threshold=Math.Max(.006,mean+sensitivity*Math.Sqrt(Math.Max(.000001,variance)));
                double spread=whites>0?(sxx+syy)/whites-(sx*sx+sy*sy)/(whites*whites):0;
                bool candidate=fishing&&cast>=0&&now-cast>=1500&&frames>=6&&!fired&&MotionFraction<.35&&WhiteChange>threshold&&spread<Side*Side*.18;
                // Accept an onset followed by continuing localized motion, not one noisy pixel frame.
                if(candidate)hits++;else if(hits>0&&MotionFraction>.005&&MotionFraction<.35&&fishing&&now-cast>=1500)hits++;else hits=0;
                if(hits>=2){fired=true;LastBiteAtMs=now;Confidence=Math.Min(.99,.75+WhiteChange);}
                if(!candidate&&MotionFraction<.35){double delta=WhiteChange-mean;mean+=delta*.08;variance=.92*(variance+.08*delta*delta);}
                previous=pixels;
            }
        }
    }
    public sealed class FishingSocialObservation
    {
        public int Sequence;public bool Known,ChatTypeKnown,Say,Whisper,BagsKnown,BagsOpen;public uint WhisperCount,SenderHash,ReplyTargetHash;
        static uint U32(byte[] b,int i){return (uint)b[i]|(uint)b[i+1]<<8|(uint)b[i+2]<<16|(uint)b[i+3]<<24;}
        public static FishingSocialObservation Decode(byte[] b,int sequence)
        {
            if(b==null||b.Length!=128||b[0]!=87||b[1]!=70||b[2]!=49||b[3]!=1||b[4]+256*b[5]!=sequence)throw new FormatException("WF1帧头或序号无效。");
            int a=0,c=0;for(int i=0;i<126;i++){a=(a+b[i])%255;c=(c+a)%255;}if(a!=b[126]||c!=b[127])throw new FormatException("WF1校验失败。");
            if(b[6]>1||b[7]!=0&&b[7]!=1&&b[7]!=3&&b[7]!=5||b[20]!=0&&b[20]!=1&&b[20]!=3)throw new FormatException("WF1状态位无效。");
            for(int i=21;i<126;i++)if(b[i]!=0)throw new FormatException("WF1保留位无效。");
            var o=new FishingSocialObservation{Sequence=sequence,Known=b[6]==1,WhisperCount=U32(b,8),SenderHash=U32(b,12),ChatTypeKnown=(b[7]&1)!=0,Say=b[7]==3,Whisper=b[7]==5,ReplyTargetHash=U32(b,16),BagsKnown=(b[20]&1)!=0,BagsOpen=(b[20]&2)!=0};
            if(o.WhisperCount!=0&&(!o.Known||o.SenderHash==0)||o.ReplyTargetHash!=0&&!o.Whisper)throw new FormatException("WF1身份字段不一致。");return o;
        }
    }
}
