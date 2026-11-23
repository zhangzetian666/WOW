using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WoWProbe
{
    // Candidate detection only: a colored bar is never proof of target identity.
    // The caller must confirm readable mouseover == locked target before clicking.
    public static class VisualTargetBars
    {
        static bool Colored(byte[] pixels,int at)
        {
            int b=pixels[at],g=pixels[at+1],r=pixels[at+2];
            return r>=65&&g>=55&&Math.Min(r,g)*10>Math.Max(r,g)*6&&b*10<Math.Min(r,g)*6
                ||r>=85&&g*10<r*6&&b*10<r*6;
        }
        public static List<Rectangle> Find(Bitmap source)
        {
            var found=new List<Rectangle>();if(source==null)return found;
            using(var image=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb))
            {
                using(var g=Graphics.FromImage(image))g.DrawImageUnscaled(source,0,0);
                var data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                try
                {
                    int stride=data.Stride;var pixels=new byte[stride*image.Height];Marshal.Copy(data.Scan0,pixels,0,pixels.Length);
                    int minWidth=Math.Max(35,image.Width/50),maxWidth=Math.Min(350,image.Width/4);
                    for(int y=image.Height/6;y<image.Height*4/5;y+=2)
                    {
                        for(int x=4;x<image.Width-4;)
                        {
                            if(!Colored(pixels,y*stride+x*4)){x++;continue;}
                            int start=x;while(x<image.Width-4&&Colored(pixels,y*stride+x*4))x++;
                            int width=x-start;if(width<minWidth||width>maxWidth)continue;
                            int center=start+width/2;
                            if(!Colored(pixels,(y+2)*stride+center*4))continue;
                            bool duplicate=false;foreach(var old in found)if(Math.Abs(old.Y-y)<14&&Math.Abs(old.X+old.Width/2-center)<25){duplicate=true;break;}
                            if(!duplicate)found.Add(new Rectangle(start,y,width,4));
                        }
                    }
                }
                finally{image.UnlockBits(data);}
                double cx=image.Width*.5,cy=image.Height*.5;
                found.Sort((a,b)=>Distance(a,cx,cy).CompareTo(Distance(b,cx,cy)));
                if(found.Count>16)found.RemoveRange(16,found.Count-16);
            }
            return found;
        }
        static double Distance(Rectangle a,double x,double y){double dx=a.X+a.Width*.5-x,dy=a.Y-y;return dx*dx+dy*dy;}
        public static bool HoverMatches(FarmObservation o,uint token)
        {
            return token!=0&&o!=null&&o.Fresh&&o.KnownCore&&o.ReadFailures==0&&!o.PlayerDead&&!o.PlayerGhost
                &&o.TargetExists&&o.TargetIdentityKnown&&o.TargetToken==token&&o.TargetHoverKnown&&o.TargetHovered
                &&o.ChatOpenKnown&&!o.ChatOpen&&o.AttackabilityKnown&&o.TargetCanAttack&&!o.TargetIsPlayer
                &&o.TargetDeadKnown&&!o.TargetDead&&!QuickCombat.Contested(o);
        }
    }
}
