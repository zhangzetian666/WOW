using System;
using System.Drawing;
namespace WoWProbe
{
    public enum QuickRecoveryAction { None, Key, Click, Complete, Stop }
    public sealed class QuickRecoveryCommand
    {
        public QuickRecoveryAction Action;public ushort Key;public int Hold;public Rectangle Region;public bool Release;public string Reason;
    }
    // A configured, map-bound graveyard route is required. Buttons come from the
    // current WO4 packet; no healer dialog coordinates or guessed button clicks.
    public sealed class QuickRecovery
    {
        readonly RouteDocument route;readonly NavigationCalibration calibration;readonly uint map;readonly int mode;readonly string zone;
        Navigator navigator;long started=-1,last=-1,clickedAt=-1;int sequence=-1,aliveFrames;bool released,resurrected;double corpseX=Double.NaN,corpseY;
        public QuickRecovery(RouteDocument route,NavigationCalibration calibration,FarmObservation death)
        {
            if(route==null||calibration==null||death==null)throw new ArgumentNullException("recovery configuration");
            route.Validate();calibration.Validate();
            if(route.RouteType!="corpse_run"||route.MapId!=death.MapId||route.MapMode!=death.MapMode||route.Zone!=death.Zone)throw new InvalidOperationException("跑尸路线与当前地图不匹配。");
            this.route=route.Clone();this.calibration=calibration.Clone();map=death.MapId;mode=death.MapMode;zone=death.Zone;
        }
        QuickRecoveryCommand Command(QuickRecoveryAction action,string reason){return new QuickRecoveryCommand{Action=action,Reason=reason};}
        QuickRecoveryCommand Stop(string reason){return Command(QuickRecoveryAction.Stop,reason);}
        public QuickRecoveryCommand Step(OperationObservation o,long now)
        {
            if(started<0)started=now;
            if(now<0||now<last)return Stop("复活流程时钟异常。");last=now;
            if(now-started>240000)return Stop("四分钟内未完成跑尸复活，停止。");
            if(o==null||!o.Fresh||o.Farm==null||!o.Farm.Fresh||!o.Farm.KnownCore||o.Farm.ReadFailures!=0)return Stop("复活状态或按钮数据不可读。");
            var f=o.Farm;
            if(f.MapId!=map||f.MapMode!=mode||f.Zone!=zone)return Stop("复活地图发生变化。");
            if(!f.PlayerDead&&!f.PlayerGhost)
            {
                if(f.Health<=0||f.Health>100)return Stop("复活后尚不能确认正生命值。");
                if(sequence==o.Sequence)return Command(QuickRecoveryAction.None,"等待第二帧存活状态。");sequence=o.Sequence;
                return ++aliveFrames>=2?Command(QuickRecoveryAction.Complete,"确认复活，先恢复满血再继续。"):Command(QuickRecoveryAction.None,"确认第二帧存活状态。");
            }
            if(sequence==o.Sequence)return Command(QuickRecoveryAction.None,"等待新的复活状态。");sequence=o.Sequence;
            if(f.PlayerDead&&!f.PlayerGhost)
            {
                if(released)return now-clickedAt>10000?Stop("释放灵魂后十秒未进入幽魂状态。"):Command(QuickRecoveryAction.None,"等待释放灵魂。");
                if(!o.ReleaseKnown||!o.ReleaseVisible||!o.ReleaseEnabled)return Command(QuickRecoveryAction.None,"等待可用的释放灵魂按钮。");
                released=true;clickedAt=now;var c=Command(QuickRecoveryAction.Click,"死亡：点击已验证的释放灵魂按钮。");c.Release=true;c.Region=o.ReleaseRegion;return c;
            }
            if(resurrected)return now-clickedAt>10000?Stop("点击复活后十秒仍未存活。"):Command(QuickRecoveryAction.None,"等待实际复活。");
            if(!o.CorpseKnown||!NavigationMath.InRange(o.CorpseX,0,1)||!NavigationMath.InRange(o.CorpseY,0,1))return Stop("实际尸体坐标不可读，不能自动跑尸。");
            if(Double.IsNaN(corpseX)){corpseX=o.CorpseX;corpseY=o.CorpseY;}
            else if(NavigationMath.Distance(o.CorpseX-corpseX,o.CorpseY-corpseY,calibration.AxisScale)>.001)return Stop("本轮尸体坐标发生变化。");
            if(o.ResurrectKnown&&o.ResurrectVisible&&o.ResurrectEnabled&&o.RecoveryDelayKnown&&o.RecoveryDelayMs==0)
            {
                if(NavigationMath.Distance(f.X-corpseX,f.Y-corpseY,calibration.AxisScale)>.035)return Stop("复活按钮出现但距离尸体异常。");
                resurrected=true;clickedAt=now;var c=Command(QuickRecoveryAction.Click,"跑尸到达：点击已验证的复活按钮。");c.Region=o.ResurrectRegion;return c;
            }
            if(navigator==null)
            {
                if(NavigationMath.Distance(f.X-route.Points[0].X,f.Y-route.Points[0].Y,calibration.AxisScale)>.012)return Stop("当前墓地不在已配置跑尸路线起点附近。");
                var work=route.Clone();var end=work.Points[work.Points.Count-1];
                double distance=NavigationMath.Distance(corpseX-end.X,corpseY-end.Y,calibration.AxisScale);
                if(distance>.04)return Stop("实际尸体超出已验证路线的末端接入范围。");
                if(distance>.008)
                {double ratio=(distance-.008)/distance;work.Points.Add(new Waypoint{X=end.X+(corpseX-end.X)*ratio,Y=end.Y+(corpseY-end.Y)*ratio,Tolerance=.001,Action="wait"});}
                navigator=new Navigator(work,calibration,false,false,0,NavigationPurpose.CorpseRun,80000);
            }
            var n=navigator.Step(new NavigationSample{X=f.X,Y=f.Y,Facing=f.Facing,MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone,Sequence=f.Sequence,Fresh=f.Fresh,KnownCore=f.KnownCore,Ghost=f.PlayerGhost,Dead=f.PlayerDead,Combat=f.InCombat,ReadFailures=f.ReadFailures},now);
            if(n.Action==NavigationAction.Stop)return Stop("跑尸停止："+n.Reason);
            if(n.Action==NavigationAction.None||n.Action==NavigationAction.Completed)return Command(QuickRecoveryAction.None,"等待复活距离及冷却就绪。");
            ushort key=n.Action==NavigationAction.Forward?(ushort)87:n.Action==NavigationAction.TurnLeft?(ushort)37:n.Action==NavigationAction.TurnRight?(ushort)39:n.Action==NavigationAction.StrafeLeft?(ushort)65:n.Action==NavigationAction.StrafeRight?(ushort)68:(ushort)32;
            var result=Command(QuickRecoveryAction.Key,"跑尸："+n.Reason);result.Key=key;result.Hold=n.HoldMilliseconds;return result;
        }
        public static bool Matches(QuickRecoveryCommand c,OperationObservation o)
        {
            if(o==null||!o.Fresh||o.Farm==null||!o.Farm.Fresh||!o.Farm.KnownCore||o.Farm.ReadFailures!=0)return false;
            if(c.Action==QuickRecoveryAction.Key)return o.Farm.PlayerGhost&&!o.Farm.InCombat;
            if(c.Action!=QuickRecoveryAction.Click||!o.GeometryKnown||c.Region.Width<1||c.Region.Height<1)return false;
            return c.Release?o.Farm.PlayerDead&&!o.Farm.PlayerGhost&&o.ReleaseKnown&&o.ReleaseVisible&&o.ReleaseEnabled&&o.ReleaseRegion==c.Region:o.Farm.PlayerGhost&&o.ResurrectKnown&&o.ResurrectVisible&&o.ResurrectEnabled&&o.ResurrectRegion==c.Region&&o.RecoveryDelayKnown&&o.RecoveryDelayMs==0;
        }
    }
}
