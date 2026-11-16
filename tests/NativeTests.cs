using System;
using System.Collections.Generic;
using System.Drawing;
using WoWProbe;

class NativeTests
{
    static int failures,checks;
    static void Check(string name, Action test)
    {
        checks++;
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    static void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected geometry rejection"); }
    static void Fails(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected input failure"); }
    static void Events(List<string> events,string expected) { Assert(String.Join(",",events.ToArray())==expected); }
    static CharacterInputSender Sender(short mapping,List<string> events,Action<ushort,bool> keys=null,Action<char,bool> unicode=null,Action<int> wait=null)
    {
        return new CharacterInputSender(ch=>mapping,(key,up)=>{events.Add(key+(up?"U":"D"));if(keys!=null)keys(key,up);},
            (ch,up)=>{events.Add(((int)ch)+(up?"u":"d"));if(unicode!=null)unicode(ch,up);},ms=>{if(wait!=null)wait(ms);});
    }
    static int Main()
    {
        Check("center converted to exact full grid", delegate {
            Assert(Native.CaptureGeometry(new Point(12, 12), 8, new Rectangle(0, 0, 1920, 1080)) == new Rectangle(8, 8, 256, 96));
        });
        Check("negative monitor coordinates remain valid", delegate {
            Assert(Native.CaptureGeometry(new Point(-1888, 30), 8, new Rectangle(-1920, 0, 3840, 1080)) == new Rectangle(-1892, 26, 256, 96));
        });
        Check("partial offscreen grid rejected", delegate {
            Reject(delegate { Native.CaptureGeometry(new Point(1900, 20), 8, new Rectangle(0, 0, 1920, 1080)); });
        });
        Check("invalid cell size rejected", delegate {
            Reject(delegate { Native.CaptureGeometry(new Point(20, 20), 0, new Rectangle(0, 0, 1920, 1080)); });
        });
        Check("Win32 INPUT layout matches process bitness", delegate {
            Assert(Native.InputStructureSize == (IntPtr.Size == 8 ? 40 : 28));
        });
        Check("ASCII character uses the mapped virtual key and releases once",delegate {
            var events=new List<string>();Sender(65,events).Send('a');Events(events,"65D,65U");
        });
        Check("shifted character releases the letter before releasing shift",delegate {
            var events=new List<string>();Sender(321,events).Send('A');Events(events,"16D,65D,65U,16U");
        });
        Check("partial ASCII down failure still attempts character release",delegate {
            var events=new List<string>();var sender=Sender(65,events,(key,up)=>{if(!up)throw new InvalidOperationException("partial down");});
            Fails(()=>sender.Send('a'));Events(events,"65D,65U");
        });
        Check("partial shift down failure still releases shift and never presses the letter",delegate {
            var events=new List<string>();var sender=Sender(321,events,(key,up)=>{if(key==16&&!up)throw new InvalidOperationException("partial shift down");});
            Fails(()=>sender.Send('A'));Events(events,"16D,16U");
        });
        Check("failed shifted letter down releases both attempted keys",delegate {
            var events=new List<string>();var sender=Sender(321,events,(key,up)=>{if(key==65&&!up)throw new InvalidOperationException("partial letter down");});
            Fails(()=>sender.Send('A'));Events(events,"16D,65D,65U,16U");
        });
        Check("interrupted character delay releases the letter and shift",delegate {
            var events=new List<string>();var sender=Sender(321,events,null,null,ms=>{throw new InvalidOperationException("interrupted");});
            Fails(()=>sender.Send('A'));Events(events,"16D,65D,65U,16U");
        });
        Check("character release failure still attempts shift release",delegate {
            var events=new List<string>();var sender=Sender(321,events,(key,up)=>{if(key==65&&up)throw new InvalidOperationException("release failed");});
            Fails(()=>sender.Send('A'));Events(events,"16D,65D,65U,16U");
        });
        Check("unmapped character sends separate Unicode down and up events",delegate {
            var events=new List<string>();Sender(-1,events).Send('\u4e2d');Events(events,"20013d,20013u");
        });
        Check("partial Unicode down failure still attempts the matching Unicode release",delegate {
            var events=new List<string>();var sender=Sender(-1,events,null,(ch,up)=>{if(!up)throw new InvalidOperationException("partial Unicode down");});
            Fails(()=>sender.Send('\u4e2d'));Events(events,"20013d,20013u");
        });
        Check("Unicode release failure propagates instead of reporting accepted input",delegate {
            var events=new List<string>();var sender=Sender(-1,events,null,(ch,up)=>{if(up)throw new InvalidOperationException("Unicode release failed");});
            Fails(()=>sender.SendUnicode('\u4e2d'));Events(events,"20013d,20013u");
        });
        Console.WriteLine("Native boundary tests: " + (checks - failures) + "/" + checks + " passed");
        return failures == 0 ? 0 : 1;
    }
}
