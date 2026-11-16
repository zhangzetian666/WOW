# 自动钓鱼实施计划

**Goal:** 在现有 Windows 工具中交付纯键盘的抛竿、听水花收杆、包满返商人并逆序返回的完整钓鱼工作区，同时保留离线演示和实机待测边界。

**Architecture:** 复用 WP1/WC3/WO4 只读观察、严格 JSON、地图标定、Navigator 与共享输入故障锁；新增独立钓鱼状态机和会话。系统声音通过 WASAPI 播放环回采集，音频/提示图像由生产端分析后发布缓存，输入监控只读缓存；游戏操作只有键盘。

**Tech Stack:** C#5、.NET Framework、WinForms、WASAPI COM，无新增生产 NuGet。参考视频仅在开发侧解码，游戏电脑不用安装解码工具。

**Spec:** 用户提供的《魔兽世界无限服自动钓鱼脚本开发产品文档》；另一小DOCX仍为旧打怪需求。两份原文未修改，提取结果仅作开发复核；本计划是实现选择，不将文档中的3.3.5、F收杆有效和识别准确率当已验收事实。

## 约束与判断

- 游戏输出全键盘：默认1抛竿、F收杆/商人交互、Tab选NPC、W及配置转向键走路，不点浮标、背包或售卖按钮。
- 每杆30秒，只接收本杆开始后且过最小延迟的一个水花候选；只F一次。自身拾取事件/库存变化确认后等1秒再抛；F无反馈等待原30秒截止，不补F。
- 音频模板来自视频中单次水花片段；语音与顶部固定区域的“物品栏已满”形状/颜色匹配、已知0空位组合确认。仅匹配一类短提示，不宣称通用OCR/语音转写或能预先辨认他人水花。
- NPC完整名称哈希、单位身份和商人身份均核对；只F交互，必须见库存版本与真实空位恢复才返回；商店关闭或超时不是成功。
- 钓点接在可逆vendor_return路线首点；严格同图、标定和记录走廊。初始不在钓点时，只能从已记录节点加入逆序剩余路线；到点恢复保存朝向再抛竿，初始归位不计售卖次数。不猜测路线或新地形。
- 配置导入和断点读取不自动启动；人工确认绑定、F收杆、Auto Loot、音频和自动售卖插件。默认未知/失效停止，战斗/死亡交回人工处理。
- F12/失焦/过期/地图变化取消拥有的按键，部分失败匹配释放、锁存；与其他工作区互斥。Ctrl+F12在Ctrl和F12均释放后才启动倒计时，不能屏蔽急停。用户持有Ctrl/Alt/Shift时停止，保留用户按键所有权，近期停止不会被同次组合键误重启。

## 文件与接口契约

### 1 决策与配置

`src/Fishing.cs`、`src/FishingSimulator.cs` 与对应测试。

- `FishingSettings`：CastKey=0x31、InteractKey=0x46、SelectKey=9、TapMs=80；CastTimeoutMs=30000、PostCatchDelayMs=1000、LootWaitMs=3000、MinimumBiteDelayMs=700、BiteDebounceMs=1000、BagFusionMs=1000、BagCandidateMs=2000、VendorWaitMs=15000、ScanIntervalMs=1000、ScanLimit=12、MinimumFreeSlots=2。`EnableVendor=false` 默认站桩，`RequireAudio=true`。确认项 `BindingsConfirmed,AutoLootConfirmed,AudioConfirmed,AutoSellConfirmed` 默认false。
- `FishingPlan`：SchemaVersion=1、Name、Settings、Calibration(NavigationCalibration)、Movement(RouteKeyBindings)、Spot(Waypoint)、Facing(double)、VendorRoute(RouteDocument，可选)、Vendor(BattleTarget，kind=npc，可选)。Clone/Validate/Fingerprint；启用返程才要求路线/NPC，路线首点连接Spot、AllowReverse且同地图。
- `FishingPlanStore.Save(path,plan)` / `Load(path)`：有界严格JSON，确认字段及Calibration.Confirmed保存/载入均清空。另有 `FishingCheckpointStore.Save/Load` 记录计划指纹、阶段和路点；商店等待阶段还保留原空位、库存版本、NPC身份和已观察商店的证据。恢复要用户确认且重新校验当前点；不能恢复未释放的按键、旧声音事件或旧抛竿时限。
- `FishingObservation`：`Game`(OperationObservation)、`CapturedAtMs`、`AudioReady`、`LastBiteAtMs=-1,LastBagVoiceAtMs=-1,LastBagTextAtMs=-1`、三个Confidence(double)。`Clone()` 深拷贝Game。时间统一来自主工作区单调时钟，候选带起始时间；历史/未来/过期候选不可跨杆使用。
- `FishingCommand`：`Action`(FishingAction.None/Key/Stop/Completed)、`Purpose`(FishingPurpose.Cast/Reel/Travel/SelectNpc/OpenMerchant/CloseMerchant/RestoreFacing)、`Key`、`HoldMilliseconds`、`ExpectedTargetToken,ExpectedNameHash`、`ExpectedBiteAtMs`、Reason。不得出现鼠标指令。
- `FishingEngine(plan)`、`Step(observation,nowMs)`、`Stop(reason)`；只产命令，不创建原生输入。公开 `Status,LastReason,RouteIndex,Casts,Reels,ConfirmedCatches,VendorVisits,CurrentCheckpoint`，`ValidateLiveObservation(plan,o)`供会话复核。状态与超时在重复帧也检查，只有新帧推进路点/确认计数。
- `FishingSimulator` 持有真实FishingEngine，用合成Game/声音推进正常循环、无咬钩、误音/重复音、包满往返、错误NPC/不清包、过期/死亡等场景；输入事件固定0。模拟与真实报告始终分开。

### 2 感知与素材

`src/FishingPerception.cs`、`assets/fishing/`、`tests/FishingPerceptionTests.cs`。

- `FishingSoundDetector(string templateDirectory)`：加载有界离线模板；`Push(float[] mono,int sampleRate,long endedAtMs)`，`Reset()`，线程安全 `Snapshot()->FishingSoundSnapshot`（LastBiteAtMs/LastBagVoiceAtMs、BiteConfidence/BagVoiceConfidence）。输入分块边界、静音、增益、负样本与事件双峰须覆盖；模板验证后不在输入监控中做磁盘I/O。
- `FishingBagTextDetector(string templateDirectory)`、`Score(Bitmap clientImage)->double`，只检查顶部归一化ROI与参考字形，拒绝聊天框同字/普通红色提示；预处理支持1280/1920缩放，未标定UI布局要提示。
- 从用户MP4提取并核验真实样本，不沿用文档未核验时间点。生产资产仅小型特征/参考图，不提交原视频、聊天文件目录或完整DOCX。

### 3 Windows声音适配

`src/FishingAudioNative.cs`：`WindowsFishingAudioCapture(Action<float[],int,long,bool> samples,Func<long> clock)`，`Start/Stop/Dispose`；`Running,LastPacketMs,Error`。同一COM工作线程采默认播放设备的shared loopback，转换PCM/float并下混单声道；bool表示断续/不可靠块，应重置匹配。静音产生0，设备断开置失效。默认不采麦克风。

纯域构建排除此适配器；实际设备采集与延迟单列实机待验收，不能用合成样本宣称通过。

### 4 会话与工作区

`src/FishingSession.cs`、`src/FishingForm.cs`、`src/Program.cs`：

- 会话复用IInputBackend、InputFailureLatch、5秒倒计时和短脉冲；generation取消重入，发送前/持有中复查Game、候选时间/身份和前台，不自动重启停止会话。
- 工作区保存钓点/当前NPC、录制/导出/载入多点路线和完整JSON；F5~F11为明确注册的人工热键，Ctrl+F12启停，F12最高优先。热键不向游戏注入，不自动修改客户端绑定。
- 音频/图像采集、日志、截图与报告在生产/界面路径完成；监控只读有时效的内存。报告带来源、置信度、事件与计划动作，game_validation固定pending。
- 所有模式启动的prepare、主Stop/Close与单次按键Busy检查纳入Fishing；准备新模式先停止其他模式。
- 构建验证包含新纯域/输入回归、素材回放、工作区离线按钮流程及旧全套；保留旧包，新增0.7.0包与指南。

## 实施与交付

- [x] 需求与视频核验，建立小型参考模板及负样本检查。
- [x] 决策/文件/模拟先写失败回归后实现，含30秒、1秒、一次F及商人往返。
- [x] WASAPI与感知适配，验证异常/断续不会复用旧正例。
- [x] 输入会话、工作区与热键接入，复核模式互斥与释放。
- [x] 素材回放、完整旧回归、UI预览/离线流程、独立审查。
- [ ] 更新Markdown与能力矩阵、0.7.0打包核对、按模块提交并同步GitHub；实际F收杆/音频延迟/自动售卖/两小时游戏运行保留待测。
