# 第二阶段路线系统实施计划

> **For agentic workers:** Execute the user-authorized phase two using test driven development, parallel independent components and final code review. User explicitly authorized proceeding on 2026-11-11 while assuming phase one interfaces work.

**Goal:** 交付路线录制、保存载入、模拟巡航、受控真实巡航、转向纠偏、逆序返回、有限脱困和断点文件。

**Architecture:** 路线文件与纯导航状态机不依赖 Windows 或游戏；模拟器把导航命令作用在虚拟角色上。真实巡航从第一阶段的有效状态流取样，经过独立输入保护执行短按。界面默认模拟模式；真实模式需要用户确认键位与当前地图标定，倒计时五秒后启动。

**Tech Stack:** 延续 C#5、.NET Framework、Windows Forms、现有 Lua 状态面板，无新增游戏电脑运行依赖。

**Spec:** [开发流程第二阶段](development-workflow.md)；[产品设计第五章](requirements/product-design.md#5-路线巡航系统重点章节)。

## 用户授权与假设

- 用户允许先假设第一阶段没有问题，不要求等待回到游戏电脑才开发。
- 第一阶段实际状态仍是待实机验收；本阶段的模拟通过不替代真实验证。
- 仅开发路线系统，不提前实现自动选怪、战斗、剥皮、跑尸流程或售卖流程。
- 默认使用键盘短按前进与转向。鼠标拖拽和自动奔跑留待有实机标定依据时扩展。
- 旧数据、地图或区域改变、死亡、灵魂、战斗、包满提示、失焦、F12和退出会停止巡航并释放本工具按键；不会自行恢复。
- 卡住采用跳跃、有限侧移、回退已到过路点的分级处理；三次脱困仍失败则停止，不跳过未知障碍继续冲向后续节点。
- 所有导航设置、模拟结果和未实测事项以 Markdown 记录；不生成 Word。

## Review Focus

1. 地图横纵尺度、朝向零点和方向不可直接套用未经确认的默认值；真实巡航须标定确认。
2. 导入文件需要结构、范围、数量、地图模式和区域检查；不能用错误地图路线控制角色。
3. 校验和通过但API字段失效、序号停滞、重复帧或读屏线程卡住时仍须及时取消按键。
4. 逆序、循环、暂停和断点恢复不能越界、丢失方向或自动重启。
5. 真实与模拟模式必须清楚区分；任何模拟运行不得调用原生输入。

## Tasks

### 1 路线数据与录制

Files: src/Routes.cs; tests/RoutesTests.cs; routes/examples/*.json.
- [ ] Write failing tests for JSON roundtrip, malformed input, finite coordinates, count bounds, map/zone consistency, recording, reverse filtering and checkpoint identity.
- [ ] Implement versioned JSON route store, recorder and atomic checkpoint persistence per phase2-interfaces.md.
- [ ] Run pure console tests, including non-English paths and culture-independent numbers.

### 2 纯导航与模拟

Files: src/Navigation.cs; tests/NavigationTests.cs.
- [ ] Write failing tests for bearings at cardinal directions, aspect ratio, angle wrapping, arrival over distinct packets, waiting, reverse/loop and recovery exhaustion.
- [ ] Implement bounded-pulse navigator, sample validation and virtual movement simulator.
- [ ] Simulate straight, corner and reverse runs; prove stops on invalid states and never calls Windows input.

### 3 输入会话与界面

Files: src/RouteSession.cs; src/RouteForm.cs; src/Program.cs; tests/RouteSessionTests.cs; tests/Smoke.cs.
- [ ] Write failing session tests using fake backend: five-second arming, pulse deadline, stale/dead/combat/focus stop, exceptions and key ownership.
- [ ] Integrate session with first-phase read-only state snapshots and an independent watchdog.
- [ ] Add route recording/import/export, plotted preview, navigation controls, calibration settings, simulation, checkpoints and report export.
- [ ] Keep simulator independent of game capture and native input; inspect application layouts.

### 4 验证与交付

Files: build.ps1; verify.ps1; README.md; docs/progress.md; docs/phase2-testing.md; docs/verification.md.
- [ ] Run existing and new suites, cross-language check, desktop capture smoke and route UI preview.
- [ ] Run independent review; reproduce and fix important findings with tests.
- [ ] Package v0.2.0 with example route and guides, preserve v0.1.0 package.
- [ ] Commit logical changes, merge into main and synchronize the authorized GitHub repository without force pushing.

## 完成口径

完成开发侧代码、模拟验证、测试包和 Git 记录；真实地图巡航、键位与数值标定仍待游戏电脑验证。阶段二不标注为实机验收通过。
