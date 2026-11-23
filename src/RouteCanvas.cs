using System;
using System.Drawing;
using System.Windows.Forms;
namespace WoWProbe
{
    public sealed class RouteCanvas : Control
    {
        public RouteDocument Route;
        public PointF? Player;
        public int Highlight;
        public double AxisScale=1;
        public RouteCanvas(){DoubleBuffered=true;BackColor=Color.White;}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);if(Route==null||Route.Points.Count==0)return;
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            double minX=1,maxX=0,minY=1,maxY=0;
            foreach(var p in Route.Points){minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);minY=Math.Min(minY,p.Y);maxY=Math.Max(maxY,p.Y);}
            if(Player.HasValue){minX=Math.Min(minX,Player.Value.X);maxX=Math.Max(maxX,Player.Value.X);minY=Math.Min(minY,Player.Value.Y);maxY=Math.Max(maxY,Player.Value.Y);}
            double spanX=Math.Max(.005,(maxX-minX)*AxisScale),spanY=Math.Max(.005,maxY-minY);double factor=Math.Min(Math.Max(1,Width-70)/spanX,Math.Max(1,Height-70)/spanY);
            Func<double,double,PointF> point=delegate(double x,double y){return new PointF((float)(35+(x-minX)*AxisScale*factor),(float)(35+(y-minY)*factor));};
            using(var line=new Pen(Color.FromArgb(70,105,170),2))
            {
                for(int i=1;i<Route.Points.Count;i++){var a=Route.Points[i-1];var b=Route.Points[i];e.Graphics.DrawLine(line,point(a.X,a.Y),point(b.X,b.Y));}
                for(int i=0;i<Route.Points.Count;i++){var p=point(Route.Points[i].X,Route.Points[i].Y);e.Graphics.FillEllipse(i==Highlight?Brushes.DarkOrange:Brushes.SteelBlue,p.X-4,p.Y-4,8,8);if(Route.Points.Count<100)e.Graphics.DrawString((i+1).ToString(),Font,Brushes.Black,p.X+5,p.Y+2);}
                if(Highlight>=0&&Highlight<Route.Points.Count){var t=Route.Points[Highlight];var selected=point(t.X,t.Y);e.Graphics.FillEllipse(Brushes.DarkOrange,selected.X-5,selected.Y-5,10,10);}
                if(Player.HasValue){var p=point(Player.Value.X,Player.Value.Y);e.Graphics.FillEllipse(Brushes.DarkGreen,p.X-6,p.Y-6,12,12);e.Graphics.DrawString("角色",Font,Brushes.DarkGreen,p.X+8,p.Y-10);}
            }
            e.Graphics.DrawString("蓝线：录制路线   橙点：当前目标   绿点：角色",Font,Brushes.DimGray,8,8);
        }
    }
}
