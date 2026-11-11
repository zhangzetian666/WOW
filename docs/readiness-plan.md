# v0.6 方案交接与实测准备计划

**Goal:** 在现有核心闭环上补齐完整方案携带、启动前检查和只读反馈摘要，修复本地可复现的导入/事件归属/断点一致性问题。

**Architecture:** 沿用 C#5 / .NET Framework / WinForms。完整方案使用受限ZIP容器（.wowplan），复用现有严格JSON和图像校验；自检、反馈分析均只读，不启动输入、不把准备条件或模拟结果记为实机通过。

**Spec:** [功能需求](requirements/functional-requirements.md)、[完整版本指南](complete-testing.md)、[能力矩阵](capability-matrix.md)。用户已授权无需等待游戏测试继续开发。本轮不猜测客户端能力，不增加通用寻路或无插件操控。

## 接口与文件

### 1. 完整方案与断点（方案任务）

新增 `src/CampaignBundle.cs`、`tests/CampaignBundleTests.cs`、`tests/CampaignCheckpointTests.cs`，修改 `CampaignCheckpointStore` 一致性校验。

`LoadedCampaignBundle`：Plan(CampaignPlan)、AvatarDirectory(string)；`CampaignBundleStore.Save(string path,CampaignPlan plan,string avatarDirectory)`、`Load(string path,string cacheDirectory)->LoadedCampaignBundle`。

- 保留三类路线、战斗/完整设置、打怪方向和循环，复制引用的有效头像；不携带运行断点，StartIndex统一0，所有实机确认及标定Confirmed清为false；导入不启动。
- 固定JSON名 farm.json/battle.json/settings.json，启用的中断分别 corpse.json/vendor.json。manifest.json schema_version1，保存farm_reverse/farm_loop、规范化后的plan_fingerprint及files条目（path,length,sha256）。头像统一avatars/<namehash的8位小写十六进制>.png。
- archive≤16MiB、总解压数据≤16MiB、entry≤256、单JSON≤2MiB、manifest≤64KiB、单头像≤1MiB且≤512×512；逐条有界读，不先ExtractToDirectory。拒绝重复/多余/绝对/../反斜线/大小写冲突路径、损坏hash/长度、畸形或语义不匹配的计划。摘要用于完整性核对，不宣称来源认证。
- 只用已有标准Framework压缩库，不引入NuGet。全部数据验证后才向cacheDirectory下新GUID子目录写已验证头像；所有输出为固定路径，拒绝重解析点，不覆盖旧缓存。失败不替换当前计划；Save原子替换。
- 断点：Completed必须与Phase=Completed一致；Farm不能Interrupted，Recovery/Vendor必须Interrupted；Stopped允许两类中断状态，但不能Completed。保持完整计划fingerprint及索引一致性。

### 2. 只读反馈摘要（反馈任务）

新增 `src/FeedbackSummary.cs`、`tests/FeedbackSummaryTests.cs`。

`FeedbackSummary.Load(string reportDirectory)->FeedbackSummary`；属性/字段 `Source,ToolVersion,RunId,LastState,StopReason,ClientVersion(string),VirtualMilliseconds,WallMilliseconds(long),GameValidation(string固定pending),Warnings(List<string>),RecentEvents(List<string>)`；`ToMarkdown()->string`。

- 支持0.5旧报告与0.6新报告，读取campaign-report.json、runtime-evidence.json和清单指定的本轮JSONL；无脚本执行、无输入后端、不导入为配置、不修改原报告。
- 严格JSON（拒绝重复键），验证报告/清单/JSONL run_id与source不矛盾；旧报告缺字段则明确未知/警告，不能用清理实机确认后的导出配置重算并强制等于运行fingerprint。
- 报告≤8MiB、清单≤4MiB、单日志≤10MiB、最多64个日志、合计≤64MiB；只读同目录普通文件，拒绝绝对/遍历路径、重解析点、源混用。丢失/截断的可选日志可警告；来源矛盾/非法路径拒绝。只保留最近20条事件和最新停止/失败原因；仅读少量可核验元信息，不把planned_command说成实际执行。
- 摘要必须注明模拟/实机尝试、虚拟/墙钟时间、未验收状态以及“准备好/运行完成不代表实机验收”。Markdown转义用户字段、移除控制字符，不扫描无关目录。

### 3. 自检与界面（root）

新增 `src/CampaignPreflight.cs`、`tests/CampaignPreflightTests.cs`；接入CampaignForm的一键保存/载入方案、自检、反馈读取和导出摘要。

`CampaignPreflight.Evaluate(CampaignPlan plan,OperationObservation observation,bool inputFaulted,string avatarDirectory)->PreflightReport`。PreflightReport有 `ReadyToArm(bool)`、`Checks(List<PreflightCheck>)`、`ToMarkdown()`；Check字段 Code/Label/Detail(string)、State(string: pass/block/pending)。

- 当前计划结构/角色/地图/方向/可逆性、必要键位/标定/界面/续跑确认、同步新鲜数据、窗口尺寸、基本坐标/血蓝、共享输入锁为阻断项；当前已出现的危险目标/光标物品/弹窗/复活邀请也阻断。
- 按当前情景检查起点或幽灵路线、释放/复活、必要目标射程/坐标。未显示的未来弹窗、尚未选中的目标或关闭背包的格子矩形列为运行时待查，不能要求它们预先全部显示。
- 启用头像辅助时验证引用文件；I/O只在UI显式检查中，禁止放入安全监控。自检不替代现有逐步安全检查，也不启动输入。
- 导入路线/战斗/标定前保留当前有效的规则编辑，不能静默重置保护清单、空位阈值和品质；新运行报告仅含本轮事件，前一轮来源不能混入。
- 新报告补schema_version/run_id/client上下文/运行方向和循环；观察客户端元信息从WP1继承，不改变线上协议格式；新旧报告明确区分。

## 验证与交付

- [x] 三个纯模块分别写失败测试、观察失败再实现；严格文件/输入边界与旧报告兼容做负例。四组专用域检查68项通过。
- [x] 真实按钮流程证明完整方案恢复方向/循环、导入不启动、自检不创建后端、反馈只读、新运行事件隔离；头像文件往返另由域检查覆盖。Readiness界面7场景通过，真实输入后端请求及输入事件均为0。
- [x] 全量旧检查、虚拟压力、桌面/界面检查和独立审查；修复具体问题后回归。完整验证退出0，域检查493项通过、0失败，另5项跨语言/像素检查；四预览已查看，两个P2修正后回归通过。
- [x] 更新Markdown，生成v0.6.0包并核对；42个归档项/40个文件，35个源文件逐字一致，71个包内Markdown相对链接通过，历史包保留。
- [ ] 将最终文档纳入包并重新生成旁置SHA256，完成按模块Git记录及同步main。实机验收继续待用户反馈。
