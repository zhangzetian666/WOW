# 第二阶段模块接口

命名空间 WoWProbe；C#5，public 类型。本文件在并行开发期间作为接口约定，所有数据均按当前地图归一化坐标表示。不可用值必须拒绝，不用0替代。

## 路线数据 Routes.cs

Waypoint: public double X,Y,Tolerance=0.003; public int WaitMilliseconds; public string Action="wait"; Clone() -> Waypoint.

RouteDocument: public int SchemaVersion=1; public string RouteId,Name,RouteType="farm",Zone=""; public uint MapId; public int MapMode; public bool AllowReverse=true,Loop; public List<Waypoint> Points; Validate(); Clone()->RouteDocument; Reversed()->RouteDocument; Fingerprint()->string.

Validate: id/name非空长度<=128；type只允许farm/corpse_run/vendor_return；mapId>0，MapMode1..3，Zone非空<=128；点数2..5000，至少两处不同位置；X/Y为有限0..1；Tolerance有限0.0001..0.02；WaitMilliseconds0..60000；Action只允许scan/wait/interact/turn_point。连续重复点允许作为显式等待，但全部重复路线拒绝。

Reversed: 原对象不变，须AllowReverse；返回倒序点，interact动作改wait且WaitMilliseconds=0；其他动作保留；重新Clone不改变路线身份。Fingerprint须覆盖所有导航相关字段和每个路点，排除创建时刻等非行为元数据。

RouteStore.Save(string path,RouteDocument route); RouteStore.Load(string path)->RouteDocument. JSON字段采用snake_case：schema_version,route_id,name,route_type,map_id,map_mode,zone,reverse_policy("allowed"/"forbidden"),loop,points[{x,y,tol,wait_ms,action}]。写入先临时文件再原子替换；加载文件<=2MB，限制递归，拒绝未知schema、非法数字或缺字段。name可默认route_id但其他身份字段不可猜。

RouteRecorder.Start(ProbePacket first,bool fresh,string name,string routeType); Add(ProbePacket packet,bool fresh)->bool; Stop()->RouteDocument; Cancel(); IsRecording(bool),Count(int),Snapshot(RouteDocument克隆或null)。Start自动记录第一点；Add必须当前数据fresh、position/map能力可用且ReadFailures=0、MapId/MapMode/Zone一致；相邻近似重复点距离<0.00002返回false；地图不符抛异常；Stop不足2点拒绝并保持录制以便继续。

RouteCheckpoint: public string RouteFingerprint; public int NextIndex; public bool Reverse,Loop,Completed; public uint MapId; public int MapMode; public string Zone.
CheckpointStore.Save(string path,RouteCheckpoint checkpoint); Load(string path,RouteDocument original,bool reverse,bool loop)->RouteCheckpoint. 校验fingerprint与原始路线、地图模式/区域、方向/循环一致，index0..points.Count，Completed须与index==points.Count一致；完成的断点不用于自动续跑。

## 导航 Navigation.cs

NavigationCalibration: public double AxisScale=1, FacingSign=-1, FacingOffset, TurnRadiansPerSecond=2, ForwardUnitsPerSecond=0.01; public bool Confirmed; public uint MapId; public int MapMode; public string Zone=""; Validate(); Clone(). AxisScale0.1..10；FacingSign只允许-1或1；FacingOffset有限；turn0.1..10；forward0.0001..0.2。Confirmed不影响模拟，只是实机输入额外门槛；地图绑定须匹配路线。

NavigationSample: public double X,Y,Facing; public uint MapId; public int MapMode,Sequence; public string Zone; public bool Fresh,KnownCore,Dead,Ghost,Combat,BagFull; public int ReadFailures; static FromPacket(ProbePacket,bool fresh)->NavigationSample。position/facing/map能力不足时Fresh=false；KnownCore要求KnownFlags&7=7。

NavigationAction enum: None,Forward,TurnLeft,TurnRight,StrafeLeft,StrafeRight,Jump,Stop,Completed.
NavigationCommand: public NavigationAction Action; public int HoldMilliseconds,TargetIndex; public string Reason.

Navigator(RouteDocument original,NavigationCalibration calibration,bool reverse,bool loop,int startIndex). Step(NavigationSample sample,long nowMs)->NavigationCommand; Stop(string reason); TargetIndex(int),Status(string),RecoveryAttempts(int),Completed(bool),WorkingRoute(RouteDocument克隆),LastReason(string).

Step只在新数据帧规划动作。重复Sequence返回None，不推进到点次数或脱困计数，但始终检查fresh/危险状态/时间。计算compass方位atan2((xt-x)*AxisScale,-(yt-y))；当前compass角Normalize(FacingSign*rawFacing+FacingOffset)。delta=Normalize(target-current)，delta>0发TurnRight，否则Left。角度阈值0.12rad，转向60..250ms；前进60..200ms，按ForwardUnitsPerSecond估算接近时间避免过冲。距离=sqrt((dx*AxisScale)^2+dy^2)，到点容差连续两个不同Sequence确认。WaitMs到期再进入下一点；scan/interact只记保留动作，不发送动作条或交互键。

支持反向（调用original.Reversed）与loop；不允许反向路线时构造拒绝。死亡/灵魂/战斗/包满、unknown核心状态、ReadFailures非0、fresh=false、地图模式/区域不匹配、无效坐标/朝向、单节点超过120秒或时钟倒退均返回Stop并锁定直到新建Navigator。已完成返回Completed，不能自动重启。

脱困：连续前进而累计2秒位移<0.0001时，按次序给Jump120、StrafeLeft或Right200、退回上一已到过路点（没有上一点则停止），继续尝试当前目标；最多3次恢复后停止。不得因为转向、等待或重复帧误判卡住。具体恢复阶段可以合并为有界命令，但不得跳过堵塞节点直接继续。

NavigationSimulator(RouteDocument route,NavigationCalibration calibration,bool reverse,bool loop,int startIndex). Sample()->NavigationSample; Apply(NavigationCommand command); Advance(int ms); X,Y,Facing(double),ElapsedMilliseconds(long),InputEvents(int恒为0)。使用同一坐标/方位换算，作用于虚拟坐标，不包含Native/IInputBackend。Apply可使时钟推进HoldMilliseconds；None由调用方Advance(200)。Sample每次产生递增Sequence，时钟与所有状态独立于真实游戏；MapId/MapMode/Zone取路线绑定。

## 会话 RouteSession.cs

由root实现；消费Navigator输出并控制一个IInputBackend。Begin显式倒计时5000ms；实际脉冲60..250ms，由独立25ms Tick释放；发下一命令前等待新的有效Sequence；每个Tick持续检查状态与前台。与第一阶段单次输入互斥，停止/异常只释放本会话拥有的键。
