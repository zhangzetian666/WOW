# 第三阶段模块接口约定

> v0.5.0兼容说明：新增WO4位于WP1右侧544px，头像参考区右移，默认客户区区域为(832,8,64,64)。WC3格式不变，以包内实时ROI为准。下文阶段三默认位置属于v0.3.0布局，完整版本接口见 [complete-interfaces.md](complete-interfaces.md)。

C#5、命名空间WoWProbe。已有WP1保持不变。新代码只消费明确能力，未知不得当作false或0。

## 0.5.14 WC3版本4增量

Windows客户端兼容WC3版本1、2、3、4；面板仍128字节，位置及Fletcher16不变。版本2增加5/6号当前动作页面槽位的独立射程，版本3增加当前目标的原生距离错误反馈。版本4增加5/6/7冷却与可用状态及7号射程。猎人模式要求版本4，避免把旧插件缺少的字段当作可用。

| offset | 增量语义 |
|---|---|
|3|版本号4；读取器仍接受1/2/3|
|17..19|版本4低七位分别为1–7号动作已知、冷却就绪、可用；旧版本仍限低四位。冷却包括公共冷却，未知不伪造为已就绪|
|121|低四位=1–4号射程已知，高四位=对应射程内|
|122|低四位=1–4号自动重复已知，高四位=对应已开启|
|123|低四位=1–4号远程技能分类；位16/32/64=5/6/7号射程已知|
|124|低四位=1–4号近战技能分类；位16/32/64=5/6/7号射程内，必须有对应已知位|
|125|位1/2保留鼠标目标核验；版本3位4=同一当前目标最近2秒收到距离太远UI_ERROR_MESSAGE；非零必须具有有效目标身份|

距离错误来自游戏本地化错误事件（含旧/新事件参数形式），按目标GUID哈希关联、2秒过期，切换目标不继承该错误。恢复时不把错误过期单独当作已经进入射程；控制器还要求实际位移及当前技能射程明确为真。该错误不是直接OCR屏幕红字，也不读取游戏进程内存。

## WC3扩展 CombatTelemetry.cs 与插件

第二个128字节面板位于WP1首色块向右272物理像素，布局仍32×12×8像素。两包同一个200ms更新序号；共同截屏，序号不符不供战斗使用。WP1旧读取继续可用。

| offset | 字段 |
|---|---|
|0..3|ASCII WC3，版本1/2/3/4：87,67,51,version|
|4..5|匹配WP1的sequence u16|
|6|capabilities：身份1、攻击性2、射程4、目标坐标8、可拾取16、可剥皮32、读条64、动作条128|
|7|flags：可攻击1、玩家2、射程内4、可拾取8、可剥皮16、施法32、引导64；bit128保留0|
|8..11|target_token：UnitGUID UTF8的djb2 u32哈希，0未知；哈希0映射1|
|12..15|target_x/y，WP1同样归一化u16，65535未知；只在可证实同地图的公开API结果下输出|
|16|range_slot1..4，默认1，/wowprobe range n可设置|
|17..19|actions_known_mask / ready_mask / usable_mask，v1–3为bit0..3对应动作条1..4；v4为bit0..6对应动作条1..7|
|20..23|cast_end_ms u32，仅诊断|
|24..27|loot_opened计数u32|
|28..31|loot_closed计数u32|
|32..35|cast_success计数u32|
|36..39|cast_failed计数u32|
|40..43|skin_success计数u32，按本地化剥皮名称识别，不能用任意施法代替|
|44..47|target_name_hash，完整UnitName UTF8 djb2 u32，与显示名称是否截断无关|
|48..55|目标头像client相对物理像素x,y,width,height u16，仅参考图保存|
|56|extra_known：头像1、skin事件2、完整名称哈希4、loot事件8、cast事件16、死亡事件32|
|57..60|最后确认死亡NPC的token u32；没有事件为0|
|61..64|去重目标死亡事件计数u32；未声明死亡事件能力时两项均为0|
|65|聊天输入框：known位1、focused位2；0未知、1已知关闭、3已知打开|
|66..69|最后第一人称战斗经验消息怪物完整名称哈希u32|
|70..73|第一人称战斗经验击杀消息计数u32；只接受客户端原生格式|
|74|经验击杀消息能力known：0/1；未知时哈希及计数为0|
|75|7号剥皮绑定：known位1、correct位2；核对技能本地化名称及7按键|
|76|12号食物槽物品数量known：0/1|
|77..78|食物数量u16，known时0表示耗尽；未知时字段0，上层FoodCount=-1|
|79..125|后续扩展为目标归属、对话、耐久、修理、普攻、潜行、交互和逐技能射程；121–125最新定义见上表，不再全是保留字节|
|126..127|Fletcher16，同WP1|

Codec拒绝未知true位、非法范围、未声明却非零的字段、动作mask不一致、保留字节和校验错误。坐标不能通过旧GetPlayerMapPosition("target")的猜测取得；可检测C_Map同map目标坐标，API不支持就unknown。射程使用所选动作条IsActionInRange。动作条冷却与可用性采用当前页面的真实槽位，缺API不假称ready。剥皮可检测目标tooltip的本地化可剥皮标记；无法读取时unknown，不以野兽类型猜测。事件参数按客户端差异保守处理。

0.7.2附近入口需配套0.5.2插件。Forever血量API不可直接算百分比时，WP1原值仍为255；仅附近入口在同次截图读取插件原生StatusBar的绿色/蓝色填充，明确标注VisualHealth/VisualPower，不改写原始协议能力。参考200px宽血条位于WP1相对物理坐标y120及138，定位标记和连续填充不合法时上层仍未知。剥皮优先读取C_TooltipInfo目标提示，使用UNIT_SKINNABLE_LEATHER，旧客户端回退UNIT_SKINNABLE及隐藏GameTooltip。

CombatPacket.Decode(byte[])->CombatPacket，属性：Sequence,Capabilities,Flags,RangeSlot,KnownActionsMask,ReadyActionsMask,UsableActionsMask,ExtraKnownFlags(int)；TargetToken,TargetNameHash,CastEndMilliseconds,LootOpened,LootClosed,CastSuccess,CastFailed,SkinSuccess(uint)；TargetX/TargetY(double NaN未知)；Portrait(Rectangle)。

FarmObservation public字段：Sequence,ReadFailures,Health,Power,PowerType,TargetHealth,MapMode,RangeSlot,KnownActionsMask,ReadyActionsMask,UsableActionsMask(int)；MapId,TargetToken,TargetNameHash,LootOpened,LootClosed,CastSuccess,CastFailed,SkinSuccess(uint)；Zone,TargetName(string)；X,Y,Facing,TargetX,TargetY(double)；Portrait(Rectangle)；Fresh,KnownCore,PlayerDead,PlayerGhost,InCombat,BagFull,TargetExists,TargetDeadKnown,TargetDead,TargetIdentityKnown,NameHashKnown,AttackabilityKnown,TargetCanAttack,TargetIsPlayer,RangeKnown,TargetInRange,TargetPositionKnown,LootKnown,Lootable,SkinKnown,Skinnable,CastingKnown,Casting,Channeling,ActionsKnown,LootEventsKnown,CastEventsKnown,SkinSuccessKnown,PortraitKnown(bool)。
FarmObservation.FromPackets(ProbePacket core,CombatPacket extra,bool fresh)->FarmObservation：fresh还要求两包非空且序号相等，位置/朝向/地图/背包能力可读（core capabilities含83），背包满提示状态已知（core known位128），坐标和朝向为有限值；不把未知能力当正常。BagFull在已知背包空位为0或已知背包满提示为true时为true。TargetExists依据core known位8；KnownCore仍仅要求core known7及target-exists known8。

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
