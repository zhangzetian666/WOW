using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
namespace WoWProbe
{
    public static class FishingPointList
    {
        public static void Append(FishingClientSettings settings,FarmObservation f)
        {if(settings.Points.Count>=500)throw new InvalidOperationException("已达500个钓点上限。");if(!NavigationMath.InRange(f.X,0,1)||!NavigationMath.InRange(f.Y,0,1)||!NavigationMath.InRange(f.Facing,0,Math.PI*2))throw new InvalidOperationException("当前钓点坐标或朝向无效。");var points=settings.Points;if(points.Count>0&&NavigationMath.Distance(f.X-points[points.Count-1].X,f.Y-points[points.Count-1].Y,settings.Fishing.Calibration.AxisScale)<.001)throw new InvalidOperationException("当前位置与上一钓点过近，请移动后再保存。");if(points.Count>1&&NavigationMath.Distance(f.X-points[0].X,f.Y-points[0].Y,settings.Fishing.Calibration.AxisScale)<.001)throw new InvalidOperationException("末点会自动连接首点，无需重复保存起点。");points.Add(new FishingPoint{X=f.X,Y=f.Y,Facing=f.Facing});}
        public static string Describe(IList<FishingPoint> points)
        {var text=new System.Text.StringBuilder();for(int i=0;i<points.Count;i++){var p=points[i];text.AppendLine(String.Format(System.Globalization.CultureInfo.InvariantCulture,"{0}：X={1:F3}  Y={2:F3}  朝向={3:F3}",i+1,p.X*100,p.Y*100,p.Facing));}return points.Count==0?"尚未保存钓点。":text.ToString();}
    }
    public static class FishingRecoveryRoutes
    {
        public static RouteDocument Create(NavigationCalibration map,List<Waypoint> points)
        {var r=new RouteDocument{RouteId="recorded-ghost",Name="墓地到钓鱼区",RouteType="corpse_run",MapId=map.MapId,MapMode=map.MapMode,Zone=map.Zone};foreach(var p in points)r.Points.Add(p.Clone());r.Validate();return r;}
        public static ushort Key(ushort key,RouteKeyBindings keys)
        {return key==QuickMovement.Forward?keys.Forward:key==83?keys.Backward:key==37?keys.TurnLeft:key==39?keys.TurnRight:key==65?keys.StrafeLeft:key==68?keys.StrafeRight:key==32?keys.Jump:key;}
    }
    public sealed class FishingDeathCheckpoint
    {
        public int Version=1,MapMode;public uint MapId;public string Zone="",RouteFingerprint="";public double X,Y;public bool EndReached;
        public FarmObservation Death(){return new FarmObservation{MapId=MapId,MapMode=MapMode,Zone=Zone,X=X,Y=Y,PlayerDead=true,Fresh=true,KnownCore=true};}
        public bool Matches(RouteDocument route,FarmObservation f){return Version==1&&route!=null&&RouteFingerprint==route.Fingerprint()&&MapIdentity.Matches(MapId,MapMode,Zone,f.MapId,f.MapMode,f.Zone)&&NavigationMath.InRange(X,0,1)&&NavigationMath.InRange(Y,0,1);}
        public void Save(string path){RouteFiles.Save(path,new JavaScriptSerializer().Serialize(this));}
        public static FishingDeathCheckpoint Load(string path){if(new FileInfo(path).Length>RouteFiles.MaxBytes)throw new FormatException("死亡坐标文件过大。");return new JavaScriptSerializer().Deserialize<FishingDeathCheckpoint>(File.ReadAllText(path));}
        public static FishingDeathCheckpoint From(RouteDocument route,OperationObservation o)
        {var f=o.Farm;bool dead=f.PlayerDead&&!f.PlayerGhost;if(!dead&&(!f.PlayerGhost||!o.CorpseKnown))throw new InvalidOperationException("尚无本轮死亡坐标或游戏尸体坐标。");double x=dead?f.X:o.CorpseX,y=dead?f.Y:o.CorpseY;if(!NavigationMath.InRange(x,0,1)||!NavigationMath.InRange(y,0,1))throw new InvalidOperationException("死亡坐标不可用。");return new FishingDeathCheckpoint{MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone,X=x,Y=y,RouteFingerprint=route.Fingerprint()};}
    }
    public static class FishingLogColors
    {
        public static Color For(string message)
        {if(message.Contains("收杆随机等待")||message.Contains("收竿随机等待"))return Color.Blue;if(message.Contains("抛竿前随机等待"))return Color.Red;return SystemColors.WindowText;}
    }
}
