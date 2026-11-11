using System;
using System.Drawing;
using WoWProbe;

class NativeTests
{
    static int failures;
    static void Check(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    static void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected geometry rejection"); }
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
        Console.WriteLine("Native boundary tests: " + (5 - failures) + "/5 passed");
        return failures == 0 ? 0 : 1;
    }
}
