using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
namespace WoWProbe
{
    public sealed class FishingRange
    {
        public double Min,Max;
        public FishingRange(){} public FishingRange(double min,double max){Min=min;Max=max;}
        public void Validate(double floor,double ceiling,string label){if(!NavigationMath.InRange(Min,floor,ceiling)||!NavigationMath.InRange(Max,Min,ceiling))throw new FormatException(label+"的最小/最大值无效。");}
        public long DrawMilliseconds(double unit,Func<int,int> random){long min=(long)Math.Round(Min*unit),max=(long)Math.Round(Max*unit);return min+(max>min?random(checked((int)(max-min+1))):0);}
    }
    public sealed class FishingPoint
    {public double X,Y,Facing;public Waypoint Waypoint(){return new Waypoint{X=X,Y=Y,Tolerance=FishingTravelPolicy.ArrivalTolerance};}}
    public sealed class FishingRegion
    {
        public double X,Y,Width,Height;
        public FishingRegion Clone(){return new FishingRegion{X=X,Y=Y,Width=Width,Height=Height};}
        public static FishingRegion FromSelection(Point a,Point b,Size viewport)
        {
            if(viewport.Width<=0||viewport.Height<=0)return new FishingRegion();
            int left=Math.Max(0,Math.Min(viewport.Width,Math.Min(a.X,b.X))),right=Math.Max(0,Math.Min(viewport.Width,Math.Max(a.X,b.X)));
            int top=Math.Max(0,Math.Min(viewport.Height,Math.Min(a.Y,b.Y))),bottom=Math.Max(0,Math.Min(viewport.Height,Math.Max(a.Y,b.Y)));
            return new FishingRegion{X=left/(double)viewport.Width,Y=top/(double)viewport.Height,Width=(right-left)/(double)viewport.Width,Height=(bottom-top)/(double)viewport.Height};
        }
        public bool Valid {get{return NavigationMath.InRange(X,0,1)&&NavigationMath.InRange(Y,0,1)&&NavigationMath.InRange(Width,.01,1)&&NavigationMath.InRange(Height,.01,1)&&X+Width<=1&&Y+Height<=1;}}
    }
    public sealed class FishingClientSettings
    {
        public int Version=1;public FishingPlan Fishing=new FishingPlan();public List<FishingPoint> Points=new List<FishingPoint>();
        public QuickRoutePlan Service=new QuickRoutePlan();public bool RandomSpots=true,PeekBags,ChatAtLap,WhisperReply,TimedSessions,UseAudio=true,UseVisual=true;
        public bool ChangeAfterCatch;public double StayMinutes=10;public FishingRange CastDelay=new FishingRange(.3,1),ReelDelay=new FishingRange(0,1),BagMinutes=new FishingRange(10,15),OnlineHours=new FishingRange(1,2),OfflineHours=new FishingRange(1,2);
        public FishingRegion VisualRegion=new FishingRegion();public double SplashSensitivity=3.5;
        public void Validate(bool ready)
        {
            if(Version!=1||Fishing==null||Service==null||Points==null||Points.Count>3||VisualRegion==null||CastDelay==null||ReelDelay==null||BagMinutes==null||OnlineHours==null||OfflineHours==null)throw new FormatException("钓鱼配置无效。");
            CastDelay.Validate(0,60,"抛竿延迟（秒）");ReelDelay.Validate(0,1,"收杆延迟（秒）");BagMinutes.Validate(.1,1440,"背包间隔（分）");OnlineHours.Validate(.01,24,"在线时长（小时）");OfflineHours.Validate(.01,24,"离线时长（小时）");
            if(!NavigationMath.InRange(StayMinutes,.1,1440)||!NavigationMath.InRange(SplashSensitivity,1.5,10))throw new FormatException("驻留时间或水花灵敏度无效。");
            foreach(var p in Points)if(p==null||!NavigationMath.InRange(p.X,0,1)||!NavigationMath.InRange(p.Y,0,1)||!NavigationMath.InRange(p.Facing,0,Math.PI*2))throw new FormatException("钓点坐标或朝向无效。");
            if(!ready)return;
            if(Points.Count<(RandomSpots?3:1))throw new InvalidOperationException(RandomSpots?"请保存三个同图钓点及面水朝向。":"请保存钓点及面水朝向。");
            if(RandomSpots)for(int i=0;i<3;i++)if(NavigationMath.Distance(Points[i].X-Points[(i+1)%3].X,Points[i].Y-Points[(i+1)%3].Y,Fishing.Calibration.AxisScale)<.001)throw new FormatException("相邻钓点过近，请保存三个不同位置。");
            if(!UseAudio&&!UseVisual)throw new FormatException("至少启用声音或水花图像识别。");
            if(UseVisual&&!VisualRegion.Valid)throw new FormatException("请到“水花与聊天”在图片上按住左键拖出绿色水面选框，再点击“保存选区”；仅显示图片不代表已保存选区。");
            var plan=At(Points[0]);plan.Validate();if(Service.Vendor.Enabled)Service.Validate(true);
        }
        public FishingPlan At(FishingPoint point)
        {
            var p=Fishing.Clone();p.Spot=point.Waypoint();p.Facing=point.Facing;p.Settings.EnableVendor=false;p.Settings.RequireAudio=true;
            p.Settings.CastDelayMinMs=(int)Math.Round(CastDelay.Min*1000);p.Settings.CastDelayMaxMs=(int)Math.Round(CastDelay.Max*1000);p.Settings.ReelDelayMinMs=(int)Math.Round(ReelDelay.Min*1000);p.Settings.ReelDelayMaxMs=(int)Math.Round(ReelDelay.Max*1000);p.Settings.PostCatchDelayMs=0;
            return p;
        }
        public FishingClientSettings Clone(){return new JavaScriptSerializer{MaxJsonLength=RouteFiles.MaxBytes}.Deserialize<FishingClientSettings>(new JavaScriptSerializer{MaxJsonLength=RouteFiles.MaxBytes}.Serialize(this));}
        public void Save(string path){Validate(false);RouteFiles.Save(path,new JavaScriptSerializer{MaxJsonLength=RouteFiles.MaxBytes}.Serialize(this));}
        public static FishingClientSettings Load(string path){if(new FileInfo(path).Length>RouteFiles.MaxBytes)throw new FormatException("钓鱼方案文件过大。");var p=new JavaScriptSerializer{MaxJsonLength=RouteFiles.MaxBytes}.Deserialize<FishingClientSettings>(File.ReadAllText(path));if(p==null)throw new FormatException("钓鱼配置为空。");p.Validate(false);return p;}
    }
    public sealed class FishingSpotPicker
    {
        readonly FishingClientSettings settings;readonly Func<int,int> random;public int Segment{get;private set;}public int CompletedLaps{get;private set;}public double PositionOnSegment{get;private set;}
        public FishingSpotPicker(FishingClientSettings settings,Func<int,int> random=null){this.settings=settings;this.random=random??QuickRandom.Next;Segment=-1;}
        public FishingPoint Start(FarmObservation current)
        {
            if(settings.RandomSpots)return Next();
            Segment=0;double best=Double.MaxValue;
            for(int i=0;i<settings.Points.Count;i++){var p=settings.Points[i];double d=NavigationMath.Distance(p.X-current.X,p.Y-current.Y,settings.Fishing.Calibration.AxisScale);if(d<best){best=d;Segment=i;}}
            return settings.Points[Segment];
        }
        public FishingPoint Next()
        {
            if(!settings.RandomSpots){Segment=0;return settings.Points[0];}
            if(Segment==2)CompletedLaps++;Segment=(Segment+1)%3;PositionOnSegment=(random(9801)+100)/10000.0;
            var a=settings.Points[Segment];var b=settings.Points[(Segment+1)%3];double angle=a.Facing+NavigationMath.Normalize(b.Facing-a.Facing)*PositionOnSegment;angle=(angle%(Math.PI*2)+Math.PI*2)%(Math.PI*2);
            return new FishingPoint{X=a.X+(b.X-a.X)*PositionOnSegment,Y=a.Y+(b.Y-a.Y)*PositionOnSegment,Facing=angle};
        }
        public RouteDocument Travel(FarmObservation from,FishingPoint destination,int previousSegment)
        {
            var c=settings.Fishing.Calibration;var r=new RouteDocument{RouteId="fishing-shore",Name="三点连线换钓点",RouteType="vendor_return",AllowReverse=true,MapId=c.MapId,MapMode=c.MapMode,Zone=c.Zone};
            Add(r,new Waypoint{X=from.X,Y=from.Y,Tolerance=.0005});
            if(settings.RandomSpots)
            {
                // Join the nearest recorded shoreline segment, then follow its vertices.
                int nearest=0;double best=Double.MaxValue;Waypoint projection=null;
                for(int i=0;i<3;i++){
                    var a=settings.Points[i];var b=settings.Points[(i+1)%3];double dx=(b.X-a.X)*c.AxisScale,dy=b.Y-a.Y;
                    double t=Math.Max(0,Math.Min(1,((from.X-a.X)*c.AxisScale*dx+(from.Y-a.Y)*dy)/(dx*dx+dy*dy)));
                    var candidate=new Waypoint{X=a.X+(b.X-a.X)*t,Y=a.Y+(b.Y-a.Y)*t,Tolerance=.0005};
                    double distance=NavigationMath.Distance(candidate.X-from.X,candidate.Y-from.Y,c.AxisScale);
                    if(distance<best){best=distance;nearest=i;projection=candidate;}
                }
                Add(r,projection);
                for(int i=nearest;i!=Segment;i=(i+1)%3)Add(r,settings.Points[(i+1)%3].Waypoint());
            }
            Add(r,destination.Waypoint());return r;
        }
        static void Add(RouteDocument r,Waypoint p){if(r.Points.Count==0||NavigationMath.Distance(r.Points[r.Points.Count-1].X-p.X,r.Points[r.Points.Count-1].Y-p.Y,1)>.00001)r.Points.Add(p);}
    }
    public static class FishingTravelPolicy
    {
        public const double ArrivalTolerance=.0015;
        public static int ForwardHold(double distance,double tolerance,double configuredSpeed)
        {return NavigationMath.Pulse(Math.Max(0,distance-tolerance*.5)/Math.Max(.04,configuredSpeed)*650,20,300);}
        public static int FacingHold(double radians,double configuredRate)
        {return NavigationMath.Pulse(Math.Abs(radians)/Math.Max(3,configuredRate)*650,20,120);}
    }
    public static class FishingCycleBoundary
    {
        public static bool IsIdle(string phase,bool holding)
        {return !holding&&(phase==null||phase=="Ready"||phase=="PreCast"||phase=="RecastDelay"||phase=="RestoreFacing");}
    }
    public sealed class FishingActivityClock
    {
        readonly FishingClientSettings settings;readonly Func<int,int> random;public long NextBag,OnlineDue,OfflineDue;public string Phase="Online";
        public FishingActivityClock(FishingClientSettings settings,long now,Func<int,int> random=null){this.settings=settings;this.random=random??QuickRandom.Next;Online(now);BagClosed(now);}
        public void BagClosed(long now){NextBag=checked(now+settings.BagMinutes.DrawMilliseconds(60000,random));}
        public bool BagDue(long now){return settings.PeekBags&&now>=NextBag;}
        public bool LogoutDue(long now){return settings.TimedSessions&&Phase=="Online"&&now>=OnlineDue;}
        public void LogoutRequested(){Phase="Logout";}
        public void OfflineConfirmed(long now){if(Phase!="Logout")return;Phase="Offline";OfflineDue=checked(now+settings.OfflineHours.DrawMilliseconds(3600000,random));}
        public void Online(long now){Phase="Online";OnlineDue=checked(now+settings.OnlineHours.DrawMilliseconds(3600000,random));OfflineDue=0;}
    }
    public sealed class FishingWhisperPolicy
    {
        readonly HashSet<uint> replied=new HashSet<uint>();uint sequence;bool initialized;
        public string Pending(uint eventSequence,uint sender)
        {if(!initialized){sequence=eventSequence;initialized=true;return null;}if(eventSequence==sequence||sender==0)return null;return replied.Contains(sender)?"稍等":"这会有个电话，稍等回你";}
        public void Sent(uint eventSequence,uint sender){sequence=eventSequence;replied.Add(sender);}
        public void Discard(uint eventSequence){sequence=eventSequence;initialized=true;}
    }
    public sealed class FishingPhrases
    {
        readonly string[] lines;readonly Func<int,int> random;int last=-1;
        public FishingPhrases(string path,Func<int,int> random=null){this.random=random??QuickRandom.Next;lines=File.ReadAllLines(path,System.Text.Encoding.UTF8);var unique=new HashSet<string>();foreach(var line in lines){if(String.IsNullOrWhiteSpace(line)||line.Length>80||line.Contains("/")||line.Contains("\r")||line.Contains("\t")||!unique.Add(line))throw new FormatException("话术须为不同的简短普通文本。");}if(lines.Length!=1000)throw new FormatException("话术库须包含1000条。");}
        public string Next(){int i=random(lines.Length-(last>=0?1:0));if(last>=0&&i>=last)i++;last=i;return lines[i];}
    }
}
