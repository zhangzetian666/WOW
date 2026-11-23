# WOW 自动打怪客户端

当前源码版本为 0.8.48，只保留一个 Windows 客户端：WoWProbe.exe。双击“启动自动打怪.cmd”或直接打开程序，进入巡航、技能、恢复及 NPC 路线界面。旧 0–0.7 阶段的独立界面、旧客户端副本及历史安装包已清理，Git 历史保留。

当前功能包括手动录点与导入、附近寻怪、战斗、拾取剥皮、自动吃喝、跑尸、NPC 售卖修理往返及行程断点恢复。路线和按键需按实际电脑配置；开发侧检查不等于实机验收。运行只保存文字日志和状态数据，不保存截图。停止/F12取消自动运行。

0.8.48 增加五行随机技能、20/10/5条巡航/NPC/跑尸路线变体及定时下线休息，详见[新版使用说明](docs/random-runtime-usage.md)。本次已通过本地回归，真实路线通行及下线上线全流程待游戏验收。随机化不保证规避运营商检测。

唯一部署入口为 E:\WOW\新版测试\WoWProbe.exe。更新时保留 quick-patrol.json、quick-patrol-recording.json、quick-corpse-route.json、quick-npc-progress.json（存在时）及 patrol-routes。辅助插件源码在 addon/WoWProbe，更新插件后需重载游戏界面。

## 分支

本轮优化完成后同步 main 和 chief；chief 保留本轮完成快照，后续新增开发在 main。

## 构建与验证

- build.ps1 -QuickCheck：编译并运行当前客户端针对性检查。
- build.ps1：完整 C# 检查。
- build.ps1 -Package：统一输出 dist/WoWProbe-Current.zip。
- verify.ps1 -PythonPath <python.exe>：C#、Lua、协议与桌面检查；-SkipDesktopSmoke 跳过桌面采集。

保留现用公共底层及回归测试；不再提供旧阶段界面。产物、日志、依赖不提交至源代码仓库。

## 文档

- [需求总表](docs/requirements/development-requirements.md)
- [本轮随机技能、路线及休息需求](docs/requirements/randomized-runtime.md)
- [进度与验证](docs/progress.md)
- [协作约定](AGENTS.md)

docs 中旧阶段指南作为历史记录保留，其旧界面和包名不再是当前操作入口。
