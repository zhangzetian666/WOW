# 第四、第五阶段开发实施计划

> 用户已授权直接完成后续开发，不等待游戏电脑反馈。使用既有 C#5 / .NET Framework / Lua5.1，所有游戏验收仍为待测。

**Goal:** 补齐死亡恢复、商人清包、返回原路线及统一运行监督器，完成离线长时回归、界面、配置、报告、测试包和 GitHub 交付。

**Architecture:** 保留 WP1/WC3 和独立旧模式，新增同序号 WO4 只读界面/背包数据。纯恢复、商人及总监督状态机只产生有界指令，统一键鼠执行器独占输入并共享故障锁；自动中断恢复不能绕过人工急停。按记录路线连接，不根据未知地形直跑。

**Spec:** [原始功能](requirements/functional-requirements.md)4/5，[产品设计](requirements/product-design.md)6.3.6/6.3.8/8/9，[执行流程](development-workflow.md)第四、第五阶段，[精确接口](complete-interfaces.md)。

## Global Constraints

- 同一客户端、角色、地图及人工录制三类路线；不引入通用寻路、内存读取、进程注入或受保护 API 自动调用。
- 所有未知能力保留未知。旧客户端、未缓存物品、无按钮/物品位置、非出售页、物品在鼠标上、确认弹窗、错误商人均禁止点击。
- 默认只售灰色、可售、有完整元数据且非任务/消耗品/用户保护清单的物品；任何物品售出都必须有背包事件及空位/数量变化证据，禁止删除物品或点出售确认。
- 运行须明确确认键位、当前地图标定、界面布局；5秒倒计时、25ms监控、60–250ms短按、鼠标按下/释放配对、失焦/F12/过期/输入故障停止。
- 复活后先在恢复状态机完成血蓝恢复，再回原 farm 路线索引；死亡、清包事件不覆盖原断点。用户手动停止必须人工重新启动。
- 本机真实桌面捕获已知返回原生句柄错误；窗口绘制与数据图像测试单列，不能算真实截图或游戏通过。
- 原生工作区工具拒绝项目的嵌套仓库路径；沿用当前专用目录的 codex/complete 分支，main 保留已交付版本。
- 日常文档 Markdown、按模块验证后提交、同步已授权 GitHub；不产生 DOCX。

## Review Focus

1. 瞬时/重复死亡、幽灵与真实存活判定、冷却/复活邀请以及界面移动，不能误点或假复活。
2. 随机尸体或商人路线连接必须在记录路径覆盖内；地图、方向、断点和路线内容改变时不可误恢复。
3. 物品未知/保护类别、换格/换数量、切买回页、光标物品、背包更新与包满重复事件，不得误卖或把超时当完成。
4. 键鼠回调重入、取消后重启、部分输入异常、按钮按住时条件改变，都必须释放自有输入且不越过新倒计时。
5. 至少两小时虚拟运行覆盖多轮中断、停止和恢复，不混用真实/模拟观察；日志、快照保留和报告必须如实标注来源。

## Tasks

### 1. 被动数据与类型

**Files:** addon/WoWProbe/*、src/OperationsTelemetry.cs、tests/OperationsTelemetryTests.cs、tests/addon_tests.lua。
**Interfaces:** complete-interfaces.md 中 WO4、OperationObservation / InventoryItem / OperationCommand；不改 WP1/WC3 格式。

- [x] 写无能力、畸形、序号不配、按钮及物品元数据未知的失败测试。
- [x] 实现被动采集及严格解码，保留逐格循环、事件和物理位置来源；没有依据时不发布可用。
- [x] Lua5.1、C#、同序号三面板图像与跨语言检查通过后提交。

### 2. 死亡路线与恢复

**Files:** src/RecoveryWorkflow.cs、src/Navigation.cs（显式旅行模式）、tests/RecoveryWorkflowTests.cs、tests/NavigationTests.cs。
**Interfaces:** RecoveryEngine.Step(OperationObservation,long)->OperationCommand，TravelPlanner，CampaignSettings，CampaignPlan。

- [x] 先验证幽灵/包满原导航仍拒绝、显式旅行策略及记录路线连接；再写死亡确认/释放/冷却/邀请/复活/恢复/原断点测试并观察失败。
- [x] 实现纯恢复状态机，按钮/坐标未知和无法接入路线均停止；不伪造 Dead/Ghost/BagFull。
- [x] 原导航与新流程测试通过后提交。

### 3. 商人清包

**Files:** src/VendorWorkflow.cs、tests/VendorWorkflowTests.cs。
**Interfaces:** VendorEngine.Step(OperationObservation,long)->OperationCommand，SalePolicy，TravelPlanner。

- [x] 先写保护规则、友方NPC身份、商店/买回页、光标/确认框、逐格售卖证据、自动清包和逆向返回失败测试。
- [x] 实现记录路线往返、已确认 NPC 按键/交互、默认灰色销售、有限次数/等待和精确断点恢复。
- [x] 合成数据完整往返及未知能力停止检查通过后提交。

### 4. 总监督、配置、受控键鼠与界面

**Files:** src/Campaign*.cs、src/Native.cs、src/Program.cs、tests/Campaign*.cs。
**Interfaces:** CampaignEngine、CampaignSession、CampaignSimulator、CampaignSettingsStore、CampaignForm。

- [x] 先写严格配置/克隆、死亡优先级和原 farm 索引、急停/失焦/重入/释放失败、离线不创建真实后端等失败测试。
- [x] 实现复用 FarmEngine 的总循环、单一输入所有者、额外 WO4 读取、默认离线演示、三类路线/配置导入及报告。
- [x] 加入持久日志、保留期限与失败快照、参考图匹配辅助及能力矩阵；关键状态仍以可靠遥测为依据。
- [x] 全量自动检查、界面实际按钮流程和独立代码复核通过后提交。

### 5. 稳定性与交付

- [x] 运行至少两小时虚拟时间的多轮死亡/售卖/恢复压力场景，记录时间口径，不替代实机连续运行。
- [x] 更新 README、workflow、progress、verification、完整版本指南、客户端能力矩阵与实机验收清单。
- [x] 打包新版本并核对 EXE、插件、示例、文档及 SHA256，保留历史包。
- [ ] 按模块提交、fast-forward main、推送并核对远程与本地一致；实机验收全部保留待测。
