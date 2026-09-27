# r4 · 扳机显示、原始读数与概览

> 历史记录（1.0.1 文档同步，2026-09-27）：正文保留当时的设计、功能状态与验证结果，不作为当前操作说明。当前键鼠 / 混合输入、全输入映射、NS2 USB 连接及默认配置见[当前文档目录](../../docs/README.md)和[1.0.1 更新日志](../../CHANGELOG.md)。

日期：2026-09-26。目标项目：NS2ProWin11（.NET 10 / Avalonia）。

## LT / RT 线性输入

此前手柄图从按键位判断扳机，SDL 只有超过 0.5 的轴值才设置数字按下位，因此轻压状态无法显示。现在标准手柄直接读取 SDL 左右扳机轴（索引 4 / 5），保持两侧独立的 0–1 行程，不经过数字按键阈值或测试死区。

- 图示从底部按比例填充，旁边显示 0–100% 行程。Xbox 使用 LT/RT；PlayStation 使用 L2/R2；Nintendo 使用 ZL/ZR。
- 原始读数区同时提供两位小数的扳机卡片和五位小数的轴读数。12.5% 行程不会再显示成松开。
- NS2 BLE 和 NS1 映射预览使用数字 ZL/ZR，松开为 0，按下为 1。切换显示皮肤不改变输入数值。
- 输入断开后两侧归零。越界数值限制到 0–1；非有限数值归零。
- 未映射的通用 HID 继续显示原始轴编号，不把任意第 4/5 轴假定成扳机。

实现位置：`Input/TriggerLevels.cs`、`GamepadSnapshot.Triggers`、`MainViewModel.UpdatePreview` 和 `ControllerTesterControl`。显示方式参考 [All Controller Test](https://allcontrollertest.com/zh/tester)，保持本应用浅色主题；不依赖网页运行。

## 原始读数布局

窄卡片中的 Fluent ProgressBar 默认测量宽度会超出卡片，多个灰色轨道连成横线。改为按实际布局尺寸绘制的 `InputLevelBar`，轨道与填充始终限制在卡片内。按键/扳机卡片固定高度并自动换行；轴/方向帽使用独立卡片，保留数值精度。较长按键名省略显示，悬停可查看全名。

## 驱动配置快捷面板

保持驱动未就绪时禁用虚拟 USB 启动。紧邻启动按钮增加「配置 USB/IP 驱动」，未就绪时显示安装/修复提示。点击后直接弹出状态、刷新、安装/修复及卸载入口；概览也可打开同一个面板。

打开面板只检查状态，不自动安装。用户点击「安装 / 修复」才启动随附官方向导。完成向导后返回应用会刷新，也可手动刷新。缺少原厂卸载器时继续禁用卸载；正在安装或执行虚拟设备操作时遵循原有互斥规则。

`DriverSettingsPanel` 同时用于快捷面板和虚拟页底部，状态与操作绑定到同一 ViewModel。

## 概览与导航

浅色概览按以下顺序纵向排列，每步在同一行呈现当前状态、说明和操作：

1. 准备运行环境：USB/IP 驱动与蓝牙适配器。
2. 选择输出身份：当前型号与虚拟 USB 状态。
3. 接入实体 NS2 Pro：实时连接状态与自动连接是否开启。
4. 验证输入：Windows 实际发现的手柄数及测试入口。

顶部根据真实状态提示下一步；底部保留有效/无效帧、主机反馈和最近按键。手动暂停自动连接后不再显示“自动搜索已开启”。Windows 枚举数量不作为游戏兼容通过的证明。“测试 Windows 手柄”入口会选择 Windows 输入源，避免误留在映射预览。切换页面回到顶部，避免沿用上一页的滚动位置。

## 验证与边界

- 构建与默认回归检查通过。新增检查覆盖低于数字阈值的部分行程、两侧独立、全按/松开、数字 Nintendo 输入、缺少轴、未映射 HID、越界及非有限数值。
- 实际 Avalonia 窗口检查了 1400 与 1050 宽度的概览、当前 Xbox 设备读数、原始卡片换行及轨道边界，以及驱动未就绪时的禁用状态和快捷面板。
- 本机当前缺少 USB/IP 客户端，仅保留服务注册项。未执行驱动安装/卸载或新虚拟设备挂载；本轮未人工按压实体线性扳机，部分行程通过合成 SDL 快照回归验证。虚拟协议与 BLE 转发链路沿用 r3，不将历史硬件记录视为本轮重新实测。

```powershell
dotnet build ControllerForwardingTool.slnx --no-restore
dotnet run --project NS2ProWin11.Checks --no-build
dotnet publish ControllerForwardingTool/ControllerForwardingTool.csproj -c Release -r win-x64 --self-contained false -o artifacts/release-win-x64-r4
```

发布包：`artifacts/NS2ProWin11-win-x64-20260926-r4.zip`。请解压完整目录后运行其中的 `NS2ProWin11.exe`，保留运行库、驱动安装器、许可证、第三方源码和文档。

以上为 r4 历史包结构。r5 起文档仅留在仓库，第三方源码改为配套独立压缩包，运行目录不再包含 docs、README 和 PDB，见 [r5 说明](11_MOTION_CALIBRATION_AND_PUBLISH.md)。
