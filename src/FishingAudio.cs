using System;

namespace WoWProbe
{
    public sealed class FishingAudioPacket
    {
        public float[] Samples{get;internal set;}
        public int SampleRate{get;internal set;}
        public long EndedAtMs{get;internal set;}
        public bool Discontinuous{get;internal set;}
    }

    // Strict, native-independent conversion of an endpoint's WAVEFORMATEX and packets.
    public sealed class FishingAudioFormat
    {
        const int MaximumPacketBytes=4*1024*1024;
        static readonly Guid Pcm=new Guid("00000001-0000-0010-8000-00aa00389b71");
        static readonly Guid Float=new Guid("00000003-0000-0010-8000-00aa00389b71");
        public int SampleRate{get;private set;}
        public int Channels{get;private set;}
        public int BitsPerSample{get;private set;}
        public int BlockAlign{get;private set;}
        public bool IsFloat{get;private set;}
        FishingAudioFormat(){}
        public static FishingAudioFormat Parse(byte[] format)
        {
            if(format==null||format.Length<18||format.Length>128)throw Invalid("An endpoint format must contain a bounded WAVEFORMATEX header.");
            int tag=U16(format,0),channels=U16(format,2),align=U16(format,12),bits=U16(format,14),extra=U16(format,16);
            uint rate=U32(format,4),average=U32(format,8);
            if(extra!=format.Length-18)throw Invalid("Endpoint format length disagrees with cbSize.");
            if(channels<1||channels>32||rate<8000||rate>192000)throw Invalid("Unsupported endpoint channel count or sample rate.");
            bool floating;
            if(tag==1||tag==3)
            {
                if(extra!=0)throw Invalid("Unexpected extra data in a PCM or float endpoint format.");
                floating=tag==3;
            }
            else if(tag==65534)
            {
                if(extra<22)throw Invalid("Truncated WAVEFORMATEXTENSIBLE endpoint format.");
                var guidBytes=new byte[16];Buffer.BlockCopy(format,24,guidBytes,0,16);Guid subformat=new Guid(guidBytes);
                if(subformat!=Pcm&&subformat!=Float)throw Invalid("Unsupported extensible endpoint subformat.");
                floating=subformat==Float;
                int validBits=U16(format,18);
                if(validBits<1||validBits>bits||(floating&&validBits!=bits))throw Invalid("Invalid valid-bit count in endpoint format.");
            }
            else throw Invalid("Only uncompressed PCM and IEEE float playback formats are supported.");
            if(floating?(bits!=32&&bits!=64):(bits!=16&&bits!=24&&bits!=32))throw Invalid("Unsupported endpoint sample width.");
            if(align!=channels*(bits/8)||(long)rate*align!=average)throw Invalid("Endpoint frame alignment or average byte rate is inconsistent.");
            return new FishingAudioFormat{SampleRate=(int)rate,Channels=channels,BitsPerSample=bits,BlockAlign=align,IsFloat=floating};
        }
        public int RequiredByteCount(int frames)
        {
            if(frames<0||frames>SampleRate*2)throw Invalid("Audio packet frame count exceeds its bound.");
            long bytes=(long)frames*BlockAlign;if(bytes>MaximumPacketBytes)throw Invalid("Audio packet exceeds four MiB.");return (int)bytes;
        }
        public float[] Decode(byte[] bytes,int frames,bool silent)
        {
            int expected=RequiredByteCount(frames);
            if(!silent&&(bytes==null||bytes.Length!=expected))throw Invalid("Audio packet bytes disagree with its complete frame count.");
            var mono=new float[frames];if(silent)return mono;
            int sampleBytes=BitsPerSample/8;
            for(int frame=0;frame<frames;frame++)
            {
                double sum=0;int start=frame*BlockAlign;
                for(int channel=0;channel<Channels;channel++)
                {
                    int offset=start+channel*sampleBytes;double sample;
                    if(IsFloat)
                    {
                        sample=BitsPerSample==32?BitConverter.ToSingle(bytes,offset):BitConverter.ToDouble(bytes,offset);
                        if(Double.IsNaN(sample)||Double.IsInfinity(sample))throw Invalid("A nonfinite audio sample cannot be used for recognition.");
                        sample=Math.Max(-1,Math.Min(1,sample));
                    }
                    else if(BitsPerSample==16)sample=(short)U16(bytes,offset)/32768.0;
                    else if(BitsPerSample==24)
                    {
                        int value=bytes[offset]|bytes[offset+1]<<8|bytes[offset+2]<<16;
                        if((value&0x800000)!=0)value|=unchecked((int)0xff000000);sample=value/8388608.0;
                    }
                    else sample=unchecked((int)U32(bytes,offset))/2147483648.0;
                    sum+=sample;
                }
                mono[frame]=(float)(sum/Channels);
            }
            return mono;
        }
        public FishingAudioPacket DecodePacket(byte[] bytes,int frames,uint flags,long endedAtMs)
        {
            if((flags&~7u)!=0||endedAtMs<0)throw Invalid("Invalid audio packet flags or monotonic packet time.");
            return new FishingAudioPacket{Samples=Decode(bytes,frames,(flags&2)!=0),SampleRate=SampleRate,EndedAtMs=endedAtMs,Discontinuous=(flags&5)!=0};
        }
        static int U16(byte[] bytes,int at){return bytes[at]|bytes[at+1]<<8;}
        static uint U32(byte[] bytes,int at){return (uint)bytes[at]|(uint)bytes[at+1]<<8|(uint)bytes[at+2]<<16|(uint)bytes[at+3]<<24;}
        static FormatException Invalid(string message){return new FormatException(message);}
    }
}
