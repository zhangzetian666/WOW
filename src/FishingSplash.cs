using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace WoWProbe
{
    // Frame-local visual evidence; images never leave memory or write to disk.
    public sealed class FishingSplashDetector
    {
        const int Side=192,Patch=24;readonly double sensitivity;float[] previous;double mean=.001,variance=.000001;long cast=-1,last=-1;int frames,hits,anchorFrames,dipHits;bool fired;
        double anchorX,anchorY,anchorHeight,hitX,hitY;
        public long LastBiteAtMs=-1;public double Confidence,MotionFraction,WhiteChange,LocalWhiteChange,Threshold,BobberDrop;public bool Ready;
        public bool TrackingBobber{get{return anchorFrames>=4;}}public string Evidence="";
        public FishingSplashDetector(double sensitivity){this.sensitivity=sensitivity;}
        public void Reset(){previous=null;cast=last=-1;frames=hits=anchorFrames=dipHits=0;fired=Ready=false;LastBiteAtMs=-1;Confidence=MotionFraction=WhiteChange=LocalWhiteChange=Threshold=BobberDrop=0;Evidence="";mean=.001;variance=.000001;}
        public void Push(Bitmap image,FishingRegion region,long now,long castStarted,bool fishing)
        {
            Ready=false;if(image==null||region==null||!region.Valid||now<0)return;
            if(castStarted!=cast){Reset();cast=castStarted;}
            if(last>=0&&(now<=last||now-last>600)){previous=null;frames=hits=anchorFrames=dipHits=0;}last=now;
            var rect=new Rectangle((int)(image.Width*region.X),(int)(image.Height*region.Y),Math.Max(1,(int)(image.Width*region.Width)),Math.Max(1,(int)(image.Height*region.Height)));
            if(!new Rectangle(Point.Empty,image.Size).Contains(rect))return;
            using(var small=new Bitmap(Side,Side))
            {
                using(var g=Graphics.FromImage(small)){g.InterpolationMode=InterpolationMode.Bilinear;g.DrawImage(image,new Rectangle(0,0,Side,Side),rect,GraphicsUnit.Pixel);}
                var pixels=new float[Side*Side];var red=new bool[pixels.Length];var white=new int[(Side+1)*(Side+1)];var motion=new int[white.Length];int changes=0,whites=0;
                var bytes=new byte[Side*Side*4];var bits=small.LockBits(new Rectangle(0,0,Side,Side),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                try{for(int y=0;y<Side;y++)Marshal.Copy(IntPtr.Add(bits.Scan0,y*bits.Stride),bytes,y*Side*4,Side*4);}finally{small.UnlockBits(bits);}
                for(int y=0;y<Side;y++)for(int x=0;x<Side;x++)
                {
                    int i=y*Side+x,b=bytes[i*4],g=bytes[i*4+1],r=bytes[i*4+2];float value=(r*3+g*6+b)/10f;pixels[i]=value;
                    red[i]=r>=45&&r>g*1.15+8&&r>b*1.08+5&&r-g>=12;
                    double diff=previous==null?0:value-previous[i];int moved=Math.Abs(diff)>24?1:0;
                    int bright=diff>24&&Math.Min(r,Math.Min(g,b))>110&&Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<90?1:0;
                    changes+=moved;whites+=bright;int p=(y+1)*(Side+1)+x+1;
                    white[p]=bright+white[p-1]+white[p-Side-1]-white[p-Side-2];motion[p]=moved+motion[p-1]+motion[p-Side-1]-motion[p-Side-2];
                }
                Ready=true;frames++;MotionFraction=changes/(double)pixels.Length;WhiteChange=whites/(double)pixels.Length;
                bool armed=fishing&&cast>=0&&now-cast>=1500&&frames>=6&&!fired&&MotionFraction<.35;
                if(MotionFraction>=.35){anchorFrames=dipHits=hits=0;frames=0;}
                double bx,by,bh;bool found=FindBobber(red,out bx,out by,out bh);BobberDrop=0;
                if(found&&MotionFraction<.35){
                    if(anchorFrames==0||!TrackingBobber&&Math.Abs(bx-anchorX)+Math.Abs(by-anchorY)>8){anchorX=bx;anchorY=by;anchorHeight=bh;anchorFrames=1;dipHits=0;}
                    else if(!TrackingBobber){anchorX=(anchorX*anchorFrames+bx)/(anchorFrames+1);anchorY=(anchorY*anchorFrames+by)/(anchorFrames+1);anchorHeight=Math.Max(anchorHeight,bh);anchorFrames++;}
                    else{
                        BobberDrop=by-anchorY;double drop=Math.Max(2.5,anchorHeight*.45)*Math.Max(.8,sensitivity/3.5);
                        bool down=armed&&BobberDrop>=drop&&Math.Abs(bx-anchorX)<=Math.Max(8,anchorHeight*2);
                        if(down){dipHits++;if(dipHits>=2)Fire(now,"浮标下沉",.9);}
                        else{dipHits=0;if(Math.Abs(BobberDrop)<drop*.6&&Math.Abs(bx-anchorX)<8){anchorX=anchorX*.97+bx*.03;anchorY=anchorY*.97+by*.03;}}
                    }
                }else dipHits=0;
                int peak=0,px=0,py=0;
                for(int y=0;y<=Side-Patch;y+=Patch/2)for(int x=0;x<=Side-Patch;x+=Patch/2){
                    if(TrackingBobber&&(Math.Abs(x+Patch/2-anchorX)>Patch*1.5||Math.Abs(y+Patch/2-anchorY)>Patch*1.5))continue;
                    int n=Sum(white,x,y,Patch);if(n>peak){peak=n;px=x;py=y;}}
                LocalWhiteChange=peak/(double)(Patch*Patch);Threshold=Math.Max(.016,mean+sensitivity*Math.Sqrt(Math.Max(.000001,variance)));
                bool candidate=armed&&!fired&&LocalWhiteChange>Threshold;
                // A local onset must be followed by motion in the same small area.
                if(candidate){if(hits==0||Math.Abs(px-hitX)+Math.Abs(py-hitY)<=Patch*2)hits++;else hits=1;hitX=px;hitY=py;}
                else if(hits>0&&armed&&Sum(motion,(int)hitX,(int)hitY,Patch)>=5)hits++;else hits=0;
                if(hits>=2&&!fired)Fire(now,"局部水花",Math.Min(.99,.8+LocalWhiteChange));
                if(!candidate&&MotionFraction<.35){double delta=LocalWhiteChange-mean;mean+=delta*.08;variance=.92*(variance+.08*delta*delta);}
                previous=pixels;
            }
        }
        void Fire(long now,string source,double confidence){fired=true;LastBiteAtMs=now;Confidence=confidence;Evidence=source;}
        static int Sum(int[] integral,int x,int y,int size){int s=Side+1;return integral[(y+size)*s+x+size]-integral[y*s+x+size]-integral[(y+size)*s+x]+integral[y*s+x];}
        bool FindBobber(bool[] mask,out double x,out double y,out double height)
        {
            x=y=height=0;double best=0;var queue=new int[mask.Length];
            for(int start=0;start<mask.Length;start++)if(mask[start]){
                int read=0,count=1,left=Side,right=0,top=Side,bottom=0;double sx=0,sy=0;queue[0]=start;mask[start]=false;
                while(read<count){int p=queue[read++],cx=p%Side,cy=p/Side;sx+=cx;sy+=cy;left=Math.Min(left,cx);right=Math.Max(right,cx);top=Math.Min(top,cy);bottom=Math.Max(bottom,cy);
                    if(cx>0)Enqueue(mask,queue,ref count,p-1);if(cx<Side-1)Enqueue(mask,queue,ref count,p+1);if(cy>0)Enqueue(mask,queue,ref count,p-Side);if(cy<Side-1)Enqueue(mask,queue,ref count,p+Side);}
                if(count<3||count>900||right-left>48||bottom-top>48)continue;double cxMean=sx/count,cyMean=sy/count;
                double dx=cxMean-(TrackingBobber?anchorX:Side*.5),dy=cyMean-(TrackingBobber?anchorY:Side*.5),distance=dx*dx+dy*dy;
                if(TrackingBobber&&distance>36*36)continue;double score=count/(1+distance/144);
                if(score>best){best=score;x=cxMean;y=cyMean;height=bottom-top+1;}
            }return best>0;
        }
        static void Enqueue(bool[] mask,int[] queue,ref int count,int p){if(mask[p]){mask[p]=false;queue[count++]=p;}}
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
