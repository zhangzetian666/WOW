using System;
namespace WoWProbe
{
    // A small relative movement while owning the right button. Never takes over
    // a user's held button, and always releases our button after any failure.
    public static class MouseLookInput
    {
        public static bool TurnWithFallback(Func<double> facing,Action mouse,Action keyboard)
        {
            double before=facing();mouse();if(TurnObserved(before,facing()))return true;
            keyboard();return TurnObserved(before,facing());
        }
        public static bool TurnObserved(double before,double after)
        {
            if(Double.IsNaN(before)||Double.IsNaN(after)||Double.IsInfinity(before)||Double.IsInfinity(after))return false;
            return Math.Abs(NavigationMath.Normalize(after-before))>=.04;
        }
        public static void Run(int dx,int dy,Action ensure,Func<bool> held,Action down,Action up,Action<int,int> move,Action<int> wait)
        {
            if(Math.Abs((long)dx)>160||Math.Abs((long)dy)>160||(dx==0&&dy==0))throw new ArgumentOutOfRangeException("dx/dy");
            ensure();if(held())throw new InvalidOperationException("用户正在按鼠标或修饰键。");
            bool owned=false;
            try
            {
                ensure();owned=true;down();wait(30);ensure();
                int steps=Math.Max((Math.Abs(dx)+7)/8,(Math.Abs(dy)+7)/8),x=0,y=0;
                for(int i=1;i<=steps;i++)
                {ensure();int nx=dx*i/steps,ny=dy*i/steps;move(nx-x,ny-y);x=nx;y=ny;wait(20);}
            }
            finally{if(owned)up();}
            ensure();
        }
    }
}
