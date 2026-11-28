using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
namespace WoWProbe
{
    // Coordinates are sampled read-only. A temporary UI handoff does not restart the route.
    public sealed class FishingRouteRecorder
    {
        readonly NavigationCalibration map;readonly List<Waypoint> points=new List<Waypoint>();
        readonly Func<long> clock;readonly bool allowGhost;long last;double x,y;int sequence;
        public int Count{get{return points.Count;}}
        public NavigationCalibration Calibration{get{return map.Clone();}}
        public string Location{get{return String.Format(CultureInfo.InvariantCulture,"({0:F3}, {1:F3})",x*100,y*100);}}
        public FishingRouteRecorder(NavigationCalibration map,FarmObservation start,Func<long> clock=null,bool allowGhost=false)
        {
            var watch=Stopwatch.StartNew();this.clock=clock??(()=>watch.ElapsedMilliseconds);last=this.clock();
            this.map=map.Clone();this.allowGhost=allowGhost;this.map.Validate();Check(start);x=start.X;y=start.Y;sequence=start.Sequence;
            points.Add(new Waypoint{X=x,Y=y,Tolerance=.0004});
        }
        void Check(FarmObservation f)
        {
            if(f==null||!f.Fresh||!f.KnownCore||f.ReadFailures!=0)throw new InvalidOperationException("暂未读到有效坐标，请露出游戏左上色块；已录点保留。最后有效坐标"+Location);
            if(f.PlayerDead&&!f.PlayerGhost||f.PlayerGhost&&!allowGhost)throw new InvalidOperationException("当前死亡/灵魂状态不适合此路线录制，已录点保留。最后有效坐标"+Location);
            if(!MapIdentity.Matches(f.MapId,f.MapMode,f.Zone,map.MapId,map.MapMode,map.Zone))throw new InvalidOperationException("当前地图发生变化，已录点保留；不能连接不同地图。最后有效坐标"+Location);
            if(!NavigationMath.InRange(f.X,0,1)||!NavigationMath.InRange(f.Y,0,1))throw new InvalidOperationException("坐标超出有效范围，已录点保留。");
        }
        public bool Observe(FarmObservation f,bool endpoint)
        {
            Check(f);long now=clock();double gap=(now-last)/1000.0;if(gap<0)throw new InvalidOperationException("录制时钟异常。");
            double moved=NavigationMath.Distance(f.X-x,f.Y-y,map.AxisScale);
            // Normal movement through a short locator/UI delay is allowed. Long missing
            // sections and implausible jumps still cannot become a completed NPC path.
            // Manual recording may use mounts/sprint; navigation calibration is not a walking speed limit.
            double limit=Math.Min(.06,Math.Max(.006,gap*Math.Max(.06,map.ForwardUnitsPerSecond*2)+.002));
            if(moved>limit||gap>10&&moved>.002)throw new InvalidOperationException("采样缺口或位移过大；最后有效坐标"+Location+String.Format(CultureInfo.InvariantCulture,"，当前({0:F3}, {1:F3})。已录{2}点保留，可结束保存草稿，或回到最后有效坐标补录。",f.X*100,f.Y*100,points.Count));
            if(f.Sequence==sequence&&!endpoint)return false;
            sequence=f.Sequence;last=now;x=f.X;y=f.Y;var p=points[points.Count-1];
            if(NavigationMath.Distance(x-p.X,y-p.Y,map.AxisScale)<(endpoint?1e-8:.00035))return false;
            if(points.Count>=4990)throw new InvalidOperationException("录制路线已达4990点，可结束保存草稿。");
            points.Add(new Waypoint{X=x,Y=y,Tolerance=.0004});return true;
        }
        public List<Waypoint> Finish(){if(points.Count<2)throw new InvalidOperationException("路线不足两个点，已保存草稿，请继续补录。");return Snapshot();}
        public List<Waypoint> Snapshot(){var result=new List<Waypoint>();foreach(var p in points)result.Add(p.Clone());return result;}
        public FishingRecordingDraft Draft(){return new FishingRecordingDraft{Map=map.Clone(),Points=Snapshot(),LastX=x,LastY=y,AllowGhost=allowGhost};}
        public static FishingRouteRecorder Resume(FishingRecordingDraft draft,FarmObservation f,Func<long> clock=null)
        {
            draft.Validate();var r=new FishingRouteRecorder(draft.Map,f,clock,draft.AllowGhost);
            if(NavigationMath.Distance(f.X-draft.LastX,f.Y-draft.LastY,draft.Map.AxisScale)>.001)
                throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture,"草稿已保留{0}点；请先回到最后有效坐标({1:F3}, {2:F3})，再点继续上次录制。",draft.Points.Count,draft.LastX*100,draft.LastY*100));
            r.points.Clear();foreach(var point in draft.Points)r.points.Add(point.Clone());return r;
        }
    }
    public sealed class FishingRecordingDraft
    {
        public int Version=1;public NavigationCalibration Map;public List<Waypoint> Points;public double LastX,LastY;public bool AllowGhost;
        public void Validate()
        {
            if(Version!=1||Map==null||Points==null||Points.Count<1||Points.Count>4990||!NavigationMath.InRange(LastX,0,1)||!NavigationMath.InRange(LastY,0,1))throw new FormatException("录制草稿无效。");
            Map.Validate();var route=new QuickVendorSettings{Points=Points};route.Validate();
            var last=Points[Points.Count-1];if(NavigationMath.Distance(last.X-LastX,last.Y-LastY,Map.AxisScale)>.001)throw new FormatException("草稿最后坐标与已录路线不匹配。");
        }
        public void Save(string path){Validate();RouteFiles.Save(path,new JavaScriptSerializer{MaxJsonLength=RouteFiles.MaxBytes}.Serialize(this));}
        public static FishingRecordingDraft Load(string path)
        {
            if(new FileInfo(path).Length>RouteFiles.MaxBytes)throw new FormatException("录制草稿过大。");
            var draft=new JavaScriptSerializer{MaxJsonLength=RouteFiles.MaxBytes}.Deserialize<FishingRecordingDraft>(File.ReadAllText(path));
            if(draft==null)throw new FormatException("录制草稿为空。");draft.Validate();return draft;
        }
    }
    public sealed class FishingRecordingFinish
    {public bool Complete;public List<Waypoint> Points;public string Message;}
    public static class FishingRecordingFinalizer
    {
        public static FishingRecordingFinish Finish(FishingRouteRecorder recorder,Func<FarmObservation> readEndpoint,string draftPath)
        {
            recorder.Draft().Save(draftPath); // Persist before a potentially slow/failed final read.
            try{
                recorder.Observe(readEndpoint(),true);recorder.Draft().Save(draftPath);
                return new FishingRecordingFinish{Complete=true,Points=recorder.Finish(),Message="录制完成。"};
            }catch(InvalidOperationException e){return new FishingRecordingFinish{Complete=false,Points=recorder.Snapshot(),Message="录制已结束，已录点已保存为草稿，原已保存路线保持不变。"+Environment.NewLine+e.Message};}
        }
        public static QuickRoutePlan Service(QuickRoutePlan existing,NavigationCalibration map,List<Waypoint> points,FarmObservation endpoint,string name,bool repair)
        {
            var next=existing.Clone();next.Calibration=map.Clone();next.Vendor.Points=new List<Waypoint>();foreach(var p in points)next.Vendor.Points.Add(p.Clone());
            next.Vendor.X=points[points.Count-1].X;next.Vendor.Y=points[points.Count-1].Y;next.Vendor.Enabled=next.Vendor.RouteComplete=true;next.Vendor.Name=name;
            if(endpoint.TargetExists&&!endpoint.TargetCanAttack&&!endpoint.TargetIsPlayer&&!endpoint.TargetDead)next.Vendor.Name=endpoint.TargetName;
            next.Repair=next.Vendor.Clone();next.Repair.Enabled=repair;next.RandomRoutes=next.TimedBreaks=next.UseRogueVanish=false;
            next.Route=new RouteDocument{RouteId="fishing-service-anchor",Name="钓鱼售卖接入范围",RouteType="farm",Loop=true,MapId=map.MapId,MapMode=map.MapMode,Zone=map.Zone};
            next.Route.Points.Add(points[0].Clone());next.Route.Points.Add(points[1].Clone());next.Route.Points.Add(points[0].Clone());next.Validate(true);return next;
        }
    }
}
