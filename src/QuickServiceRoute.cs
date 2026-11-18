using System;
using System.Collections.Generic;
namespace WoWProbe
{
    // Recording reads coordinates only; it never injects movement or changes focus.
    public sealed class QuickServiceRecording
    {
        readonly QuickRoutePlan plan;long lastTime=-1,lastGood;int sequence=-1;
        readonly List<Waypoint> points=new List<Waypoint>();
        public int Count {get{return points.Count;}}
        public QuickServiceRecording(QuickRoutePlan plan,FarmObservation start,long now)
        {
            plan.Validate(false);this.plan=plan.Clone();Check(start);
            if(!plan.Contains(start.X,start.Y))throw new InvalidOperationException("请先走到打怪巡航路线附近，再开始录制NPC路线。");
            Add(start);lastGood=lastTime=now;sequence=start.Sequence;
        }
        void Check(FarmObservation o)
        {if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0||o.PlayerDead||o.PlayerGhost||!plan.MapMatches(o)||!NavigationMath.InRange(o.X,0,1)||!NavigationMath.InRange(o.Y,0,1))throw new InvalidOperationException("录制坐标失效、死亡或地图变化；请回到最后录点后重试，不能跨过未记录路段。");}
        void Add(FarmObservation o){if(points.Count>=4990)throw new InvalidOperationException("NPC路线已达到4990点，请结束录制。");points.Add(new Waypoint{X=o.X,Y=o.Y,Tolerance=.0004});}
        public bool Observe(FarmObservation o,long now,bool endpoint=false)
        {
            if(now<0||now<lastTime)throw new InvalidOperationException("录制时钟异常。");lastTime=now;Check(o);
            var p=points[points.Count-1];double distance=NavigationMath.Distance(o.X-p.X,o.Y-p.Y,plan.Calibration.AxisScale);
            if(distance>Math.Max(.004,(now-lastGood)/1000.0*plan.Calibration.ForwardUnitsPerSecond*2+.001)||now-lastGood>3000&&distance>.001)throw new InvalidOperationException("坐标采样中断或发生跳跃，请回到最后录点；未保存跨越障碍的直线。");
            if(!endpoint&&sequence==o.Sequence)return false;sequence=o.Sequence;lastGood=now;
            if(distance<(endpoint?1e-8:.00035))return false;Add(o);return true;
        }
        public List<Waypoint> Snapshot(){var c=new List<Waypoint>();foreach(var p in points)c.Add(p.Clone());return c;}
        public QuickVendorSettings Finish(FarmObservation o,long now,QuickVendorSettings settings)
        {
            Observe(o,now,true);if(points.Count<2)throw new InvalidOperationException("请实际走到NPC旁，路线至少需要两个不同位置。");
            var s=settings.Clone();s.Points=Snapshot();s.RouteComplete=true;s.Enabled=true;s.X=o.X;s.Y=o.Y;
            if(o.TargetExists&&o.TargetIdentityKnown&&!o.TargetIsPlayer&&o.AttackabilityKnown&&!o.TargetCanAttack&&o.TargetDeadKnown&&!o.TargetDead)s.Name=o.TargetName;
            s.RequireRoute();return s;
        }
    }
    public static class QuickServicePaths
    {
        public static RouteDocument StartReturn(QuickRoutePlan p,FarmObservation start)
        {
            if(!p.MapMatches(start)||start.PlayerDead||start.PlayerGhost)return null;
            QuickVendorSettings selected=null;double best=Double.PositiveInfinity;
            foreach(var s in new[]{p.Vendor,p.Repair})
            {
                if(s==null||!s.RouteComplete||s.Points==null||s.Points.Count<2)continue;
                var end=s.Points[s.Points.Count-1];double d=NavigationMath.Distance(start.X-end.X,start.Y-end.Y,p.Calibration.AxisScale);
                if(d<=.0015&&d<best){selected=s;best=d;}
            }
            if(selected==null)return null;
            selected.RequireRoute();
            if(p.DistanceToRoute(selected.Points[0].X,selected.Points[0].Y)>p.CorridorWidth)throw new InvalidOperationException("NPC路线起点不在当前巡航路线附近，请重新录制。");
            var outbound=Empty(p);foreach(var point in selected.Points)Add(outbound,point);
            return Return(p,outbound,start);
        }
        static void Add(RouteDocument r,Waypoint p){if(r.Points.Count==0||NavigationMath.Distance(r.Points[r.Points.Count-1].X-p.X,r.Points[r.Points.Count-1].Y-p.Y,1)>1e-8)r.Points.Add(p.Clone());}
        public static RouteDocument Empty(QuickRoutePlan p){return new RouteDocument{RouteId="recorded-service",Name="录制NPC往返",RouteType="vendor_return",MapId=p.Route.MapId,MapMode=p.Route.MapMode,Zone=p.Route.Zone};}
        public static RouteDocument Outbound(QuickRoutePlan p,QuickVendorSettings service,FarmObservation start)
        {
            service.RequireRoute();if(!p.MapMatches(start))throw new InvalidOperationException("NPC路线地图不一致。");
            var r=Empty(p);Add(r,new Waypoint{X=start.X,Y=start.Y,Tolerance=.0004});
            if(NavigationMath.Distance(start.X-service.X,start.Y-service.Y,p.Calibration.AxisScale)<=.0015)return r;
            var a=service.Points[0];if(p.DistanceToRoute(a.X,a.Y)>p.CorridorWidth)throw new InvalidOperationException("NPC路线起点不在当前巡航路线附近，请重新从巡航路线录制。");
            if(p.DistanceToRoute(start.X,start.Y)>p.CombatRadius)throw new InvalidOperationException("当前位置离巡航路线过远，不能直接接入NPC路线。");
            double sx,sy,ax,ay;int se,ae;p.Project(start.X,start.Y,out sx,out sy,out se);p.Project(a.X,a.Y,out ax,out ay,out ae);
            Add(r,new Waypoint{X=sx,Y=sy,Tolerance=.0004});
            var farm=p.Route.Points;var lengths=new double[farm.Count];for(int i=1;i<farm.Count;i++)lengths[i]=lengths[i-1]+NavigationMath.Distance(farm[i].X-farm[i-1].X,farm[i].Y-farm[i-1].Y,p.Calibration.AxisScale);
            double from=lengths[se-1]+NavigationMath.Distance(sx-farm[se-1].X,sy-farm[se-1].Y,p.Calibration.AxisScale),to=lengths[ae-1]+NavigationMath.Distance(ax-farm[ae-1].X,ay-farm[ae-1].Y,p.Calibration.AxisScale),total=lengths[lengths.Length-1];
            if(to<from-1e-8)to+=total;
            for(int lap=0;lap<2;lap++)for(int i=1;i<farm.Count;i++){double at=lengths[i]+lap*total;if(at>from+1e-8&&at<to-1e-8)Add(r,new Waypoint{X=farm[i].X,Y=farm[i].Y,Tolerance=.0004});}
            Add(r,new Waypoint{X=ax,Y=ay,Tolerance=.0004});foreach(var point in service.Points)Add(r,point);
            if(r.Points.Count>4999)throw new InvalidOperationException("巡航接入加NPC路线超过4999点，请缩短路线。");return r;
        }
        public static RouteDocument Return(QuickRoutePlan p,RouteDocument outbound,FarmObservation current)
        {var r=Empty(p);Add(r,new Waypoint{X=current.X,Y=current.Y,Tolerance=.0004});for(int i=outbound.Points.Count-1;i>=0;i--)Add(r,outbound.Points[i]);return r;}
        public static Navigator Navigate(QuickRoutePlan p,RouteDocument path,FarmObservation current,int next=0)
        {var r=Empty(p);Add(r,new Waypoint{X=current.X,Y=current.Y,Tolerance=.0004});for(int i=next;i<path.Points.Count;i++)Add(r,path.Points[i]);if(r.Points.Count<2)return null;return new Navigator(r,p.Calibration,false,false,0,NavigationPurpose.VendorReturn,120000);}
    }
}
