using System;
using System.Drawing;
using WoWProbe;
class VisualVitalsTests
{
    static int passed,failed;
    static void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static void Check(bool b){if(!b)throw new Exception("Unexpected visual vital reading");}
    static Bitmap Bar(int percent,int gray)
    {
        var b=new Bitmap(544,160);using(var g=Graphics.FromImage(b)){g.Clear(Color.Black);g.FillRectangle(Brushes.Magenta,0,120,2,10);g.FillRectangle(Brushes.Cyan,202,120,2,10);using(var bg=new SolidBrush(Color.FromArgb(gray,gray,gray)))g.FillRectangle(bg,2,120,200,10);g.FillRectangle(Brushes.Lime,2,120,percent*2,10);}return b;
    }
    static int Main()
    {
        Test("full and empty bars retain real boundary percentages",()=>{using(var b=Bar(100,99))Check(VisualVitals.Read(b,8,0)==100);using(var b=Bar(0,99))Check(VisualVitals.Read(b,8,0)==0);});
        Test("linear and sRGB gray backgrounds both decode decreasing health",()=>{foreach(int gray in new[]{32,99})using(var b=Bar(73,gray))Check(VisualVitals.Read(b,8,0)==73);});
        Test("marker loss rejects a covered bar instead of inventing full health",()=>{using(var b=Bar(80,99)){b.SetPixel(1,125,Color.Black);Check(VisualVitals.Read(b,8,0)==255);}});
        Test("noncontiguous fill cannot turn unrelated green pixels into health",()=>{using(var b=Bar(40,99)){b.SetPixel(150,125,Color.Lime);Check(VisualVitals.Read(b,8,0)==255);}});
        Test("colored occlusion and missing image retain unknown",()=>{using(var b=Bar(40,99)){b.SetPixel(150,125,Color.Red);Check(VisualVitals.Read(b,8,0)==255);}Check(VisualVitals.Read(null,8,0)==255);});
        Console.WriteLine("Visual vitals tests: "+passed+" passed, "+failed+" failed. Synthetic pixels only.");return failed==0?0:1;
    }
}
