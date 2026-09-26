# 手柄转发工具 / Controller Forwarding Tool 开发文档

目标项目：`D:\github\NS2ProWin11\ControllerForwardingTool`（.NET 10 / Avalonia 12）。程序将 Switch 2 Pro BLE 或 Windows 标准映射手柄输入转发为 Nintendo / Xbox / PS5 / Edge 虚拟 USB，并提供多类型手柄测试。当前采用 SDL、VIIPER 与 usbip-win2 开源组件，保留来源和许可证。

| 文件 | 用途 |
| --- | --- |
| [01_BLE_TO_NS1_IMPLEMENTATION.md](01_BLE_TO_NS1_IMPLEMENTATION.md) | 架构、实施阶段、接口、验收与风险 |
| [02_NS2_PRO_PROTOCOL_REFERENCE.md](02_NS2_PRO_PROTOCOL_REFERENCE.md) | NS2 Pro 输入端的公开资料核查和非官方协议观察 |
| [03_NS1_PRO_PROTOCOL_REFERENCE.md](03_NS1_PRO_PROTOCOL_REFERENCE.md) | NS1 Pro 输出端的公开资料核查和非官方协议观察 |
| [04_DRIVER_AND_LICENSE.md](04_DRIVER_AND_LICENSE.md) | 已复制驱动安装器的来源、哈希、许可证及使用边界 |
| [05_UI_DESIGN_RULES.md](05_UI_DESIGN_RULES.md) | 手柄转发工具的页面结构、视觉令牌、连接状态与交互验收规范 |
| [06_PROTOTYPE_STATUS.md](06_PROTOTYPE_STATUS.md) | 当前代码原型、可运行检查与未验证边界 |
| [07_CONNECTION_AND_VIRTUAL_USB.md](07_CONNECTION_AND_VIRTUAL_USB.md) | 自动连接、测试页、虚拟输出和驱动入口的操作说明 |
| [08_TESTER_CALIBRATION_AND_STABILITY.md](08_TESTER_CALIBRATION_AND_STABILITY.md) | 型号布局、陀螺仪校准、震动配置与 NS1 卡死修复 |
| [09_ARENA_AND_CONTROLLER_LAB.md](09_ARENA_AND_CONTROLLER_LAB.md) | r3 浅色工作台、图片模式卡片、反馈转码、配置和验证边界 |
| [10_TRIGGER_READOUT_AND_OVERVIEW.md](10_TRIGGER_READOUT_AND_OVERVIEW.md) | r4 线性扳机显示、读数布局、驱动快捷面板与纵向概览 |
| [11_MOTION_CALIBRATION_AND_PUBLISH.md](11_MOTION_CALIBRATION_AND_PUBLISH.md) | r5 Steam 风格手柄布局、三轴姿态、手动/自动陀螺仪校准及精简发布 |
| [12_MULTI_CONTROLLER_INPUT.md](12_MULTI_CONTROLLER_INPUT.md) | r6 Y 轴修复、Windows 手柄作为输入、映射与功能缺失策略、跨型号回读验证 |
| [13_XBOX_CONTROLLER_PROTOCOL.md](13_XBOX_CONTROLLER_PROTOCOL.md) | Xbox XInput 与 360 USB / One、Series GIP / 蓝牙 HID 的通信格式和来源 |
| [14_PS5_CONTROLLER_PROTOCOL.md](14_PS5_CONTROLLER_PROTOCOL.md) | DualSense USB / 蓝牙 HID、CRC、IMU 校准、输出与能力边界 |
| [17_NS2_WAKE_RECONNECT.md](17_NS2_WAKE_RECONNECT.md) | NS2 设备记忆、唤醒回连、广播诊断与实机验收步骤 |
| [18_NS2_HOST_REGISTRATION_PROPOSAL.md](18_NS2_HOST_REGISTRATION_PROPOSAL.md) | L + R 失败诊断、本地名称、有限重试与待授权主机注册方案 |
| [19_SYNC_RECOVERY_AND_USER_DATA.md](19_SYNC_RECOVERY_AND_USER_DATA.md) | 撤销误拦截 SYNC 的条件、配置与日志入口、用户目录持久化 |

**来源边界：**02 / 03 的 Nintendo 报文和 13 / 14 的 Xbox / PS5 硬件报文主要来自开源实现与逆向资料，不是厂商完整协议或认证声明。微软 XInput 文档仅定义应用接口。各协议文档均标明来源、版本/核查日期，不将某次观察当作跨固件标准。

更新日期：2026-09-26。r6 使用本机已安装 USB/IP 完成 Xbox → NS1、NS1 → NS2、Xbox → PS5、PS5 → Xbox 的合成输入与 Windows 实际回读验证。实体手柄固件、无线稳定性和具体游戏仍需实测。历次状态文档保留当时验证边界，最新结果以 12 为准；安装器随包提供不等于已安装驱动。

## r7 / r8 名称与旧版兼容

中文名称：**手柄转发工具**；英文名称：**Controller Forwarding Tool**。窗口、侧栏、关于页、应用元数据、诊断标题和发布包使用新名称；EXE / 主程序集为 `ControllerForwardingTool.exe` / `ControllerForwardingTool.dll`。Visual Studio 默认发布目标更新为 `D:\Temp\Publish\ControllerForwardingTool`。

r8 的解决方案为 `ControllerForwardingTool.slnx`，项目为 `ControllerForwardingTool/ControllerForwardingTool.csproj`，根命名空间为 `ControllerForwardingTool`。图标资源与 `ControllerForwardingTool.pubxml` 发布配置也同步改名，Visual Studio 上次选择的发布配置引用已更新。请重新打开新解决方案。

Visual Studio 发布配置的 `PublishDir` / `PublishUrl` 均为 `D:\Temp\Publish\ControllerForwardingTool\`；脚本发布目录为 `artifacts/Publish/<版本>/ControllerForwardingTool`，输出 EXE 为 `ControllerForwardingTool.exe`。当前检出未包含历史 `NS2ProWin11.Checks` 检查工程，不将旧检查记录当成本次运行结果。

仓库根目录仍为 `D:\github\NS2ProWin11`；`%LOCALAPPDATA%\NS2ProWin11` 设置目录与虚拟设备旧序列号前缀保留，以继续使用既有校准并兼容回环过滤。r4—r6 的发布记录保留当时文件名与哈希。

新版的开机启动项显示为 `Controller Forwarding Tool`。已开启开机启动的用户在新版点击「应用并保存」时会更新 EXE 路径并移除旧的 `NS2ProWin11` 启动项；关闭开机启动会同时清理新旧两项。修改源码和构建不直接更改本机启动注册表。

r8 验证：新解决方案在独立目录和常规 `bin/obj` 路径均构建通过，0 警告 / 0 错误；已验证 XAML 页面、托盘/窗口图标、五张模式图片及程序集名称，并实际使用新发布配置生成 `D:\Temp\Publish\ControllerForwardingTool\ControllerForwardingTool.exe`。发布目录不包含旧名称 EXE、docs 或 PDB。

## 来源与引用规则

- 官方：[Nintendo Switch 2 Pro Controller 产品规格](https://www.nintendo.com/sg/hardware/switch2/accessories/controller.html)、[Nintendo Pro Controller 有线通信帮助](https://en-americas-support.nintendo.com/app/answers/detail/a_id/26315/~/how-to-enable%2Fdisable-pro-controller-wired-communication)、[微软 BLE GATT 客户端 API](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client)、[微软 UMDF HID 驱动文档](https://learn.microsoft.com/en-us/windows-hardware/drivers/wdf/creating-umdf-hid-minidrivers)。
- 参考工程：`D:\github\XinHeLianSheng-Pro2-Bridge\source\final-three-in-one`，主要关注 `windows/v60_viiper_app` 的 BLE 输入端与 `experiments/esp-switch1-r4` 的 NS1 USB 实验端。其源码和发布二进制版本存在差异，见参考工程 `README.md`。
- 社区逆向：[dekuNukem 的 Bluetooth HID 报告记录](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/bluetooth_hid_notes.md)与[子命令记录](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/bluetooth_hid_subcommands_notes.md)。这不是 Nintendo 官方资料。
