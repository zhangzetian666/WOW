using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
namespace WoWProbe
{
    public sealed class QuickRoutePlan
    {
        public int SchemaVersion=1;
        public RouteDocument Route=new RouteDocument();
        public NavigationCalibration Calibration=new NavigationCalibration();
        public double CorridorWidth=.005;
        public void Validate(bool live)
        {
            if(SchemaVersion!=1||Route==null||Calibration==null)throw new FormatException("无效的巡航方案。");
            Route.Validate();Calibration.Validate();
            if(Route.RouteType!="farm"||!Route.Loop)throw new FormatException("请选择打怪用途的闭合循环路线。");
            if(!NavigationMath.InRange(CorridorWidth,.0005,.02))throw new FormatException("路线偏离范围必须为0.05至2个坐标点。");
            NavigationMath.Prepare(Route,Calibration,false,true,0);
            var first=Route.Points[0];var last=Route.Points[Route.Points.Count-1];
            if(Math.Abs(first.X-last.X)>1e-8||Math.Abs(first.Y-last.Y)>1e-8)throw new FormatException("最后一个点必须回到起点，例如A→B→C→D→A。可点击“闭合到起点”。");
            for(int i=0;i<Route.Points.Count;i++)
            {if(i>0&&NavigationMath.Distance(Route.Points[i].X-Route.Points[i-1].X,Route.Points[i].Y-Route.Points[i-1].Y,Calibration.AxisScale)<1e-8)throw new FormatException("相邻路点不能重复。");if(Route.Points[i].Tolerance>CorridorWidth/2)throw new FormatException("路点到达误差须小于路线偏离范围的一半。");if(Route.Points[i].WaitMilliseconds!=0||Route.Points[i].Action=="interact")throw new FormatException("打怪巡航路点不支持等待或商人交互动作。");}
            if(live&&!Calibration.Confirmed)throw new InvalidOperationException("请先核对本机地图与移动参数。");
        }
        public QuickRoutePlan Clone(){return new QuickRoutePlan{SchemaVersion=SchemaVersion,Route=Route.Clone(),Calibration=Calibration.Clone(),CorridorWidth=CorridorWidth};}
        public bool MapMatches(FarmObservation o){return o!=null&&o.MapId==Route.MapId&&o.MapMode==Route.MapMode&&o.Zone==Route.Zone;}
        public double DistanceToRoute(double x,double y)
        {double px,py;int end;Project(x,y,out px,out py,out end);return NavigationMath.Distance(x-px,y-py,Calibration.AxisScale);}
        public bool Contains(double x,double y)
        {return NavigationMath.InRange(x,0,1)&&NavigationMath.InRange(y,0,1)&&DistanceToRoute(x,y)<=CorridorWidth;}
        public void ProjectSegment(double x,double y,int end,out double px,out double py)
        {
            var a=Route.Points[end-1];var b=Route.Points[end];double s=Calibration.AxisScale,dx=(b.X-a.X)*s,dy=b.Y-a.Y,d=dx*dx+dy*dy;
            double t=d<=1e-20?0:Math.Max(0,Math.Min(1,((x-a.X)*s*dx+(y-a.Y)*dy)/d));px=a.X+(b.X-a.X)*t;py=a.Y+(b.Y-a.Y)*t;
        }
        public void Project(double x,double y,out double px,out double py,out int end)
        {
            px=py=Double.NaN;end=1;double best=Double.PositiveInfinity,scale=Calibration.AxisScale;
            for(int i=1;i<Route.Points.Count;i++)
            {
                var a=Route.Points[i-1];var b=Route.Points[i];double dx=(b.X-a.X)*scale,dy=b.Y-a.Y,d=dx*dx+dy*dy;
                double t=d<=1e-20?0:Math.Max(0,Math.Min(1,((x-a.X)*scale*dx+(y-a.Y)*dy)/d));
                double xx=a.X+(b.X-a.X)*t,yy=a.Y+(b.Y-a.Y)*t,dist=NavigationMath.Distance(x-xx,y-yy,scale);
                if(dist<best){best=dist;px=xx;py=yy;end=i;}
            }
        }
        public static void Save(string path,QuickRoutePlan plan)
        {plan.Validate(false);var p=plan.Clone();p.Calibration.Confirmed=false;RouteFiles.Save(path,new JavaScriptSerializer().Serialize(p));}
        public static QuickRoutePlan Load(string path)
        {return Load(path,false);}
        public static void SaveRecording(string path,QuickRoutePlan plan)
        {ValidateRecording(plan);var copy=plan.Clone();copy.Calibration.Confirmed=false;RouteFiles.Save(path,new JavaScriptSerializer().Serialize(copy));}
        public static QuickRoutePlan LoadRecording(string path){return Load(path,true);}
        static void ValidateRecording(QuickRoutePlan p)
        {
            if(p==null||p.SchemaVersion!=1||p.Route==null||p.Calibration==null||p.Route.Points==null||p.Route.Points.Count>4999)throw new FormatException("录点草稿无效。");
            p.Calibration.Validate();RouteChecks.Label(p.Route.RouteId,"route_id");RouteChecks.Label(p.Route.Name,"name");RouteChecks.Binding(p.Route.MapId,p.Route.MapMode,p.Route.Zone);
            if(p.Route.SchemaVersion!=1||p.Route.RouteType!="farm"||!p.Route.Loop||p.Route.MapId!=p.Calibration.MapId||p.Route.MapMode!=p.Calibration.MapMode||p.Route.Zone!=p.Calibration.Zone||!NavigationMath.InRange(p.CorridorWidth,.0005,.02))throw new FormatException("录点草稿地图或范围无效。");
            for(int i=0;i<p.Route.Points.Count;i++)
            {var w=p.Route.Points[i];if(w==null||!NavigationMath.InRange(w.X,0,1)||!NavigationMath.InRange(w.Y,0,1)||!NavigationMath.InRange(w.Tolerance,.0001,.02)||w.Tolerance>p.CorridorWidth/2||w.WaitMilliseconds!=0||w.Action!="wait")throw new FormatException("录点草稿坐标无效。");if(i>0&&w.X==p.Route.Points[i-1].X&&w.Y==p.Route.Points[i-1].Y)throw new FormatException("请移动到下一个位置后再保存巡航点。");}
        }
        static QuickRoutePlan Load(string path,bool recording)
        {
            var root=RouteJson.Object(RouteFiles.Load(path));RouteJson.Keys(root,"SchemaVersion,Route,Calibration,CorridorWidth",null);
            var r=RouteJson.Object(root["Route"]);RouteJson.Keys(r,"SchemaVersion,RouteId,Name,RouteType,Zone,MapId,MapMode,AllowReverse,Loop,Points",null);
            var c=RouteJson.Object(root["Calibration"]);RouteJson.Keys(c,"AxisScale,FacingSign,FacingOffset,TurnRadiansPerSecond,ForwardUnitsPerSecond,Confirmed,MapId,MapMode,Zone",null);
            var points=r["Points"] as System.Collections.Generic.List<object>;if(points==null)throw new FormatException("缺少路点列表。");foreach(var point in points)RouteJson.Keys(RouteJson.Object(point),"X,Y,Tolerance,WaitMilliseconds,Action",null);
            var plan=new JavaScriptSerializer().Deserialize<QuickRoutePlan>(File.ReadAllText(path,Encoding.UTF8));
            if(plan==null)throw new FormatException("巡航方案为空。");if(recording)ValidateRecording(plan);else plan.Validate(false);plan.Calibration.Confirmed=false;return plan;
        }
    }

    public sealed class QuickRouteFollower
    {
        public QuickRoutePlan Plan {get;private set;}
        public int TargetIndex {get;private set;}public int Laps {get;private set;}
        public bool Returning {get;private set;}
        double returnX,returnY,progressX,progressY;long last=-1,progressAt=-1,targetAt=-1,nextProbe;int sequence=-1;bool suspended;
        public QuickRouteFollower(QuickRoutePlan plan,FarmObservation start)
        {
            plan.Validate(true);Plan=plan.Clone();
            if(start==null||!start.Fresh||!start.KnownCore||start.ReadFailures!=0||!Plan.MapMatches(start))throw new InvalidOperationException("角色地图状态与路线不匹配。");
            if(start.PlayerDead||start.PlayerGhost){returnX=Plan.Route.Points[0].X;returnY=Plan.Route.Points[0].Y;TargetIndex=1;Returning=true;return;}
            if(!Plan.Contains(start.X,start.Y))throw new InvalidOperationException("角色须位于当前地图的指定路线范围内才能开始。");
            int end;Plan.Project(start.X,start.Y,out returnX,out returnY,out end);
            TargetIndex=end;Returning=NavigationMath.Distance(start.X-returnX,start.Y-returnY,Plan.Calibration.AxisScale)>Tolerance;
        }
        double Tolerance {get{return Math.Min(.001,Plan.CorridorWidth/3);}}
        public bool OnRoute(FarmObservation o){return o!=null&&Plan.MapMatches(o)&&Plan.Contains(o.X,o.Y);}
        public bool TargetAllowed(FarmObservation o)
        {return OnRoute(o)&&(o.InCombat||!o.TargetPositionKnown||Plan.Contains(o.TargetX,o.TargetY));}
        public bool MayAcquire(FarmObservation o)
        {return OnRoute(o)&&TargetAllowed(o)&&(o.InCombat||!Returning&&!suspended&&o.TargetPositionKnown);}
        public void Interrupt(FarmObservation o)
        {
            if(!suspended){Plan.ProjectSegment(o.X,o.Y,TargetIndex,out returnX,out returnY);suspended=true;}
        }
        public void Resume(){if(suspended){Returning=true;suspended=false;progressAt=targetAt=-1;}}
        public bool ProbeDue(FarmObservation o,long now)
        {
            if(Returning||suspended||!OnRoute(o)||o.InCombat||o.TargetPositionKnown||!GrindRecoveryPolicy.CanAttack(o)||o.TargetReaction!=4||now<nextProbe)return false;
            nextProbe=now+750;return true;
        }
        public QuickCommand Step(FarmObservation o,long now)
        {
            if(now<0||now<last)return C(QuickAction.Stop,"路线时钟异常。",0,0);last=now;
            if(!o.Fresh||!o.KnownCore||o.ReadFailures!=0||!Plan.MapMatches(o)||!NavigationMath.Finite(o.Facing))return C(QuickAction.Stop,"路线位置或地图状态不可验证。",0,0);
            if(!OnRoute(o))return C(QuickAction.Stop,"角色超出指定路线范围，已停止；请放回路线后重新开始。",0,0);
            if(sequence==o.Sequence)return C(QuickAction.None,"等待路线位置更新。",0,0);sequence=o.Sequence;
            if(suspended)Resume();
            if(targetAt<0)targetAt=now;if(now-targetAt>120000)return C(QuickAction.Stop,"两分钟未到达下一个路点，请检查路线与移动标定。",0,0);
            if(progressAt<0){progressAt=now;progressX=o.X;progressY=o.Y;}
            if(NavigationMath.Distance(o.X-progressX,o.Y-progressY,Plan.Calibration.AxisScale)>.0002){progressAt=now;progressX=o.X;progressY=o.Y;}
            if(now-progressAt>12000)return C(QuickAction.Stop,"路线持续无位移，停止；请检查路点间障碍。",0,0);
            double tx=Returning?returnX:Plan.Route.Points[TargetIndex].X,ty=Returning?returnY:Plan.Route.Points[TargetIndex].Y;
            double tolerance=Returning?Tolerance:Plan.Route.Points[TargetIndex].Tolerance;
            if(NavigationMath.Distance(tx-o.X,ty-o.Y,Plan.Calibration.AxisScale)<=tolerance)
            {
                progressAt=targetAt=now;
                if(Returning){Returning=false;}
                else {TargetIndex++;if(TargetIndex>=Plan.Route.Points.Count){TargetIndex=1;Laps++;}}
                tx=Plan.Route.Points[TargetIndex].X;ty=Plan.Route.Points[TargetIndex].Y;
            }
            var calibration=Plan.Calibration;
            double bearing=Math.Atan2((tx-o.X)*calibration.AxisScale,-(ty-o.Y));
            double facing=NavigationMath.Normalize(calibration.FacingSign*o.Facing+calibration.FacingOffset),delta=NavigationMath.Normalize(bearing-facing);
            string label=(Returning?"返回打怪前的路线位置":"沿指定路线前往路点"+(TargetIndex+1))+"，已完成"+Laps+"圈。";
            if(Math.Abs(delta)>.12)return C(QuickAction.Turn,label,(ushort)(delta>0?39:37),Math.Max(40,Math.Min(120,(int)(Math.Abs(delta)/calibration.TurnRadiansPerSecond*1000))));
            return C(QuickAction.Patrol,label,87,1000);
        }
        QuickCommand C(QuickAction a,string text,ushort key,int hold){return new QuickCommand{Action=a,Reason=text,Key=key,Hold=hold,RouteMovement=true,RouteContext=this};}
    }
}
