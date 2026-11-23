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
        public double CombatRadius=.02;
        public string OpeningKeys="242223",LoopKeys="2";
        public int SkillIntervalMs=750;
        public string[] SkillRows;public bool RandomRoutes,TimedBreaks;public double RouteVariation=.0006;public int RandomRouteSeed=20261123;
        public bool UseRogueVanish=true;public bool AttackHostile;public bool NoChatCombat;public string StartAttackKey="1",LastTargetKey="F8",StopAttackKey="F9";
        public int RestBelowPercent=80;public string DrinkKey="";
        public QuickVendorSettings Vendor=new QuickVendorSettings();
        public QuickVendorSettings Repair=new QuickVendorSettings();
        public void ValidateSkills(){if(SkillRows!=null)new QuickSkillRows(SkillRows);if(!NavigationMath.InRange(RouteVariation,.0001,.002))throw new FormatException("随机路线偏移须为0.01至0.20坐标点。");if(NoChatCombat){var a=QuickCombatHotkeys.Key(StartAttackKey);var b=QuickCombatHotkeys.Key(LastTargetKey);var c=QuickCombatHotkeys.Key(StopAttackKey);if(a==b||a==c||b==c)throw new FormatException("开始攻击、上一个目标和停止攻击快捷键不能相同。");}QuickCombat.CleanKeys(OpeningKeys,true);QuickCombat.CleanKeys(LoopKeys,false);QuickCombat.ConfiguredKey(DrinkKey);if(RestBelowPercent<30||RestBelowPercent>95)throw new FormatException("吃喝触发阈值须为30–95%。");if(SkillIntervalMs<100||SkillIntervalMs>5000)throw new FormatException("技能间隔须为0.10至5.00秒。");}
        public void Validate(bool live)
        {
            if(SchemaVersion!=1||Route==null||Calibration==null)throw new FormatException("无效的巡航方案。");
            Route.Validate();Calibration.Validate();
            if(Vendor==null)throw new FormatException("商人设置无效。");Vendor.Validate();
            if(Repair==null)throw new FormatException("修理NPC设置无效。");Repair.Validate();
            if(live){if(Vendor.Enabled)Vendor.RequireRoute();if(Repair.Enabled)Repair.RequireRoute();}
            ValidateSkills();
            if(Route.RouteType!="farm"||!Route.Loop)throw new FormatException("请选择打怪用途的闭合循环路线。");
            if(!NavigationMath.InRange(CorridorWidth,.0005,.02))throw new FormatException("路线偏离范围必须为0.05至2个坐标点。");
            if(!NavigationMath.InRange(CombatRadius,CorridorWidth,.05))throw new FormatException("追怪范围必须不小于巡航偏离范围，且最多5个坐标点。");
            NavigationMath.Prepare(Route,Calibration,false,true,0);
            var first=Route.Points[0];var last=Route.Points[Route.Points.Count-1];
            if(Math.Abs(first.X-last.X)>1e-8||Math.Abs(first.Y-last.Y)>1e-8)throw new FormatException("最后一个点必须回到起点，例如A→B→C→D→A。可点击“闭合到起点”。");
            for(int i=0;i<Route.Points.Count;i++)
            {if(i>0&&NavigationMath.Distance(Route.Points[i].X-Route.Points[i-1].X,Route.Points[i].Y-Route.Points[i-1].Y,Calibration.AxisScale)<1e-8)throw new FormatException("相邻路点不能重复。");if(Route.Points[i].Tolerance>CorridorWidth/2)throw new FormatException("路点到达误差须小于路线偏离范围的一半。");if(Route.Points[i].WaitMilliseconds!=0||Route.Points[i].Action=="interact")throw new FormatException("打怪巡航路点不支持等待或商人交互动作。");}
            if(live&&!Calibration.Confirmed)throw new InvalidOperationException("请先核对本机地图与移动参数。");
        }
        public QuickRoutePlan Clone(){return new QuickRoutePlan{SkillRows=SkillRows==null?null:(string[])SkillRows.Clone(),RandomRoutes=RandomRoutes,TimedBreaks=TimedBreaks,RouteVariation=RouteVariation,RandomRouteSeed=RandomRouteSeed,SchemaVersion=SchemaVersion,UseRogueVanish=UseRogueVanish,AttackHostile=AttackHostile,NoChatCombat=NoChatCombat,StartAttackKey=StartAttackKey,LastTargetKey=LastTargetKey,StopAttackKey=StopAttackKey,Route=Route.Clone(),Calibration=Calibration.Clone(),CorridorWidth=CorridorWidth,CombatRadius=CombatRadius,OpeningKeys=OpeningKeys,LoopKeys=LoopKeys,SkillIntervalMs=SkillIntervalMs,RestBelowPercent=RestBelowPercent,DrinkKey=DrinkKey,Vendor=Vendor.Clone(),Repair=Repair.Clone()};}
        public bool MapMatches(FarmObservation o){return o!=null&&MapIdentity.Matches(o.MapId,o.MapMode,o.Zone,Route.MapId,Route.MapMode,Route.Zone);}
        public double DistanceToRoute(double x,double y)
        {double px,py;int end;Project(x,y,out px,out py,out end);return NavigationMath.Distance(x-px,y-py,Calibration.AxisScale);}
        public bool Contains(double x,double y)
        {return NavigationMath.InRange(x,0,1)&&NavigationMath.InRange(y,0,1)&&DistanceToRoute(x,y)<=CorridorWidth;}
        public bool WithinCombatArea(double x,double y)
        {return NavigationMath.InRange(x,0,1)&&NavigationMath.InRange(y,0,1)&&DistanceToRoute(x,y)<=CombatRadius;}
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
        public static QuickRoutePlan Import(string path)
        {
            var root=RouteJson.Object(RouteFiles.Load(path));
            if(root.ContainsKey("Route"))return Load(path,true);
            if(!root.ContainsKey("schema_version"))throw new FormatException("不支持的JSON：请选择打怪路线、巡航方案或录点草稿。");
            var r=RouteStore.Load(path);if(r.RouteType!="farm")throw new FormatException("请选择打怪路线；跑尸和商人路线不能作为巡航路线导入。");
            r.Loop=true;foreach(var w in r.Points){w.Tolerance=.001;w.WaitMilliseconds=0;w.Action="wait";}
            var a=r.Points[0];var z=r.Points[r.Points.Count-1];if(a.X!=z.X||a.Y!=z.Y){if(r.Points.Count>=4999)throw new FormatException("路线点过多，无法补上闭合起点。");r.Points.Add(a.Clone());}
            var p=new QuickRoutePlan{RandomRoutes=true,TimedBreaks=true,Route=r,Calibration=new NavigationCalibration{MapId=r.MapId,MapMode=r.MapMode,Zone=r.Zone,AxisScale=1.5,TurnRadiansPerSecond=5.85,ForwardUnitsPerSecond=.008}};p.Validate(false);return p;
        }
        static void ValidateRecording(QuickRoutePlan p)
        {
            if(p==null||p.SchemaVersion!=1||p.Route==null||p.Calibration==null||p.Route.Points==null||p.Route.Points.Count>4999)throw new FormatException("录点草稿无效。");
            p.Calibration.Validate();RouteChecks.Label(p.Route.RouteId,"route_id");RouteChecks.Label(p.Route.Name,"name");RouteChecks.Binding(p.Route.MapId,p.Route.MapMode,p.Route.Zone);
            p.ValidateSkills();
            if(p.Vendor==null)throw new FormatException("商人设置无效。");p.Vendor.Validate();
            if(p.Repair==null)throw new FormatException("修理NPC设置无效。");p.Repair.Validate();
            if(!NavigationMath.InRange(p.CombatRadius,p.CorridorWidth,.05))throw new FormatException("录点草稿追怪范围无效。");
            if(p.Route.SchemaVersion!=1||p.Route.RouteType!="farm"||!p.Route.Loop||p.Route.MapId!=p.Calibration.MapId||p.Route.MapMode!=p.Calibration.MapMode||p.Route.Zone!=p.Calibration.Zone||!NavigationMath.InRange(p.CorridorWidth,.0005,.02))throw new FormatException("录点草稿地图或范围无效。");
            for(int i=0;i<p.Route.Points.Count;i++)
            {var w=p.Route.Points[i];if(w==null||!NavigationMath.InRange(w.X,0,1)||!NavigationMath.InRange(w.Y,0,1)||!NavigationMath.InRange(w.Tolerance,.0001,.02)||w.Tolerance>p.CorridorWidth/2||w.WaitMilliseconds!=0||w.Action!="wait")throw new FormatException("录点草稿坐标无效。");if(i>0&&w.X==p.Route.Points[i-1].X&&w.Y==p.Route.Points[i-1].Y)throw new FormatException("请移动到下一个位置后再保存巡航点。");}
        }
        static QuickRoutePlan Load(string path,bool recording)
        {
            var root=RouteJson.Object(RouteFiles.Load(path));RouteJson.Keys(root,"SchemaVersion,Route,Calibration,CorridorWidth,CombatRadius,OpeningKeys,LoopKeys,SkillIntervalMs,RestBelowPercent,DrinkKey,Vendor,Repair,UseRogueVanish,AttackHostile,NoChatCombat,StartAttackKey,LastTargetKey,StopAttackKey,SkillRows,RandomRoutes,TimedBreaks,RouteVariation,RandomRouteSeed","CombatRadius,OpeningKeys,LoopKeys,SkillIntervalMs,RestBelowPercent,DrinkKey,Vendor,Repair,UseRogueVanish,AttackHostile,NoChatCombat,StartAttackKey,LastTargetKey,StopAttackKey,SkillRows,RandomRoutes,TimedBreaks,RouteVariation,RandomRouteSeed");
            foreach(string field in new[]{"Vendor","Repair"})if(root.ContainsKey(field)){var s=RouteJson.Object(root[field]);RouteJson.Keys(s,"Enabled,X,Y,Name,Points,RouteComplete","Points,RouteComplete");if(s.ContainsKey("Points")){var ps=s["Points"] as System.Collections.Generic.List<object>;if(ps==null)throw new FormatException("NPC路线列表无效。");foreach(var point in ps)RouteJson.Keys(RouteJson.Object(point),"X,Y,Tolerance,WaitMilliseconds,Action",null);}}
            var r=RouteJson.Object(root["Route"]);RouteJson.Keys(r,"SchemaVersion,RouteId,Name,RouteType,Zone,MapId,MapMode,AllowReverse,Loop,Points",null);
            var c=RouteJson.Object(root["Calibration"]);RouteJson.Keys(c,"AxisScale,FacingSign,FacingOffset,TurnRadiansPerSecond,ForwardUnitsPerSecond,Confirmed,MapId,MapMode,Zone",null);
            var points=r["Points"] as System.Collections.Generic.List<object>;if(points==null)throw new FormatException("缺少路点列表。");foreach(var point in points)RouteJson.Keys(RouteJson.Object(point),"X,Y,Tolerance,WaitMilliseconds,Action",null);
            var plan=new JavaScriptSerializer().Deserialize<QuickRoutePlan>(File.ReadAllText(path,Encoding.UTF8));
            if(plan==null)throw new FormatException("巡航方案为空。");if(!root.ContainsKey("RandomRoutes"))plan.RandomRoutes=true;if(!root.ContainsKey("TimedBreaks"))plan.TimedBreaks=true;if(recording)ValidateRecording(plan);else plan.Validate(false);plan.Calibration.Confirmed=false;return plan;
        }
    }

    public sealed class QuickRouteFollower
    {
        public QuickRoutePlan Plan {get;private set;}
        readonly System.Collections.Generic.List<RouteDocument> variants;
        public int ChosenRoute {get;private set;}public int RouteRevision {get;private set;}
        void ChooseRoute(){ChosenRoute=QuickRandom.Next(variants.Count);Plan.Route=variants[ChosenRoute].Clone();RouteRevision++;}
        public int TargetIndex {get;private set;}public int Laps {get;private set;}
        public bool Returning {get;private set;}
        public bool Joining {get;private set;}
        public bool SearchingNearby {get;private set;}
        double nearbyX,nearbyY;long nearbyUntil=-1;
        RouteDocument serviceReturn;int serviceReturnIndex;
        public bool ReturningFromService {get{return serviceReturn!=null;}}
        double returnX,returnY,progressX,progressY,defenseRadius;long last=-1,progressAt=-1,targetAt=-1;int sequence=-1;bool suspended;
        int escapePhase,escapeAttempts;double escapeFacing;long escapeDeadline;
        public QuickRouteFollower(QuickRoutePlan plan,FarmObservation start)
        {
            plan.Validate(true);Plan=plan.Clone();variants=QuickRouteVariants.For(plan,plan.Route,20);ChooseRoute();
            if(start==null||!start.Fresh||!start.KnownCore||start.ReadFailures!=0||!Plan.MapMatches(start))throw new InvalidOperationException("角色地图状态与路线不匹配。");
            nearbyX=start.X;nearbyY=start.Y;SearchingNearby=true;
            if(start.PlayerDead||start.PlayerGhost){returnX=Plan.Route.Points[0].X;returnY=Plan.Route.Points[0].Y;TargetIndex=1;Returning=true;return;}
            if(!NavigationMath.InRange(start.X,0,1)||!NavigationMath.InRange(start.Y,0,1))throw new InvalidOperationException("当前角色位置不可读。");
            serviceReturn=QuickServicePaths.StartReturn(Plan,start);
            if(serviceReturn!=null){serviceReturnIndex=1;returnX=serviceReturn.Points[1].X;returnY=serviceReturn.Points[1].Y;TargetIndex=1;Returning=Joining=true;SearchingNearby=false;return;}
            if(!Plan.Contains(start.X,start.Y))
            {
                int nearest=0;double distance=Double.PositiveInfinity;
                for(int i=0;i<Plan.Route.Points.Count-1;i++){var w=Plan.Route.Points[i];double d=NavigationMath.Distance(start.X-w.X,start.Y-w.Y,Plan.Calibration.AxisScale);if(d<distance){distance=d;nearest=i;}}
                returnX=Plan.Route.Points[nearest].X;returnY=Plan.Route.Points[nearest].Y;TargetIndex=nearest+1;Returning=Joining=true;return;
            }
            int end;Plan.Project(start.X,start.Y,out returnX,out returnY,out end);
            TargetIndex=end;Returning=NavigationMath.Distance(start.X-returnX,start.Y-returnY,Plan.Calibration.AxisScale)>Tolerance;
        }
        double Tolerance {get{return Math.Min(.001,Plan.CorridorWidth/3);}}
        public bool OnRoute(FarmObservation o){return o!=null&&Plan.MapMatches(o)&&Plan.Contains(o.X,o.Y);}
        bool InNearbyArea(FarmObservation o){return o!=null&&Plan.MapMatches(o)&&NavigationMath.InRange(o.X,0,1)&&NavigationMath.InRange(o.Y,0,1)&&NavigationMath.Distance(o.X-nearbyX,o.Y-nearbyY,Plan.Calibration.AxisScale)<=Plan.CombatRadius;}
        public double PursuitDistance(FarmObservation o){double d=Plan.DistanceToRoute(o.X,o.Y);return SearchingNearby||suspended?Math.Min(d,NavigationMath.Distance(o.X-nearbyX,o.Y-nearbyY,Plan.Calibration.AxisScale)):d;}
        public void BeginNearbySearch(FarmObservation o,long now){if(ReturningFromService)return;nearbyX=o.X;nearbyY=o.Y;SearchingNearby=true;nearbyUntil=now+2500;}
        public void TickNearbySearch(FarmObservation o,long now,bool idle)
        {
            if(ReturningFromService)return;
            if(idle&&!o.InCombat&&!CanOperate(o)&&Plan.MapMatches(o)&&NavigationMath.InRange(o.X,0,1)&&NavigationMath.InRange(o.Y,0,1))JoinNearest(o);
            if(!SearchingNearby||!idle||o.InCombat)return;
            if(nearbyUntil<0)nearbyUntil=now+2500;
            if(now<nearbyUntil)return;
            SearchingNearby=false;
            JoinNearest(o);
        }
        void JoinNearest(FarmObservation o)
        {
            int nearest=0;double distance=Double.PositiveInfinity;
            for(int i=0;i<Plan.Route.Points.Count-1;i++){var w=Plan.Route.Points[i];double d=NavigationMath.Distance(o.X-w.X,o.Y-w.Y,Plan.Calibration.AxisScale);if(d<distance){distance=d;nearest=i;}}
            returnX=Plan.Route.Points[nearest].X;returnY=Plan.Route.Points[nearest].Y;TargetIndex=nearest+1;Returning=Joining=true;suspended=false;progressAt=targetAt=-1;escapePhase=0;
        }
        public bool InCombatArea(FarmObservation o){return o!=null&&Plan.MapMatches(o)&&Plan.WithinCombatArea(o.X,o.Y);}
        public void ObserveCombat(FarmObservation o)
        {if(o!=null&&o.InCombat&&Plan.MapMatches(o)&&NavigationMath.InRange(o.X,0,1)&&NavigationMath.InRange(o.Y,0,1)){double d=Plan.DistanceToRoute(o.X,o.Y);if(d<=.1)defenseRadius=Math.Max(defenseRadius,d+.002);}}
        public bool CanOperate(FarmObservation o)
        {return o!=null&&Plan.MapMatches(o)&&NavigationMath.InRange(o.X,0,1)&&NavigationMath.InRange(o.Y,0,1)&&(Joining||InNearbyArea(o)&&(SearchingNearby||suspended)||o.InCombat&&Plan.DistanceToRoute(o.X,o.Y)<=.1||InCombatArea(o)&&(OnRoute(o)||Returning||suspended)|| (Returning||suspended)&&Plan.DistanceToRoute(o.X,o.Y)<=defenseRadius);}
        public bool TargetAllowed(FarmObservation o)
        {return o!=null&&Plan.MapMatches(o)&&(o.InCombat&&CanOperate(o)||(InCombatArea(o)||InNearbyArea(o)&&(SearchingNearby||suspended))&&(!o.TargetPositionKnown||Plan.WithinCombatArea(o.TargetX,o.TargetY)||NavigationMath.Distance(o.TargetX-nearbyX,o.TargetY-nearbyY,Plan.Calibration.AxisScale)<=Plan.CombatRadius));}
        public bool MayAcquire(FarmObservation o)
        {return TargetAllowed(o)&&(o.InCombat||!ReturningFromService&&(SearchingNearby||OnRoute(o)&&!Returning&&!suspended));}
        public void Interrupt(FarmObservation o)
        {
            escapePhase=0;if(!suspended){if(!Returning)Plan.ProjectSegment(o.X,o.Y,TargetIndex,out returnX,out returnY);suspended=true;}
        }
        public void Resume(){if(suspended){Returning=true;suspended=false;progressAt=targetAt=-1;}}
        public QuickCommand Step(FarmObservation o,long now)
        {
            if(now<0||now<last)return C(QuickAction.Stop,"路线时钟异常。",0,0);last=now;
            if(!o.Fresh||!o.KnownCore||o.ReadFailures!=0||!Plan.MapMatches(o)||!NavigationMath.Finite(o.Facing))return C(QuickAction.Stop,"路线位置或地图状态不可验证。",0,0);
            if(!CanOperate(o))return C(QuickAction.Stop,"角色超出巡航或追怪返回范围，已停止。",0,0);
            if(sequence==o.Sequence)return C(QuickAction.None,"等待路线位置更新。",0,0);sequence=o.Sequence;
            if(suspended)Resume();
            if(targetAt<0)targetAt=now;if(now-targetAt>120000)return C(QuickAction.Stop,"两分钟未到达下一个路点，请检查路线与移动标定。",0,0);
            if(progressAt<0){progressAt=now;progressX=o.X;progressY=o.Y;}
            if(NavigationMath.Distance(o.X-progressX,o.Y-progressY,Plan.Calibration.AxisScale)>.0002){progressAt=now;progressX=o.X;progressY=o.Y;}
            if(escapePhase==1){escapePhase=2;escapeFacing=o.Facing;escapeDeadline=now+3000;var mouse=C(QuickAction.DetourTurn,"巡航脱困：按住鼠标右键换视角及角色方向。",(ushort)(escapeAttempts%2==1?39:37),0);mouse.MouseTurn=true;return mouse;}
            if(escapePhase==2){if(!MouseLookInput.TurnObserved(escapeFacing,o.Facing))return now<escapeDeadline?C(QuickAction.None,"等待确认实际转向后再试走。",0,0):C(QuickAction.Stop,"脱困没有观察到角色转向，停止检查右键控制。",0,0);escapePhase=0;progressAt=now;return C(QuickAction.DetourForward,"巡航脱困：已确认换方向，短距离试走后继续原路线。",87,650);}
            if(now-progressAt>2500){if(++escapeAttempts>3)return C(QuickAction.Stop,"三次换向仍受阻，停止检查障碍或增加绕行路点。",0,0);escapePhase=1;progressAt=now;return C(QuickAction.BackOff,"前进无实际位移：先后退，再右键换方向绕开障碍。",83,250);}
            double tx=Returning?returnX:Plan.Route.Points[TargetIndex].X,ty=Returning?returnY:Plan.Route.Points[TargetIndex].Y;
            double tolerance=ReturningFromService?serviceReturn.Points[serviceReturnIndex].Tolerance:Returning?Tolerance:Plan.Route.Points[TargetIndex].Tolerance;
            if(NavigationMath.Distance(tx-o.X,ty-o.Y,Plan.Calibration.AxisScale)<=tolerance)
            {
                progressAt=targetAt=now;escapeAttempts=0;
                if(ReturningFromService)
                {
                    serviceReturnIndex++;
                    if(serviceReturnIndex<serviceReturn.Points.Count){returnX=serviceReturn.Points[serviceReturnIndex].X;returnY=serviceReturn.Points[serviceReturnIndex].Y;return C(QuickAction.None,"沿录制NPC路线逆序返回打怪区域。",0,0);}
                    serviceReturn=null;JoinNearest(o);
                    return C(QuickAction.None,"已沿NPC路线返回起点，接入打怪巡航。",0,0);
                }
                if(Returning){Returning=Joining=false;defenseRadius=0;}
                else {TargetIndex++;if(TargetIndex>=Plan.Route.Points.Count){TargetIndex=1;Laps++;ChooseRoute();}}
                tx=Plan.Route.Points[TargetIndex].X;ty=Plan.Route.Points[TargetIndex].Y;
            }
            var calibration=Plan.Calibration;
            double bearing=Math.Atan2((tx-o.X)*calibration.AxisScale,-(ty-o.Y));
            double facing=NavigationMath.Normalize(calibration.FacingSign*o.Facing+calibration.FacingOffset),delta=NavigationMath.Normalize(bearing-facing);
            string label=(ReturningFromService?"沿录制NPC路线原路返回":Joining?"前往最近巡航点接入路线":Returning?"返回打怪前的路线位置":"沿指定路线前往路点"+(TargetIndex+1))+"，已完成"+Laps+"圈。";
            if(Math.Abs(delta)>.12){var turn=C(QuickAction.Turn,label,(ushort)(delta>0?39:37),Math.Max(40,Math.Min(120,(int)(Math.Abs(delta)/calibration.TurnRadiansPerSecond*1000))));turn.PauseForward=ReturningFromService||Math.Abs(delta)>.7||NavigationMath.Distance(tx-o.X,ty-o.Y,calibration.AxisScale)<.003&&Math.Abs(delta)>.2;return turn;}
            if(ReturningFromService){var forward=C(QuickAction.Patrol,label,87,Math.Max(60,Math.Min(3000,(int)((NavigationMath.Distance(tx-o.X,ty-o.Y,calibration.AxisScale)-tolerance*.5)/calibration.ForwardUnitsPerSecond*1000))));forward.PauseForward=true;return forward;}
            return C(QuickAction.Patrol,label,87,1000);
        }
        QuickCommand C(QuickAction a,string text,ushort key,int hold){return new QuickCommand{Action=a,Reason=text,Key=key,Hold=hold,RouteMovement=true,RouteContext=this};}
    }
}
