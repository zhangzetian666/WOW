using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace WoWProbe
{
    // Captures the default playback endpoint's shared mix, never a microphone.
    // COM activation, packet reads, Stop and COM release stay on one MTA worker.
    public sealed class WindowsFishingAudioCapture:IDisposable
    {
        readonly object gate=new object();readonly Action<float[],int,long,bool> samples;readonly Func<long> clock;
        readonly ManualResetEvent stop=new ManualResetEvent(false);
        Thread worker;bool running,disposed,signalClosed;long lastPacketMs=-1;string error="";
        public WindowsFishingAudioCapture(Action<float[],int,long,bool> samples,Func<long> clock)
        {if(samples==null||clock==null)throw new ArgumentNullException("audio capture dependencies");this.samples=samples;this.clock=clock;}
        public bool Running{get{lock(gate)return running;}}
        public long LastPacketMs{get{lock(gate)return lastPacketMs;}}
        public string Error{get{lock(gate)return error;}}
        public void Start()
        {
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("WindowsFishingAudioCapture");
                if(worker!=null)throw new InvalidOperationException("Audio capture is already starting, running or stopping.");
                stop.Reset();error="";lastPacketMs=-1;running=false;
                worker=new Thread(Capture){IsBackground=true,Name="WoWProbe playback audio"};worker.SetApartmentState(ApartmentState.MTA);
                try{worker.Start();}catch{worker=null;throw;}
            }
        }
        public void Stop()
        {
            Thread thread;
            lock(gate){thread=worker;running=false;lastPacketMs=-1;if(thread!=null)stop.Set();}
            if(thread!=null&&thread!=Thread.CurrentThread&&!thread.Join(5000))
            {lock(gate)if(error.Length==0)error="Audio capture has not finished stopping; it cannot be restarted yet.";}
            CloseSignalIfStopped();
        }
        public void Dispose()
        {lock(gate){if(disposed)return;disposed=true;}Stop();CloseSignalIfStopped();}
        void CloseSignalIfStopped()
        {lock(gate){if(disposed&&worker==null&&!signalClosed){signalClosed=true;stop.Close();}}}
        bool Stopping(){return stop.WaitOne(0);}
        void SetError(Exception ex){lock(gate){error=ex.GetType().Name+": "+ex.Message;running=false;lastPacketMs=-1;}}
        bool Publish(FishingAudioPacket packet)
        {
            if(Stopping())return false;
            try{samples(packet.Samples,packet.SampleRate,packet.EndedAtMs,packet.Discontinuous);}
            catch(Exception ex){SetError(new InvalidOperationException("Audio sample consumer failed.",ex));stop.Set();return false;}
            lock(gate){if(!Stopping()){lastPacketMs=packet.Discontinuous?-1:packet.EndedAtMs;return true;}}
            return false;
        }
        void Capture()
        {
            IMMDeviceEnumerator enumerator=null;IMMDevice device=null;IAudioClient client=null;IAudioCaptureClient capture=null;
            IntPtr mixFormat=IntPtr.Zero;bool initialized=false,started=false;
            try
            {
                Check(CoInitializeEx(IntPtr.Zero,0));initialized=true;if(Stopping())return;
                Type type=Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"),true);
                enumerator=(IMMDeviceEnumerator)Activator.CreateInstance(type);
                Check(enumerator.GetDefaultAudioEndpoint(0,0,out device));string endpoint=DeviceId(device);
                Guid audioClientId=new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");object service;
                Check(device.Activate(ref audioClientId,23,IntPtr.Zero,out service));client=(IAudioClient)service;
                Check(client.GetMixFormat(out mixFormat));if(mixFormat==IntPtr.Zero)throw new FormatException("Playback endpoint returned no audio format.");
                int extra=(ushort)Marshal.ReadInt16(mixFormat,16);if(extra>110)throw new FormatException("Playback endpoint audio format exceeds its bound.");
                var formatBytes=new byte[18+extra];Marshal.Copy(mixFormat,formatBytes,0,formatBytes.Length);FishingAudioFormat format=FishingAudioFormat.Parse(formatBytes);
                // AUDCLNT_SHAREMODE_SHARED + AUDCLNT_STREAMFLAGS_LOOPBACK, 100 ms buffer.
                Check(client.Initialize(0,0x00020000,1000000,0,mixFormat,IntPtr.Zero));Marshal.FreeCoTaskMem(mixFormat);mixFormat=IntPtr.Zero;
                Guid captureClientId=new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");Check(client.GetService(ref captureClientId,out service));capture=(IAudioCaptureClient)service;
                if(Stopping())return;Check(client.Start());started=true;
                lock(gate){if(!Stopping())running=true;}
                var deviceCheck=Stopwatch.StartNew();long lastClock=-1;
                while(!Stopping())
                {
                    uint available;Check(capture.GetNextPacketSize(out available));
                    while(available>0&&!Stopping())
                    {
                        IntPtr data;uint frames,flags;ulong position,qpc;
                        Check(capture.GetBuffer(out data,out frames,out flags,out position,out qpc));
                        if(frames==0)break;
                        FishingAudioPacket packet;
                        try
                        {
                            if(frames>Int32.MaxValue)throw new FormatException("Playback packet frame count is invalid.");
                            int count=format.RequiredByteCount((int)frames);byte[] bytes=null;
                            if((flags&2)==0){if(data==IntPtr.Zero)throw new FormatException("Non-silent playback packet has no sample buffer.");bytes=new byte[count];Marshal.Copy(data,bytes,0,count);}
                            long now=clock();if(now<0||now<lastClock)throw new InvalidOperationException("Audio capture monotonic clock moved backwards.");lastClock=now;
                            long endedAt=PacketEnd(now,qpc,(int)frames,format.SampleRate,ref flags);
                            packet=format.DecodePacket(bytes,(int)frames,flags,endedAt);
                        }
                        finally{Check(capture.ReleaseBuffer(frames));}
                        // The endpoint buffer is always released before invoking external code.
                        if(!Publish(packet))return;
                        Check(capture.GetNextPacketSize(out available));
                    }
                    if(deviceCheck.ElapsedMilliseconds>=1000)
                    {
                        IMMDevice current=null;
                        try{Check(enumerator.GetDefaultAudioEndpoint(0,0,out current));if(!String.Equals(endpoint,DeviceId(current),StringComparison.Ordinal))throw new InvalidOperationException("Default playback device changed. Stop and restart audio capture.");}
                        finally{Release(current);}
                        deviceCheck.Restart();
                    }
                    stop.WaitOne(20);
                }
            }
            catch(Exception ex){if(!Stopping())SetError(ex);}
            finally
            {
                if(started&&client!=null)try{client.Stop();}catch{}
                if(mixFormat!=IntPtr.Zero)Marshal.FreeCoTaskMem(mixFormat);
                Release(capture);Release(client);Release(device);Release(enumerator);
                if(initialized)CoUninitialize();
                lock(gate){running=false;lastPacketMs=-1;if(worker==Thread.CurrentThread)worker=null;}
                CloseSignalIfStopped();
            }
        }
        static long PacketEnd(long now,ulong packetStartQpc,int frames,int rate,ref uint flags)
        {
            if((flags&4)!=0)return now;
            double current100ns=Stopwatch.GetTimestamp()*(10000000.0/Stopwatch.Frequency);
            double end100ns=packetStartQpc+frames*(10000000.0/rate),ageMs=(current100ns-end100ns)/10000.0;
            if(packetStartQpc==0||ageMs< -100||ageMs>60000||Double.IsNaN(ageMs)||Double.IsInfinity(ageMs)){flags|=4;return now;}
            return Math.Max(0,now-(long)Math.Ceiling(Math.Max(0,ageMs)));
        }
        static string DeviceId(IMMDevice device)
        {
            if(device==null)throw new InvalidOperationException("No default playback endpoint is available.");IntPtr id=IntPtr.Zero;
            try{Check(device.GetId(out id));if(id==IntPtr.Zero)throw new InvalidOperationException("Playback endpoint has no identity.");return Marshal.PtrToStringUni(id);}
            finally{if(id!=IntPtr.Zero)Marshal.FreeCoTaskMem(id);}
        }
        static void Check(int result){if(result<0)Marshal.ThrowExceptionForHR(result);}
        static void Release(object value){if(value!=null&&Marshal.IsComObject(value))try{Marshal.FinalReleaseComObject(value);}catch{}}
        [DllImport("ole32.dll")]static extern int CoInitializeEx(IntPtr reserved,uint coInit);
        [DllImport("ole32.dll")]static extern void CoUninitialize();

        [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig]int EnumAudioEndpoints(int flow,uint stateMask,out IntPtr devices);
            [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IMMDevice device);
            [PreserveSig]int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IMMDevice device);
            [PreserveSig]int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig]int UnregisterEndpointNotificationCallback(IntPtr client);
        }
        [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig]int Activate(ref Guid iid,uint context,IntPtr activationParameters,[MarshalAs(UnmanagedType.IUnknown)]out object value);
            [PreserveSig]int OpenPropertyStore(uint access,out IntPtr properties);
            [PreserveSig]int GetId(out IntPtr id);
            [PreserveSig]int GetState(out uint state);
        }
        [ComImport,Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioClient
        {
            [PreserveSig]int Initialize(int shareMode,uint flags,long bufferDuration,long periodicity,IntPtr format,IntPtr sessionGuid);
            [PreserveSig]int GetBufferSize(out uint frames);
            [PreserveSig]int GetStreamLatency(out long latency);
            [PreserveSig]int GetCurrentPadding(out uint frames);
            [PreserveSig]int IsFormatSupported(int shareMode,IntPtr format,out IntPtr closestMatch);
            [PreserveSig]int GetMixFormat(out IntPtr format);
            [PreserveSig]int GetDevicePeriod(out long defaultPeriod,out long minimumPeriod);
            [PreserveSig]int Start();
            [PreserveSig]int Stop();
            [PreserveSig]int Reset();
            [PreserveSig]int SetEventHandle(IntPtr handle);
            [PreserveSig]int GetService(ref Guid iid,[MarshalAs(UnmanagedType.IUnknown)]out object value);
        }
        [ComImport,Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioCaptureClient
        {
            [PreserveSig]int GetBuffer(out IntPtr data,out uint frames,out uint flags,out ulong devicePosition,out ulong qpcPosition);
            [PreserveSig]int ReleaseBuffer(uint frames);
            [PreserveSig]int GetNextPacketSize(out uint frames);
        }
    }
}
