# 第一阶段环境与可行性验证实施计划

> **For agentic workers:** Execute the approved first phase task by task using test driven development and independent final review. The user explicitly requested implementation on 2026-11-11.

**Goal:** 交付 Windows 11 可运行检测工具、最小游戏插件和跨机测试包，取得真实客户端能力证据。

**Architecture:** 插件通过屏幕二进制色块输出只读状态；外部工具截取校准区域并校验数据。外部工具默认只读，用户明确启用后可倒计时执行单次、限时按键；独立监控负责急停、失焦和数据失效时释放按键。

**Tech Stack:** .NET Framework 4.x C# 5 Windows Forms；游戏 Lua 5.1；Windows 原生截图与 SendInput。游戏电脑无需 Python 或开发工具。

**Spec:** ../魔兽世界无限服脚本开发流程与周期计划.docx 第一阶段；../脚本功能需求.docx；../魔兽世界无限服脚本开发产品文档.docx。

## Global Constraints

- 游戏位于另一台 Windows 11 电脑；客户端版本待用户回家确认。
- 以 3.3.5 接口作为候选基线，所有能力须检测并报告缺失，不宣称已通过游戏实测。
- 第一阶段不实现自动打怪、循环移动、跑尸或售卖。
- 不读取游戏内存，不修改客户端文件；插件由用户复制至 AddOns。
- 新项目位于 phase1 独立仓库的 codex/phase1 分支，不修改上级项目分支。

## Review Focus

1. 陈旧或损坏的色块数据不得启用按键。
2. 失焦、F12、超时、界面退出都须释放工具按住的键，且不自动恢复。
3. 窗口坐标、DPI 缩放与色块校准要明确，像素越界须报错。
4. 缺失 API、Lua 错误与非蓝条职业不得伪造正常值。
5. 缺少游戏实测不能把阶段标为验收完成；测试报告须保留待测项。

## Task 1 插件只读状态与色块输出

Files: addon/WoWProbe/*; tests/addon_tests.lua; docs/protocol.md.
Interfaces: 128-byte WP1 packet, 32 columns by 12 rows, 8 physical pixels per cell; protocol document is authoritative.
- [ ] Write and run failing Lua tests for encoding, missing APIs and state flags.
- [ ] Implement a Lua 5.1-compatible read-only AddOn, capability flags, saved diagnostic report and slash commands.
- [ ] Run Lua tests; validate packet against C# reader fixtures.

## Task 2 解析与按键保护

Files: src/Core.cs; tests/CoreTests.cs.
Interfaces: ProbePacket.Decode(byte[]); PixelCodec.Decode(Bitmap,int,int,int); Heartbeat.Observe/IsFresh; InputGuard.Schedule/Tick/Stop, pure injected backend.
- [ ] Write and run failing tests for checksum, invalid ranges, heartbeat expiry and input stop/release behavior.
- [ ] Implement protocol decoder and backend-independent pulse scheduler with bounded duration.
- [ ] Run full core tests without emitting OS input.

## Task 3 Windows 检测界面与交付

Files: src/Native.cs; src/Program.cs; build.ps1; README.md; docs/实机测试记录.csv.
- [ ] Connect window selection, manual pixel calibration, status display and report export.
- [ ] Connect single-key test with five-second countdown and dedicated 25ms safety monitor.
- [ ] Compile executable and tests; smoke test UI, real pixel screen capture and report creation using a local simulator, without controlling unrelated applications.
- [ ] Package AddOn, executable, guide, test checklist and verification evidence into dist/WoWProbe-Phase1.zip.
- [ ] Obtain independent code review, fix important findings, rerun verification.

## Completion

开发侧交付可测试包；游戏电脑 API、输入效果、真实地图坐标与交互功能均待实机验收。将这两种结果分别记入 progress.md 与测试记录。
