# 实现与验证状态（2026-09-26）

> 历史记录（2026-09-27 标注）：下文保留当时的设计、功能状态与验证结果。当前实现、使用方法和可运行检查工程请从[当前文档目录](../../docs/README.md)查阅；历史“待实现”、地址探测、测试卡片及工程路径描述可能已被后续实现替代。

`01`—`05` 包含早期设计与协议研究；最新使用方法以 [12_MULTI_CONTROLLER_INPUT.md](12_MULTI_CONTROLLER_INPUT.md) 和实际代码为准。

## r6 当前增量

已修复 NS2 BLE 测试图 Y 轴方向，接入 Windows 标准映射手柄作为虚拟输入，保留模拟扳机、有效校准体感及普通震动回传，缺少能力默认无输出。加入输入选择、断线归零、来源隔离和本应用虚拟设备排除。Xbox / PS5 协议分别见 [13](13_XBOX_CONTROLLER_PROTOCOL.md)、[14](14_PS5_CONTROLLER_PROTOCOL.md)。

本机现有 USB/IP 环境已完成 Xbox → NS1、NS1 → NS2、Xbox → PS5、PS5 → Xbox 四条链路的合成输入及真实 Windows 设备回读；验证面键位置、两根摇杆方向/行程、模拟扳机和断线归零。此结果不代替实体控制器与游戏验收。以下 r3 和原型段落保留历史阶段状态。

## r3 增量

浅色测试工作台、五种图片身份、PS5/Xbox/Edge 报文、源项目震动转码与音频保护、配置保存和诊断导出已经接入。详见 [09](09_ARENA_AND_CONTROLLER_LAB.md)。本轮四种 VIIPER 模式通过本地后端测试；当前缺少 USB/IP 客户端，没有做新增模式 Windows 挂载，以下挂载记录属于前两轮。

## 已实现

- Windows 11 x64、.NET 10、Avalonia 12 桌面应用。测试页包含曲线手柄图、按钮高亮、双摇杆坐标图、全部按钮/轴读数、电量与可用传感器。
- 第二轮测试页：Switch / Xbox / DualShock / DualSense 型号布局，陀螺仪原始值和零偏校准值、3 秒静置采样及失败保护，震动时间与双频力度；导航图标、选中态和折叠。见 [08](08_TESTER_CALIBRATION_AND_STABILITY.md)。
- NS1 卡死：USB/IP 接收循环不再继承 UI 同步上下文，SDL 全部设备调用统一在专用线程，界面只消费快照。
- NS2 BLE 启动即扫描、候选串行连接、保持 GATT 会话、未缓存服务发现、初始化写入和订阅重试、有效首帧确认、断线归零和重连。单次连接限时 60 秒。
- 修正原第 7 条 BLE 初始化命令多一个 `00` 的长度错误；15 条命令逐字节匹配参考程序，加入负载长度回归检查。
- SDL 3 HIDAPI 检测标准手柄和未知 HID；自动热插拔和设备选择，支持 NS1 的 USB/蓝牙初始化。NS2 USB 使用启用 libusb 的定制 SDL。
- 原生 NS2 Pro USB 输出：随附 VIIPER 服务进程，28 字节输入状态（含运动时间戳），创建总线和设备、USB/IP 自动挂载、停止与退出时清理。
- NS1 Pro USB 输出：C# USB/IP 服务、描述符/握手、0x30 输入报告、子命令和 SPI 合成校准应答。NS2 同名按键与摇杆转换；C/GL/GR 不映射。
- 驱动安装器和已注册驱动/客户端状态；安装/修复、卸载、刷新入口。安装器检查固定哈希和嵌入签名者。卸载使用已安装包的原厂向导。

## 已完成的验证

1. 解决方案构建通过，0 错误、0 警告。
2. FD2 边界、按钮/轴/IMU 字段，NS1 报告编码，SPI 校准和超长读取拒绝，VIIPER 28 字节编码，USB/IP 列举/导入/控制传输检查通过。
3. 使用本机已有 USBip 驱动实际挂载 `057E:2069` 和 `057E:2009`；SDL 识别到 Nintendo Switch Pro Controller。合成按键和摇杆输入回读、恢复中立状态通过，测试创建的设备已清理。
4. 桌面窗口 UI 检查：中文渲染、测试页布局和虚拟手柄/驱动页面。未执行驱动安装或卸载。
5. 型号标签、震动范围、静置校准/移动拒绝/旧偏差保留/重复样本/数据延迟/断流/重置检查通过；无 UI 消息泵条件下的 USB/IP 应答检查通过。
6. NS1 专用输入线程连续三轮挂载、合成输入回读、断流归零和停止通过；实际 Avalonia 窗口启动 NS1 后，可切换测试页和折叠导航，并设置震动参数。
7. 实体 NS2 USB 通过 SDL 读取真实陀螺仪，3 秒静置校准采集 753 个样本并成功应用零偏，静置校准后角速度接近零。详细观测值见 [08](08_TESTER_CALIBRATION_AND_STABILITY.md)。不等于 BLE 校准或长期漂移验收。

上述第 3 项验证的是 Windows 虚拟 USB 输入链路，**不是实体 NS2 BLE 全链路验证**。

## 明确边界

- 当前没有收到真实 NS2 的 BLE 帧；首次连接、不同固件、断电回连和真实方向仍待实机复核。
- NS1 当前支持 0x30 完整输入模式；其他输入模式不返回成功 ACK。NS1 IMU 保持中立，主机震动/灯光尚未反向发送到实体 NS2。
- NS2 传递 IMU 和原始运动时间戳，可应用本次 BLE 静置校准的角速度零偏；方向、比例与校准效果仍待真实固件验证。
- SDL 支持取决于设备和系统配对；不能保证所有第三方手柄、蓝牙适配器、Steam/Eden/游戏均兼容。
- NS2 USB 初始化接口存在独占约束，开始游戏前离开 Windows 手柄测试页；其他程序正在占用时可能无法检测。
- 自定义按键映射编辑仍未实现。配置保存、诊断导出已在 r3 完成；源软件四人槽位与实验后端未迁移。

## 可重复检查

在解决方案根目录运行：

```powershell
dotnet build .\ControllerForwardingTool.slnx
dotnet run --project .\NS2ProWin11.Checks
dotnet run --project .\NS2ProWin11.Checks -- --devices
# 需要已安装 USBip；创建临时设备并在 finally 清理
dotnet run --project .\NS2ProWin11.Checks -- --attach
# 只检查 NS1
dotnet run --project .\NS2ProWin11.Checks -- --attach --ns1
dotnet run --project .\NS2ProWin11.Checks -- --attach-monitor
```

默认检查不挂载驱动设备。`--attach` 会改变本机的临时 USB 设备状态，不执行安装/卸载驱动。依赖版本、来源、源码与许可证见 [04_DRIVER_AND_LICENSE.md](04_DRIVER_AND_LICENSE.md)。
