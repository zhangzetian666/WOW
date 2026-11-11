# 第三阶段模块接口约定

C#5、命名空间WoWProbe。已有WP1保持不变。新代码只消费明确能力，未知不得当作false或0。

## WC3扩展 CombatTelemetry.cs 与插件

第二个128字节面板位于WP1首色块向右272物理像素，布局仍32×12×8像素。两包同一个200ms更新序号；共同截屏，序号不符不供战斗使用。WP1旧读取继续可用。

| offset | 字段 |
|---|---|
|0..3|ASCII WC3，version1：87,67,51,1|
|4..5|匹配WP1的sequence u16|
|6|capabilities：身份1、攻击性2、射程4、目标坐标8、可拾取16、可剥皮32、读条64、动作条128|
|7|flags：可攻击1、玩家2、射程内4、可拾取8、可剥皮16、施法32、引导64；bit128保留0|
|8..11|target_token：UnitGUID UTF8的djb2 u32哈希，0未知；哈希0映射1|
|12..15|target_x/y，WP1同样归一化u16，65535未知；只在可证实同地图的公开API结果下输出|
|16|range_slot1..4，默认1，/wowprobe range n可设置|
|17..19|actions_known_mask / ready_mask / usable_mask，均bit0..3对应动作条1..4|
|20..23|cast_end_ms u32，仅诊断|
|24..27|loot_opened计数u32|
|28..31|loot_closed计数u32|
|32..35|cast_success计数u32|
|36..39|cast_failed计数u32|
|40..43|skin_success计数u32，按本地化剥皮名称识别，不能用任意施法代替|
|44..47|target_name_hash，完整UnitName UTF8 djb2 u32，与显示名称是否截断无关|
|48..55|目标头像client相对物理像素x,y,width,height u16，仅参考图保存|
|56|extra_known：头像1、skin事件2、完整名称哈希4、loot事件8、cast事件16|
|57..125|保留0|
|126..127|Fletcher16，同WP1|

Codec拒绝未知true位、非法范围、未声明却非零的字段、动作mask不一致、保留字节和校验错误。坐标不能通过旧GetPlayerMapPosition("target")的猜测取得；可检测C_Map同map目标坐标，API不支持就unknown。射程使用所选动作条IsActionInRange。动作条冷却与可用性采用当前页面的真实槽位，缺API不假称ready。剥皮可检测目标tooltip的本地化可剥皮标记；无法读取时unknown，不以野兽类型猜测。事件参数按客户端差异保守处理。

CombatPacket.Decode(byte[])->CombatPacket，属性：Sequence,Capabilities,Flags,RangeSlot,KnownActionsMask,ReadyActionsMask,UsableActionsMask,ExtraKnownFlags(int)；TargetToken,TargetNameHash,CastEndMilliseconds,LootOpened,LootClosed,CastSuccess,CastFailed,SkinSuccess(uint)；TargetX/TargetY(double NaN未知)；Portrait(Rectangle)。

FarmObservation public字段：Sequence,ReadFailures,Health,Power,PowerType,TargetHealth,MapMode,RangeSlot,KnownActionsMask,ReadyActionsMask,UsableActionsMask(int)；MapId,TargetToken,TargetNameHash,LootOpened,LootClosed,CastSuccess,CastFailed,SkinSuccess(uint)；Zone,TargetName(string)；X,Y,Facing,TargetX,TargetY(double)；Portrait(Rectangle)；Fresh,KnownCore,PlayerDead,PlayerGhost,InCombat,BagFull,TargetExists,TargetDeadKnown,TargetDead,TargetIdentityKnown,NameHashKnown,AttackabilityKnown,TargetCanAttack,TargetIsPlayer,RangeKnown,TargetInRange,TargetPositionKnown,LootKnown,Lootable,SkinKnown,Skinnable,CastingKnown,Casting,Channeling,ActionsKnown,LootEventsKnown,CastEventsKnown,SkinSuccessKnown,PortraitKnown(bool)。
FarmObservation.FromPackets(ProbePacket core,CombatPacket extra,bool fresh)->FarmObservation：fresh还要求两包非空且序号相等，位置/朝向/地图能力可读；不把未知能力当正常。TargetExists依据core known位8；KnownCore要求core known7及target-exists known8。

## 目标与配置 BattleProfiles.cs

BattleTarget public字段：Name(string),NameHash(uint),Kind="monster"(monster或npc),AvatarFile=""(相对路径或空),SavedAtUtc=""(string)；Clone(),Validate()。
TargetCatalog.HashName(string)->uint：UTF8字节djb2初值5381，每字节hash=hash*33+byte，u32回绕，结果0映射1；Match(BattleProfile,FarmObservation)->BattleTarget或null，仅匹配monster、完整名称hash一致且可攻击非玩家、身份可用、活目标；名称显示截断不导致同前缀误匹配。

BattleProfile public字段：SchemaVersion=1(int),Name="默认"(string),Targets(List<BattleTarget>),SkillSequence="112"(1..4字符1..64),SkillIntervalMs=1500,ScanLimit=10,ScanIntervalMs=2000,ApproachTimeoutMs=12000,CombatTimeoutMs=60000,LootTimeoutMs=4000,SkinWaitMs=1500,SkinTimeoutMs=6000,RestTimeoutMs=25000,HealBelow=40,ManaBelow=35,RecoverTo=85,CriticalHealth=15,RangeSlot=1(int)；Skinning,AllowApproach,ApproachPositionConfirmed,BindingsConfirmed(bool)；Calibration(NavigationCalibration)；Movement(RouteKeyBindings)；Validate(),Clone()。
动作键固定：TAB=9，技能1..4=VK0x31..34，交互F=0x46，剥皮5=0x35，食物8=0x38，水9=0x39。Movement仍提供已验证的转向/平移组合。实时启动额外要求BindingsConfirmed与Calibration.Confirmed，AllowApproach时还要求ApproachPositionConfirmed。纯模拟忽略确认标志。
校验边界：targets1..200，Name1..128，targetName1..128，hash必须等于Name全名哈希（无手填不一致）；avatar只允许相对avatars/*.png，不允许..、盘符、绝对路径；同类同hash不可重复。SkillInterval1000..5000；ScanLimit1..20；ScanInterval500..10000；Approach1..60秒；Combat5..180秒；Loot1..15秒；SkinWait1500..10000，SkinTimeout>=SkinWait且<=30000；Rest5..120秒；Critical1..40，Heal>Critical且<=80，Mana1..80，RecoverTo>max(Heal,Mana)且<=100。RangeSlot1..4。校准/Movement均Validate。
BattleProfileStore.Save(string,BattleProfile)原子UTF8 JSON；Load(string)->BattleProfile，schema_version1，文件<=1MB，拒绝非法/缺字段，中文与数值文化独立。实现可复用Routes内部严格JSON工具。提供profiles/examples/simulation-battle.json，模拟地图900001/模式2/Zone="Simulation only"，一条怪物“演示野兽”；BindingsConfirmed和Calibration.Confirmed=false、AllowApproach=true、ApproachPositionConfirmed=false。模拟路线同现有routes/examples/simulation-square.json绑定。

## 状态机 Farming.cs 与模拟 FarmSimulator.cs

FarmAction enum：None,Forward,TurnLeft,TurnRight,StrafeLeft,StrafeRight,Jump,SelectTarget,Skill1,Skill2,Skill3,Skill4,Interact,Skin,Heal,Mana,Stop,Completed。
FarmCommand public字段：Action(FarmAction),HoldMilliseconds(int),ExpectedTargetToken(uint),RequireDeadTarget,RequireAliveTarget(bool),Reason(string)。无动作/停止/完成hold0，其他60..250ms，按键一律有界。
FarmEngine(RouteDocument route,BattleProfile profile,bool reverse,bool loop,int startIndex)；Step(FarmObservation,long nowMs)->FarmCommand；Stop(string)；Status,LastReason(string),RouteIndex(int),LockedTargetToken(uint),EncounterActive,Completed(bool),WorkingRoute(RouteDocument clone)。纯函数式步进，无Native或IInputBackend。

流程：Patrol复用Navigator；每ScanInterval暂停巡航TAB扫描，最多ScanLimit后续跑。命中保存且可攻击非玩家目标后锁身份；射程内进入Combat，否则仅在AllowApproach且TargetPositionKnown、RangeKnown和当前范围槽位匹配时按标定方位短按接近，距离上限0.02归一化；未知方向/距离、不可进展和超时停止。Combat等待读条结束与动作条known/ready/usable，按SkillSequence循环，跳过未就绪键，遵守SkillInterval；3秒无可用能力或10秒无施法/血量变化证据、总战斗超时停止。目标死亡需两个不同帧；身份改变/消失不能当死亡。Loot按F后用loot_opened/closed新事件确认；必要时只允许一次重试；可剥皮known=false就跳过，unknown且用户启用时停止，known=true发5并等至少SkinWait及结束证据。脱战后血/法力低于阈值发8/9各一次，达到RecoverTo继续，超时暂停；非mana职业忽略法力阈值。危险低血Critical停止。死亡、灵魂、包满、地图变化、核心unknown/fresh=false立即停止；意外战斗(未锁已保存目标)停止。重复Sequence不能重复发动作或确认死亡/拾取；检查时间与危险状态不能跳过。继续巡航恢复保存RouteIndex，超过安全接入距离0.02停止；不生成未知长捷径。

FarmSimulator(RouteDocument,BattleProfile,bool reverse,bool loop,int startIndex)；Sample()->FarmObservation；Apply(FarmCommand)；Advance(int ms)；ElapsedMilliseconds(long),InputEvents(int恒0),Kills,SkillCasts,Loots,Skins,Heals,Drinks(int)，X,Y(double)；MissingTargetPosition,NoSupplies(bool settable)。提供虚拟怪物、目标筛选、射程、攻击、死亡、瞬时loot事件、剥皮读条、食物水恢复；没有真实输入或API。默认先低血/低蓝展示恢复，之后目标在射程外以展示接近，再完成一次完整击杀拾取剥皮并巡航结束。所有新样本Sequence递增。模拟与真实观测不混用。

## root集成

FarmSession采用独立25ms监控，所有模式共享InputFailureLatch。首次5秒倒计时；每次短按释放后等新帧；攻击/拾取/剥皮在发送前再次校验ExpectedTargetToken与生死条件，回调取消后不可继续发送。离线模拟、路线、单次测试、真实打怪互斥。UI提供目标与参考头像、路线/配置、标定、模拟、真实启动与报告；缺扩展包不影响旧WP1读取，但禁止实机打怪。
