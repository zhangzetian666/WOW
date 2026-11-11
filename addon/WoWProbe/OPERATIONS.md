# WO4 只读恢复与背包面板

版本 0.5.0。`Operations.lua` 在原 WP1/WC3 模块之后加载，只读取状态，不执行释放灵魂、复活、售卖、物品使用、光标修改或按钮点击。三块面板同序号；WO4 位于 WP1 右侧 544 个物理像素，参考头像移至 WP1 右侧 824 像素。旧两块协议布局保持兼容。

恢复按钮只识别默认 StaticPopup 的 `DEATH` 和 `RECOVER_CORPSE` 类型，并读取真实按钮的显示、启用状态及当前物理区域。玩家复活请求单独标记，不当作捡尸。任何其他可见弹窗都标记为确认框。缺少已知结构、冷却 API 或几何数据时，不公布对应能力。

背包只支持 0–4 号容器、每包最多 40 格。每帧输出一格元数据和全部受支持格子的占用图；`BAG_UPDATE` 来源不可用时，不公布背包能力。物品身份来自完整 link/id，数量、品质、价格和元数据要一致；任务与消耗品属性另有 Known 标记。未知不会标为可售卖。客户端未缓存物品、缺少类型或任务接口时，需要人工处理或选择经验证的服务器自动清包方式。

商店身份来自 `UnitGUID("npc")`、`UnitName("npc")`，不使用当前选中目标代替。默认背包按钮通过真实父容器 ID 和按钮 `GetID()` 映射格子；不把倒序的控件名称后缀当格号。仅默认商店出售页、实际可见的背包格公布点击区域；自定义背包、买回页和缺失区域都不允许靠推算点击。

实现参考的客户端源码与接口定义：

- [StaticPopup.lua](https://github.com/tekkub/wow-ui-source/blob/live/FrameXML/StaticPopup.lua)：死亡、捡尸、他人复活是不同弹窗类型，捡尸弹窗带恢复冷却。
- [ContainerFrame.lua](https://github.com/tekkub/wow-ui-source/blob/live/FrameXML/ContainerFrame.lua)：背包交互以实际按钮 ID 取格号，商店选中买回页时不得出售。
- [Container API 定义](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_APIDocumentationGenerated/ContainerDocumentation.lua)：现代容器接口返回结构体，部分读取允许无结果；未知值保留为未知。
- [Item API 定义](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_APIDocumentationGenerated/ItemDocumentation.lua)：物品缓存信息、类型与售卖价格分别读取，未缓存不能伪造元数据。

这些源码用于实现接口分支，不证明目标私服客户端兼容。真实游戏、分辨率、UI 缩放和背包插件仍待游戏电脑验证。
