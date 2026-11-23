using System;
using System.Collections.Generic;
using System.IO;
namespace WoWProbe
{
    public static class QuickRandom
    {
        static readonly Random random=new Random();
        public static int Next(int count){lock(random)return random.Next(count);}
        public static int Except(int count,int previous){if(count<2)return 0;int n=Next(count-1);return n>=previous?n+1:n;}
        public static string Label(int index){return index==0?"原录制路线":"随机路线"+index;}
    }
    public sealed class QuickSkillRows
    {
        readonly string[] rows;readonly List<int> available=new List<int>();
        public int Selected {get;private set;}
        public string Keys {get{return rows[Selected];}}
        public QuickSkillRows(string[] values)
        {
            if(values==null||values.Length!=5)throw new FormatException("技能顺序须为5行。");
            rows=new string[5];for(int i=0;i<5;i++){rows[i]=QuickCombat.CleanKeys(values[i]??"",true);if(rows[i].Length>0)available.Add(i);}
            if(available.Count==0)throw new FormatException("请至少填写一行技能顺序。");Selected=available[0];
        }
        public int Choose(){Selected=available[QuickRandom.Next(available.Count)];return Selected;}
    }
    public static class QuickRouteVariants
    {
        // Keep every recorded corner and both endpoints. Only insert bounded nearby
        // midpoints; this is geometric variation, not a map collision/pathfinding proof.
        public static List<RouteDocument> Create(RouteDocument source,int count,double width,double scale,int seed)
        {
            source.Validate();if(count<0||count>20||!NavigationMath.InRange(width,.0001,.002)||scale<=0)throw new ArgumentException("Invalid route variation.");
            var result=new List<RouteDocument>{source.Clone()};
            for(int variant=1;variant<=count;variant++)
            {
                var r=source.Clone();r.Name="随机路线"+variant;r.Points.Clear();r.Points.Add(source.Points[0].Clone());
                var rng=new Random(unchecked(seed+variant*104729));int inserted=0,budget=5000-source.Points.Count;
                for(int i=1;i<source.Points.Count;i++)
                {
                    var a=source.Points[i-1];var b=source.Points[i];double dx=(b.X-a.X)*scale,dy=b.Y-a.Y,length=Math.Sqrt(dx*dx+dy*dy);
                    if(length>.0004&&inserted<budget)
                    {
                        double side=rng.Next(2)==0?-1:1,offset=Math.Min(width,length*.12)*(.35+.65*rng.NextDouble())*side;
                        double t=.4+.2*rng.NextDouble(),x=a.X+(b.X-a.X)*t-dy/length*offset/scale,y=a.Y+(b.Y-a.Y)*t+dx/length*offset;
                        x=Math.Max(0,Math.Min(1,x));y=Math.Max(0,Math.Min(1,y));
                        r.Points.Add(new Waypoint{X=x,Y=y,Tolerance=Math.Min(.0002,Math.Min(a.Tolerance,b.Tolerance)),Action="wait"});inserted++;
                    }
                    r.Points.Add(b.Clone());
                }
                if(inserted==0)throw new InvalidOperationException("路线点过密或数量已满，无法生成不同路线；请先精简录点或关闭随机路线。");
                r.Validate();result.Add(r);
            }
            return result;
        }
        public static List<RouteDocument> For(QuickRoutePlan p,RouteDocument route,int count)
        {return p.RandomRoutes?Create(route,count,Math.Min(p.RouteVariation,p.CorridorWidth/4),p.Calibration.AxisScale,p.RandomRouteSeed):new List<RouteDocument>{route.Clone()};}
        public static RouteDocument Service(QuickRoutePlan p,QuickVendorSettings s)
        {var r=new RouteDocument{RouteId="recorded-service",Name="NPC原录制路线",RouteType="vendor_return",MapId=p.Route.MapId,MapMode=p.Route.MapMode,Zone=p.Route.Zone};foreach(var w in s.Points)r.Points.Add(w.Clone());return r;}
        public static void Export(string directory,string prefix,List<RouteDocument> routes)
        {Directory.CreateDirectory(directory);for(int i=0;i<routes.Count;i++)RouteStore.Save(Path.Combine(directory,prefix+"-"+i.ToString("00")+".json"),routes[i]);}
    }
}
