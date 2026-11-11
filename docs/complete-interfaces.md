# 完整版本精确接口约定

C#5，namespace WoWProbe，DTO 使用 public 字段。WP1/WC3不变；新WO4必须同序号配对。未知=false-known，不许把缺失数据替成可用。

## 公共DTO与WO4（遥测任务所有）

`OperationPurpose { Farm, Travel, Release, Resurrect, NpcSelect, MerchantOpen, BagsOpen, Sell, CloseUi, RecoveryRest }`

`OperationAction { None, Key, LeftClick, RightClick, Stop, Completed, ResumeFarm }`

`OperationCommand` 字段：Action(OperationAction), Purpose(OperationPurpose), Key(ushort), HoldMilliseconds(int), Region(Rectangle), ExpectedTargetToken,ExpectedNameHash,ExpectedItemId,ExpectedItemFingerprint,ExpectedInventoryRevision(uint), ExpectedItemCount,Bag,Slot(int), Farm(FarmCommand 或null), Reason(string)。无输入命令hold=0，所有输入60..250ms。Farm仅在Purpose.Farm时存在；键盘用既有FarmCommand或显式Key。

`InventoryItem` 字段：Bag,Slot,Count,Quality(int),ItemId,Fingerprint,UnitPrice(uint), Known,Empty,Locked,QuestKnown,Quest,TypeKnown,Consumable,RectangleKnown(bool), Region(Rectangle)。Clone()。

`OperationObservation` 字段：Farm(FarmObservation), Sequence,FreeSlots,ClientWidth,ClientHeight,RecoveryDelayMs,PageIndex,TotalSlots(int), MapId,LayoutToken,InventoryRevision,BagFullEvents,MerchantOpened,MerchantClosed,InventoryCycle,MerchantTargetToken,MerchantNameHash(uint), CorpseX,CorpseY(double), Item(InventoryItem), BagSizes(byte[5]), Occupancy(byte[25]);

bool：Fresh,CorpseKnown,FreeSlotsKnown,GeometryKnown,ReleaseKnown,ReleaseVisible,ReleaseEnabled,ResurrectKnown,ResurrectVisible,ResurrectEnabled,RecoveryDelayKnown,MerchantKnown,MerchantOpen,CursorKnown,CursorEmpty,ConfirmationKnown,ConfirmationVisible,IncomingResurrectionKnown,IncomingResurrection,InventoryKnown,InventoryEventsKnown,InventoryCycleKnown,SellTabKnown,SellTab；ReleaseRegion,ResurrectRegion(Rectangle)。

提供 Clone() 深拷贝（FarmObservation可MemberwiseClone或手动复制），SlotOccupied(int bag,int slot)->bool（无效索引抛ArgumentOutOfRangeException；必须先检查InventoryKnown），FromPackets(ProbePacket,CombatPacket,OperationsPacket,bool fresh)->OperationObservation。FromPackets 要三包同序号、旧Farm.Fresh，并提取WP1 corpse cap128、空位cap16及merchant基本状态；任何包缺失Fresh=false。`OperationsPacket.Decode(byte[])->OperationsPacket` 后可提供 `ApplyTo(OperationObservation)` 进行字段填充。

### WO4：128字节

物理位置在WP1右侧544px（68个cell），WO4 32×12×8；整体采集宽度100cell。WC3头像右移至WP1+824px，默认client区域(832,8,64,64)，不重叠。WP1/WC3旧单独读取仍可用。

| offset | 字段 |
|---|---|
|0..3|87,79,52,1 (WO4 v1)|
|4..5|u16 sequence|
|6..7|u16 capabilities：Release1，Resurrect2，RecoveryDelay4，Merchant8，Inventory16，Cursor32，Confirmation64，Geometry128，BagEvents256，IncomingResurrection512，ItemRectangle1024，InventoryCycle2048，SellTab4096；其余0|
|8..9|u16 flags：ReleaseVisible1，ReleaseEnabled2，ResurrectVisible4，ResurrectEnabled8，MerchantOpen16，CursorEmpty32，ConfirmationVisible64，IncomingResurrection128，SellTab256；其余0|
|10..17|release x/y/w/h u16 physical client rectangle；不可见/未知则0|
|18..25|resurrect x/y/w/h，同上|
|26..29|recovery_delay_ms u32；未知FFFFFFFF；可用0..3600000|
|30..33|client_width,height u16|
|34..37|layout_token u32：尺寸/缩放签名；unknown0|
|38..41|inventory_revision u32：BAG_UPDATE等事件计数|
|42..45|bag_full_events u32：明确背包已满错误计数|
|46..53|merchant_opened/closed u32|
|54..57|inventory_cycle u32：逐格循环完整轮序号|
|58..61|page_index / total_slots u16|
|62..63|当前 bag 0..4，slot1..40；无Inventory则0/0|
|64..67|item_id u32；空格0|
|68..69|item_count u16；空格0|
|70|quality0..7或未知/空格255|
|71|item_flags：Locked1，Quest2，Consumable4，MetadataKnown8，Empty16，QuestKnown32，TypeKnown64；其余0|
|72..75|unit_sell_price u32；未知FFFFFFFF；空格0|
|76..79|slot_fingerprint u32：bag/slot/完整link/id/count/quality等签名；未知0|
|80..87|item x/y/w/h u16 physical client rectangle；unknown0|
|88..95|merchant_target_token / merchant_name_hash u32；不在商店/未知则0|
|96..100|bags0..4槽数各u8，最大40；任一>40则不发布Inventory|
|101..125|5个bag，每个5字节占用位图；bit0表示slot1；超出实际bag大小的位必须0|
|126..127|Fletcher16，同WP1|

Inventory为当前页+全包占用图；只循环只读采集，不调用使用/销售/删除API。完整元数据未缓存时Item.Known=false，不能出售。没有可验证可见格子时RectangleKnown=false。Quest和Consumable分类无法证明时各Known=false。商店必须确认出售页、无鼠标携带物品、无确认框。确认框不由程序点击。

释放/复活依赖已知StaticPopup类型和可见可用按钮，复活需已知冷却0；来人复活邀请停止自动点击。缺API时保留unknown。现代/旧版容器API适配均须测试。事件注册失败不发布相应Known。WP1.ReadFailures不被WO4缺能力污染（WO4自己的未知由监督器处理）。

## 配置与计划（root所有）

`CampaignSettings` 字段默认：SchemaVersion=1,Name="完整流程", EnableRecovery=true,EnableVendor=true, BindingsConfirmed=false,UiConfirmed=false,AutoResumeConfirmed=false(bool), ClientWidth=1920,ClientHeight=1080(int), JoinTolerance=.006,CorpseTolerance=.006(double), DeathConfirmFrames=2,ReleaseTimeoutMs=15000,CorpseTimeoutMs=300000,ResurrectTimeoutMs=15000,VendorTimeoutMs=180000,NpcScanLimit=10,NpcScanIntervalMs=1000,MinimumFreeSlots=2,SaleLimit=100,SaleTimeoutMs=5000,InventoryTimeoutMs=120000,AutoCleanTimeoutMs=10000(int), NpcSelectKey=0x75(ushort,F6，用户自行绑定目标NPC宏或选取友方键), AutoCleanOnly=false(bool), MaxSaleQuality=0(int仅0..1), ProtectedItemIds(List<uint>)。Validate(),Clone()；CampaignSettingsStore.Save(path,settings),Load(path)严格JSON≤1MB，不能持久化实机确认true（读取重置false）。

`CampaignPlan` 字段：FarmRoute,CorpseRoute,VendorRoute(RouteDocument), Battle(BattleProfile), Settings(CampaignSettings), FarmReverse,FarmLoop(bool), StartIndex(int)。Validate(),Clone(),Fingerprint()->string；三路线/标定必须同图同zone，类型分别farm/corpse_run/vendor_return；计划开关关闭的路线可null；商人模式需Battle.Targets内至少一项Kind="npc"；FarmRoute及VendorRoute必须AllowReverse=true用于回程（开启Vendor时）。启用Recovery时CorpseRoute末端须接入FarmRoute第一点（JoinTolerance内）；VendorRoute首端同样。全路线不许自带interact动作（交互由状态机受控执行）。

## 记录路线规划与死亡引擎（恢复任务所有）

新增 `NavigationPurpose { Patrol, CorpseRun, VendorReturn }`；Navigator保留原构造函数，再加末尾purpose参数重载；Patrol原行为不变；CorpseRun只允许实际Ghost=true的预期死亡/幽灵，忽略包满，拒绝非Ghost、战斗、未知和换图；VendorReturn允许包满，拒绝死亡/幽灵/战斗。不能改写真实状态来绕过检测。

`TravelPlanner.ToVendor(CampaignPlan plan,OperationObservation current,int savedFarmIndex)->RouteDocument`：从当前位置安全接入已记录farm路线，沿已记录farm逆向到其起点，再vendor正向。

`TravelPlanner.FromVendor(CampaignPlan plan,int savedFarmIndex)->RouteDocument`：vendor逆序回farm起点，再沿已记录farm正向到中断点前的anchor（savedIndex-1，最小0），恢复原index。实际farm方向以FarmReverse应用后的WorkingRoute为准。

`TravelPlanner.ToCorpse(CampaignPlan plan,OperationObservation current,int savedFarmIndex)->RouteDocument`：实际ghost位置须接近corpse路线首端；实际CorpseKnown坐标须在farm记录线段CorpseTolerance覆盖内；沿corpse路线及farm记录路径到尸体最近可达anchor，最多添加CorpseTolerance内短连接，超出停止。不能用死亡位置冒充CorpseKnown。`
`TravelPlanner.FromCorpse(CampaignPlan plan,OperationObservation current,int savedFarmIndex)->RouteDocument`：沿farm记录路径回savedIndex-1 anchor；起始接入须在JoinTolerance内。路线的微小重合可直接判到点；生成RouteDocument仍须至少2个不同点，必要时引擎直接跳过行走而非伪造路线。方法可返回null表示已在对应anchor容差内，不能返回非法路线。

`RecoveryEngine(CampaignPlan plan,int savedFarmIndex)`；Step(OperationObservation,long nowMs)->OperationCommand；Stop(string)；Status,LastReason(string), Completed(bool), SavedFarmIndex(int)。完成返回ResumeFarm，不自行发送输入。流程2个不同帧死亡→点击释放(一次、超时)→真实Ghost→记录跑尸→冷却0且可见可用复活按钮→一次点击→两帧实际存活且HP>0→在本引擎吃喝恢复（低于Critical也允许正常恢复，不允许战斗/数据未知）→沿记录路径回原anchor→ResumeFarm。若开始已经Ghost可跳过释放，但仍要所有实际证据。中途再死亡/坐标丢失/邀请/路线不可连接均停止，独立总超时。

## 商人引擎（售卖任务所有）

`SalePolicy.CanSell(InventoryItem item,CampaignSettings settings)->bool`：完整Known、非空、非Locked、QuestKnown且非Quest、TypeKnown且非Consumable、ItemID>0、Count>0、0<=Quality<=MaxSaleQuality、0<UnitPrice<FFFFFFFF、ID不在保护清单；不把未知类别当安全。

`VendorEngine(CampaignPlan plan,int savedFarmIndex)`；Step(OperationObservation,long nowMs)->OperationCommand；Stop(string)；Status,LastReason(string), Completed(bool), SoldItems(int), SavedFarmIndex(int)。

流程：脱战等待（有限）→关闭拾取Esc→TravelPlanner.ToVendor→F6(配置键)有限选NPC，只匹配保存npc完整hash、非玩家且不可攻击（必须Known）→F交互→当前商店打开且merchantToken/name与选中保存NPC一致→B打开所有背包（绑定用户确认）→AutoCleanOnly时等FreeSlots>=Minimum且背包Full=false，否则逐格SalePolicy筛选+同槽可见ROI右键。

售卖前保存当前slot/fingerprint/count/revision/freeSlots；售卖后需不同新帧、InventoryRevision改变、原slot位图变空及FreeSlots增加才计成功；单纯BAG_UPDATE或其他格子变化不够。当前页切换可用位图证明原格，但不能修改原目标的身份。最多SaleLimit，不够空位/无可售项完整轮/元数据未知持续超时/界面切换/光标带物品/确认框都停止。FreeSlots已足且Full=false可以直接认清包成功。完成Esc关店，必须确认MerchantOpen=false才逆向路线，最后ResumeFarm。

## 总监督与执行（root所有）

CampaignEngine(CampaignPlan)；Step(OperationObservation,long)->OperationCommand；Stop(string)；Status,LastReason,Completed,RouteIndex,SavedFarmIndex,RecoveryCount,VendorCount；CurrentCheckpoint。死亡优先于售卖；中断只保存一次原farm索引，死亡覆盖售卖也保留它。自动成功才重建FarmEngine；Stop不会自动重启。

ICampaignInputBackend : IInputBackend，方法 ClientBounds()->Rectangle（屏幕坐标）, IsMouseAlreadyDown(bool right), MoveMouse(Point screen), MouseDown(bool right), MouseUp(bool right)。Windows实现仅外部SendInput/移动光标，无后台消息点击。

CampaignSession使用一个backend/engine/25ms监控，5秒倒计时；最终KeyDown/MouseDown前重新读取同目的、同target/hash、同item/fingerprint/revision、同layout/region以及全部可用条件；按住期间检查危险变化，保证自有按键/按钮释放。鼠标仅允许已知按钮或物品中心点，client边界内，避开三个数据网格；未知/异常/重入取消共享InputFailureLatch并停止。

CampaignSimulator完全不持有Native/backend；驱动完整farm→death→recovery→farm→full→vendor→farm，支持多轮/2h虚拟运行及失败注入。报告统一game_validation="pending"，虚拟时间/墙钟时间分列，系统输入0，真会话不得标输入0。UI开始任一模式先停止其他模式，运行时不允许重绑计划，断点校验完整计划fingerprint。
