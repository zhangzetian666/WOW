using System;
using System.Drawing;
namespace WoWProbe
{
    public static class VisualVitals
    {
        // Require both colored edge markers and a single contiguous native fill.
        // This estimates a rendered bar; it never changes the WP1 capabilities.
        public static int Read(Bitmap image,int cell,int row)
        {
            double scale=cell/8.0;int y=(int)((120+18*row+5)*scale);
            int left=(int)scale,right=(int)(203*scale),start=(int)(2*scale),end=(int)(202*scale);
            if(image==null||y<0||y>=image.Height||right>=image.Width||end<=start)return 255;
            Color a=image.GetPixel(left,y),b=image.GetPixel(right,y);
            if(!(a.R>220&&a.G<40&&a.B>220&&b.R<40&&b.G>220&&b.B>220))return 255;
            int fill=0;bool empty=false;
            for(int x=start;x<end;x++)
            {
                Color c=image.GetPixel(x,y);bool colored=row==0?c.R<40&&c.G>220&&c.B<40:c.R<40&&c.G<40&&c.B>220;
                // WoW's linear UI gray is converted to sRGB by the renderer;
                // a .125 texture is about 99 on this client, rather than 32.
                bool background=c.R>=20&&c.R<=130&&Math.Abs(c.R-c.G)<9&&Math.Abs(c.R-c.B)<9;
                if(colored){if(empty)return 255;fill++;}else if(background)empty=true;else return 255;
            }
            return (int)Math.Floor(100.0*fill/(end-start));
        }
    }
}
