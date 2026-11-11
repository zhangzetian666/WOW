using System;
using System.Drawing;

namespace WoWProbe
{
    public sealed class WindowsCampaignInputBackend:ICampaignInputBackend
    {
        readonly WindowInfo target;readonly WindowsInputBackend keys;readonly Action<string> log;
        public WindowsCampaignInputBackend(WindowInfo target,Action<string> log){if(target==null)throw new ArgumentNullException("target");this.target=target;this.log=log;keys=new WindowsInputBackend(target,log);}
        public Rectangle ClientBounds(){return Native.ClientBounds(target);}
        public bool IsTargetForeground(){return keys.IsTargetForeground();}public bool IsEmergencyPressed(){return keys.IsEmergencyPressed();}
        public bool IsKeyAlreadyDown(ushort k){return keys.IsKeyAlreadyDown(k);}public void KeyDown(ushort k){keys.KeyDown(k);}public void KeyUp(ushort k){keys.KeyUp(k);}
        public bool IsMouseAlreadyDown(bool right){return (Native.GetAsyncKeyState(right?2:1)&0x8000)!=0;}
        public void MoveMouse(Point screen){Native.MoveMouse(screen);}
        public void MouseDown(bool right){Send(right,false);}public void MouseUp(bool right){Send(right,true);}
        void Send(bool right,bool up){Native.SendMouseButton(right,up);if(log!=null)log("Mouse "+(right?"right":"left")+" "+(up?"up":"down")+" accepted.");}
    }
}
