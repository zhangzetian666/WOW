using System;
using System.Drawing;
namespace WoWProbe
{
    public sealed class QuickDeathRate
    {
        readonly System.Collections.Generic.Queue<long> recent=new System.Collections.Generic.Queue<long>();long last=-1;
        public bool TryRecover(long now){if(now<0||now<last)return false;last=now;while(recent.Count>0&&now-recent.Peek()>=600000)recent.Dequeue();if(recent.Count>=3)return false;recent.Enqueue(now);return true;}
    }
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
        readonly bool actualCorpseRouting;
        int detourPhase;double detourFacing;
        readonly double deathX=Double.NaN,deathY=Double.NaN;
        Navigator navigator;long started=-1,last=-1,clickedAt=-1;int sequence=-1,aliveFrames,resurrectAttempts;bool released,resurrected;double corpseX=Double.NaN,corpseY;
        public static bool RouteMatches(RouteDocument route,FarmObservation state)
        {return route!=null&&state!=null&&route.RouteType=="corpse_run"&&route.MapId==state.MapId&&route.MapMode==state.MapMode&&route.Zone==state.Zone;}
        public QuickRecovery(RouteDocument route,NavigationCalibration calibration,FarmObservation death,bool actualCorpseRouting=false)
        {
            if(calibration==null||death==null||route==null&&!actualCorpseRouting)throw new ArgumentNullException("recovery configuration");
            calibration.Validate();if(route!=null){route.Validate();if(!RouteMatches(route,death))throw new InvalidOperationException("跑尸路线与当前地图不匹配。");}
            if(calibration.MapId!=death.MapId||calibration.MapMode!=death.MapMode||calibration.Zone!=death.Zone)throw new InvalidOperationException("跑尸移动参数与当前地图不匹配。");
            this.route=route==null?null:route.Clone();this.calibration=calibration.Clone();map=death.MapId;mode=death.MapMode;zone=death.Zone;this.actualCorpseRouting=actualCorpseRouting;
            if(death.PlayerDead&&!death.PlayerGhost&&NavigationMath.InRange(death.X,0,1)&&NavigationMath.InRange(death.Y,0,1)){deathX=death.X;deathY=death.Y;}
        }
        QuickRecoveryCommand Command(QuickRecoveryAction action,string reason){return new QuickRecoveryCommand{Action=action,Reason=reason};}
        QuickRecoveryCommand Stop(string reason){return Command(QuickRecoveryAction.Stop,reason);}
        public QuickRecoveryCommand Step(OperationObservation o,long now)
        {
            if(started<0)started=now;
            if(now<0||now<last)return Stop("复活流程时钟异常。");last=now;
            if(now-started>(route!=null&&route.RouteId=="recorded-ghost"?1800000:240000))return Stop("跑尸复活超时，停止检查路线。");
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
            if(resurrected){if(now-clickedAt>10000)return Stop("复活尝试后十秒仍未存活，请检查弹窗和点击记录。");if(now-clickedAt<1500||resurrectAttempts>=3||!o.ResurrectKnown||!o.ResurrectVisible||!o.ResurrectEnabled||!o.RecoveryDelayKnown||o.RecoveryDelayMs!=0)return Command(QuickRecoveryAction.None,"等待实际复活或重新可用的复活按钮。");}
            bool apiCorpse=o.CorpseKnown&&NavigationMath.InRange(o.CorpseX,0,1)&&NavigationMath.InRange(o.CorpseY,0,1);
            bool recorded=actualCorpseRouting&&!Double.IsNaN(deathX);
            if(!apiCorpse&&!recorded)return Stop("实际尸体坐标及记录的死亡点均不可用，不能自动跑尸。");
            double cx=apiCorpse?o.CorpseX:deathX,cy=apiCorpse?o.CorpseY:deathY;
            if(apiCorpse&&recorded&&NavigationMath.Distance(cx-deathX,cy-deathY,calibration.AxisScale)>.005)return Stop("尸体坐标与本轮记录的死亡点不匹配。");
            if(Double.IsNaN(corpseX)){corpseX=cx;corpseY=cy;}
            else if(NavigationMath.Distance(cx-corpseX,cy-corpseY,calibration.AxisScale)>.005)return Stop("本轮尸体坐标发生变化。");
            if(o.ResurrectKnown&&o.ResurrectVisible&&o.ResurrectEnabled&&o.RecoveryDelayKnown&&o.RecoveryDelayMs==0)
            {
                if(NavigationMath.Distance(f.X-corpseX,f.Y-corpseY,calibration.AxisScale)>.035)return Stop("复活按钮出现但距离尸体异常。");
                resurrected=true;resurrectAttempts++;clickedAt=now;var c=Command(QuickRecoveryAction.Click,"跑尸到达：尝试点击当前复活按钮（"+resurrectAttempts+"/3），等待存活证据。");c.Region=o.ResurrectRegion;return c;
            }
            if(navigator==null)
            {
                bool knownGraveyard=route!=null&&NavigationMath.Distance(f.X-route.Points[0].X,f.Y-route.Points[0].Y,calibration.AxisScale)<=.012;
                bool strict=route!=null&&route.RouteId=="recorded-ghost";
                if(!strict&&!knownGraveyard&&!actualCorpseRouting)return Stop("当前墓地不在已配置跑尸路线起点附近。");
                var work=route==null?new RouteDocument{RouteId="observed-corpse",Name="实际死亡点跑尸",RouteType="corpse_run",MapId=map,MapMode=mode,Zone=zone}:route.Clone();
                if(strict)
                {
                    int nearest=0;double best=Double.PositiveInfinity;
                    for(int i=0;i<work.Points.Count;i++){var p=work.Points[i];double d=NavigationMath.Distance(f.X-p.X,f.Y-p.Y,calibration.AxisScale);if(d<best){best=d;nearest=i;}}
                    if(nearest>0)work.Points.RemoveRange(0,nearest);
                    if(best>.0001)work.Points.Insert(0,new Waypoint{X=f.X,Y=f.Y,Tolerance=.001});
                }
                if(actualCorpseRouting&&!strict)
                {
                    if(knownGraveyard){if(!strict){int nearest=0;double best=Double.PositiveInfinity;for(int i=0;i<work.Points.Count;i++){var p=work.Points[i];double d=NavigationMath.Distance(corpseX-p.X,corpseY-p.Y,calibration.AxisScale);if(d<best){best=d;nearest=i;}}if(nearest<work.Points.Count-1)work.Points.RemoveRange(nearest+1,work.Points.Count-nearest-1);}}
                    else{work.Points.Clear();work.Points.Add(new Waypoint{X=f.X,Y=f.Y,Tolerance=.001});}
                }
                if(strict){work=QuickServicePaths.StraightSegments(new QuickRoutePlan{Route=work,Calibration=calibration},work);work.RouteType="corpse_run";}
                var end=work.Points[work.Points.Count-1];
                double distance=NavigationMath.Distance(corpseX-end.X,corpseY-end.Y,calibration.AxisScale);
                if(distance>.04&&!actualCorpseRouting)return Stop("实际尸体超出已验证路线的末端接入范围。");
                if(distance>.008)
                {double ratio=(distance-.008)/distance;work.Points.Add(new Waypoint{X=end.X+(corpseX-end.X)*ratio,Y=end.Y+(corpseY-end.Y)*ratio,Tolerance=.001,Action="wait"});}
                if(work.Points.Count<2)return Command(QuickRecoveryAction.None,"已接近实际死亡点，等待尸体复活按钮和冷却，不与灵魂医者交互。");
                navigator=new Navigator(work,calibration,false,false,0,NavigationPurpose.CorpseRun,120000){ForwardPulseLimit=3000};
            }
            if(detourPhase!=0){if(detourPhase==1)detourFacing=f.Facing;else if(!MouseLookInput.TurnObserved(detourFacing,f.Facing))return Stop("跑尸脱困未确认朝向改变，停止盲目前进。");var move=Command(QuickRecoveryAction.Key,detourPhase==1?"跑尸受阻：转向换方向。":"跑尸脱困：短距离试走，再接回录制路线。");move.Key=(ushort)(detourPhase==1?39:87);move.Hold=350;detourPhase=detourPhase==1?2:0;return move;}
            var n=navigator.Step(new NavigationSample{X=f.X,Y=f.Y,Facing=f.Facing,MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone,Sequence=f.Sequence,Fresh=f.Fresh,KnownCore=f.KnownCore,Ghost=f.PlayerGhost,Dead=f.PlayerDead,Combat=f.InCombat,ReadFailures=f.ReadFailures},now);
            if(n.Action==NavigationAction.Stop)return Stop("跑尸停止："+n.Reason);
            if(n.Action==NavigationAction.Jump||n.Action==NavigationAction.StrafeLeft){detourPhase=1;var back=Command(QuickRecoveryAction.Key,"跑尸前进无位移：后退后换方向脱困。");back.Key=83;back.Hold=250;return back;}
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
