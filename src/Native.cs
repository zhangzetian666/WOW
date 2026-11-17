using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WoWProbe
{
    public sealed class CharacterInputSender
    {
        readonly Func<char,short> map;readonly Action<ushort,bool> sendKey;readonly Action<char,bool> sendUnicode;readonly Action<int> wait;
        public CharacterInputSender(Func<char,short> map,Action<ushort,bool> sendKey,Action<char,bool> sendUnicode,Action<int> wait)
        {
            if(map==null||sendKey==null||sendUnicode==null||wait==null)throw new ArgumentNullException("character input dependencies");
            this.map=map;this.sendKey=sendKey;this.sendUnicode=sendUnicode;this.wait=wait;
        }
        public void Send(char ch)
        {
            short mapped=map(ch);ushort key=(ushort)(mapped&255);
            if(key==0||key==255){SendUnicode(ch);return;}
            bool shift=(mapped&256)!=0;
            bool shiftAttempted=false;
            try
            {
                // A failed transport can have accepted the down event before
                // reporting failure. Record attempted ownership before sending.
                if(shift){shiftAttempted=true;sendKey(16,false);}
                try{sendKey(key,false);wait(15);}
                finally{sendKey(key,true);}
            }
            finally{if(shiftAttempted)sendKey(16,true);}
            wait(15);
        }
        public void SendUnicode(char ch)
        {try{sendUnicode(ch,false);}finally{sendUnicode(ch,true);}wait(25);}
    }

    public sealed class WindowInfo
    {
        public IntPtr Handle;
        public uint ProcessId;
        public string Title;
        public string ProcessName;
        public override string ToString() { return ProcessName + " [" + ProcessId + "]  " + Title; }
    }

    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT
        { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT
        { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTUNION
        { [FieldOffset(0)] public KEYBDINPUT Keyboard; [FieldOffset(0)] public MOUSEINPUT Mouse; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT
        { public uint Type; public INPUTUNION Data; }
        delegate bool WindowCallback(IntPtr handle, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr param);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr handle, out RECT rect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr handle, ref POINT point);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, INPUT[] input, int size);
        [DllImport("user32.dll", SetLastError=true)] static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern short VkKeyScanW(char ch);
        [DllImport("user32.dll")] static extern bool ClipCursor(IntPtr rect);
        public static int InputStructureSize { get { return Marshal.SizeOf(typeof(INPUT)); } }

        public static List<WindowInfo> Windows()
        {
            var result = new List<WindowInfo>();
            uint own = (uint)Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h, IntPtr unused) {
                if (!IsWindowVisible(h)) return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (pid == own) return true;
                var text = new StringBuilder(512); GetWindowText(h, text, text.Capacity);
                if (text.Length == 0) return true;
                string name;
                try { using (var process = Process.GetProcessById((int)pid)) name = process.ProcessName; }
                catch { return true; }
                result.Add(new WindowInfo { Handle=h, ProcessId=pid, Title=text.ToString(), ProcessName=name });
                return true;
            }, IntPtr.Zero);
            result.Sort(delegate(WindowInfo a, WindowInfo b) {
                bool aw = a.ProcessName.StartsWith("wow", StringComparison.OrdinalIgnoreCase);
                bool bw = b.ProcessName.StartsWith("wow", StringComparison.OrdinalIgnoreCase);
                if (aw != bw) return aw ? -1 : 1;
                return String.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
            });
            return result;
        }

        public static bool Matches(WindowInfo target)
        {
            if (target == null || !IsWindow(target.Handle) || IsIconic(target.Handle)) return false;
            uint pid; GetWindowThreadProcessId(target.Handle, out pid);
            return pid == target.ProcessId;
        }

        public static Rectangle ClientBounds(WindowInfo target)
        {
            if (!Matches(target)) throw new InvalidOperationException("所选窗口已关闭或最小化，请刷新后重新选择。");
            RECT r; POINT origin = new POINT();
            if (!GetClientRect(target.Handle, out r) || !ClientToScreen(target.Handle, ref origin))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取游戏窗口位置。");
            return new Rectangle(origin.X, origin.Y, r.Right-r.Left, r.Bottom-r.Top);
        }

        public static Rectangle CaptureGeometry(Point center, int cellSize, Rectangle bounds)
        {
            if (cellSize < 2 || cellSize > 32) throw new ArgumentOutOfRangeException("cellSize");
            var region = new Rectangle(center.X-cellSize/2, center.Y-cellSize/2, checked(32*cellSize), checked(12*cellSize));
            if (!bounds.Contains(region)) throw new ArgumentException("色块区域超出窗口或屏幕，请重新定位第一个色块中心。");
            return region;
        }

        public static Bitmap Capture(Rectangle region)
        {
            if (!SystemInformation.VirtualScreen.Contains(region)) throw new ArgumentException("截取区域超出屏幕。");
            var image = new Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            try { using (var g=Graphics.FromImage(image)) g.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy); }
            catch { image.Dispose(); throw; }
            return image;
        }

        public static void SendKey(ushort key, bool up)
        {
            // VK-based injection: verified working on the Forever client for W/A and
            // arrow keys. Scan-code injection silently no-ops for arrow keys here.
            uint scan = MapVirtualKey(key, 0);
            if (scan == 0) throw new ArgumentException("该按键没有可用的扫描码。");
            var input = new INPUT { Type=1, Data=new INPUTUNION { Keyboard=new KEYBDINPUT {
                Key=key, Scan=(ushort)scan, Flags=(up ? 2u : 0u), Extra=UIntPtr.Zero } } };
            if (SendInput(1, new INPUT[] {input}, InputStructureSize) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "按键未发送。请确认工具与游戏以相同权限运行。");
        }
        public static void MoveMouse(Point screen)
        {
            if(!SystemInformation.VirtualScreen.Contains(screen))throw new ArgumentException("鼠标位置超出有效屏幕。");
            if(!SetCursorPos(screen.X,screen.Y))throw new Win32Exception(Marshal.GetLastWin32Error(),"鼠标移动失败。");
        }
        public static void SendMouseButton(bool right,bool up)
        {
            var input=new INPUT{Type=0,Data=new INPUTUNION{Mouse=new MOUSEINPUT{Flags=right?(up?16u:8u):(up?4u:2u),Extra=UIntPtr.Zero}}};
            if(SendInput(1,new INPUT[]{input},InputStructureSize)!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"鼠标按钮未发送。");
        }
        public static void MoveMouseRelative(int dx,int dy)
        {
            if(Math.Abs((long)dx)>32||Math.Abs((long)dy)>32)throw new ArgumentOutOfRangeException("dx/dy");
            var input=new INPUT{Type=0,Data=new INPUTUNION{Mouse=new MOUSEINPUT{X=dx,Y=dy,Flags=0x2001u,Extra=UIntPtr.Zero}}};
            if(SendInput(1,new[]{input},InputStructureSize)!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"相对鼠标移动未发送。");
        }
        // Release any ClipCursor restriction a previous run left behind so physical
        // clicks land where the operating system (and the operator) expect them.
        public static void UnclipCursor() { try { ClipCursor(IntPtr.Zero); } catch { } }

        // Chat typing: map a character to its virtual key plus shift state, then send
        // real down/up events so the game's edit box receives them like a keyboard.
        // Characters without a layout mapping (e.g. CJK names) use KEYEVENTF_UNICODE,
        // which injects the raw code point and is accepted by the game's chat edit box.
        static readonly CharacterInputSender characterInput = new CharacterInputSender(VkKeyScanW,SendKey,SendUnicodeEvent,Thread.Sleep);
        public static void SendChar(char ch)
        {
            characterInput.Send(ch);
        }
        const uint KEYEVENTF_UNICODE=0x0004, KEYEVENTF_KEYUP=0x0002;
        public static void SendUnicodeChar(char ch)
        {
            characterInput.SendUnicode(ch);
        }
        static void SendUnicodeEvent(char ch,bool up)
        {
            var input=new INPUT{Type=1,Data=new INPUTUNION{Keyboard=new KEYBDINPUT{Key=0,Scan=(ushort)ch,Flags=KEYEVENTF_UNICODE|(up?KEYEVENTF_KEYUP:0),Extra=UIntPtr.Zero}}};
            if(SendInput(1,new[]{input},InputStructureSize)!=1)
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Unicode 字符未发送。");
        }
        public static void SendText(string text)
        {
            foreach(char ch in text)SendChar(ch);
        }
    }

    public sealed class WindowsInputBackend : IInputBackend
    {
        readonly WindowInfo target;
        readonly Action<string> log;
        public WindowsInputBackend(WindowInfo target,Action<string> log) { this.target=target;this.log=log; }
        public bool IsTargetForeground() { return Native.Matches(target) && Native.GetForegroundWindow()==target.Handle; }
        public bool IsEmergencyPressed()
        {
            Point p=Cursor.Position;
            return (Native.GetAsyncKeyState((int)Keys.F12)&0x8000)!=0 || (p.X>=0 && p.X<60 && p.Y>=0 && p.Y<60);
        }
        public bool IsKeyAlreadyDown(ushort key) { return (Native.GetAsyncKeyState(key)&0x8000)!=0; }
        public void KeyDown(ushort key) { Send(key,false); }
        public void KeyUp(ushort key) { Send(key,true); }
        void Send(ushort key,bool up)
        {
            if(log!=null)log((up?"KeyUp":"KeyDown")+" attempt VK="+key);
            try {Native.SendKey(key,up);if(log!=null)log((up?"KeyUp":"KeyDown")+" accepted by SendInput VK="+key);}
            catch(Exception ex){if(log!=null)log("SendInput failure: "+ex.Message);throw;}
        }
    }
}
