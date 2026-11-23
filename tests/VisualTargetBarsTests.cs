using System;
using System.Drawing;
using WoWProbe;
class VisualTargetBarsTests
{
    static void Check(bool ok){if(!ok)throw new Exception("Visual target assertion failed");}
    static int Main()
    {
        try
        {
            using(var b=new Bitmap(1200,800))using(var g=Graphics.FromImage(b))
            {
                g.Clear(Color.DarkSlateGray);
                using(var yellow=new SolidBrush(Color.FromArgb(112,109,12)))g.FillRectangle(yellow,520,350,110,8);
                g.FillRectangle(Brushes.DarkRed,800,280,95,8);
                g.FillRectangle(Brushes.Yellow,100,45,100,8); // Excluded telemetry/top UI.
                g.FillRectangle(Brushes.Yellow,100,700,100,8); // Excluded action bar.
                g.FillRectangle(Brushes.Yellow,50,200,12,4); // Text-sized fragment.
                var bars=VisualTargetBars.Find(b);Check(bars.Count==2);Check(bars[0].Contains(575,352));
            }
            var o=new FarmObservation{Fresh=true,KnownCore=true,TargetExists=true,TargetIdentityKnown=true,TargetToken=44,TargetHoverKnown=true,TargetHovered=true,ChatOpenKnown=true,AttackabilityKnown=true,TargetCanAttack=true,TargetDeadKnown=true};
            Check(VisualTargetBars.HoverMatches(o,44));Check(!VisualTargetBars.HoverMatches(o,45));
            o.TargetHoverKnown=false;Check(!VisualTargetBars.HoverMatches(o,44));o.TargetHoverKnown=true;
            o.TargetHovered=false;Check(!VisualTargetBars.HoverMatches(o,44));o.TargetHovered=true;
            o.ChatOpen=true;Check(!VisualTargetBars.HoverMatches(o,44));o.ChatOpen=false;
            o.TargetDead=true;Check(!VisualTargetBars.HoverMatches(o,44));o.TargetDead=false;
            o.TargetClaimKnown=o.TargetClaimedByOther=true;Check(!VisualTargetBars.HoverMatches(o,44));
            Console.WriteLine("PASS visual candidates and target identity guards (synthetic images only)");return 0;
        }
        catch(Exception ex){Console.WriteLine(ex);return 1;}
    }
}
