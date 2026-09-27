# 协议完整性与代码一致性核对

适用版本：**1.0.1**；核对日期：2026-09-27，范围为当前工作区 C# 输入/输出实现、现存协议文档和随附依赖的职责边界。

**结论：原文档不够全面，且存在已过时的功能描述。** 本次补齐了应用自行处理的 NS2 BLE、NS1 USB/HID、四种 VIIPER 内部流和 NS1 USB/IP 传输，并修正与源码不一致的结论。现在可作为当前实现的维护入口，但仍不是四家设备的完整官方协议手册，也不表示所有实体固件已验收。

## 覆盖矩阵

| 协议/层 | 代码实际负责 | 当前文档 | 尚未完整覆盖/实现 |
| --- | --- | --- | --- |
| NS2 BLE 输入 | 广播筛选、GATT、14 条初始化、FD2、部分电量 | [NS2](NS2_PRO.md) | 未知 FD2 字段、全部命令语义、工厂校准、多固件时序 |
| NS2 主机注册 | 条件触发、4 阶段交换、AES/回复校验 | [NS2](NS2_PRO.md) | 不能等同 Windows SMP 完整配对或跨固件保证 |
| NS2 USB 管理与恢复 | libusb 状态读取、注册 / 删除、读回验证、SDL 占用重试 | [USB 注册](../29_NS2_USB_REGISTRATION.md) | 实体注册、拔线回连及删除再注册待验收 |
| NS2 震动 | raw02 转 33 字节 BLE、持续/流式生命周期 | [NS2](NS2_PRO.md) | 完整波形保真、实体 LED、所有震动命令 |
| NS1 虚拟设备 | 描述符、0x30、0x21、0x81、合成 SPI/IMU、震动转码 | [NS1](NS1_PRO.md) | 0x3F/0x31 输入、NFC/IR、真实电量、完整设备状态机 |
| Xbox 实体输入 | SDL 标准状态映射 | [Xbox](XBOX.md)及参考资料 | 原生 GIP/蓝牙/无线适配器驱动由依赖负责 |
| PS5/Edge 实体输入 | SDL 标准状态和传感器转换 | [PS5](PS5.md)及参考资料 | 工厂校准/CRC/增强模式由 SDL 负责；不转触摸轨迹等完整能力 |
| 其他 Windows 手柄 | SDL 数据消费、布局识别、原始测试 | [SDL 输入](SDL_INPUT.md) | DualShock 3/4、通用 HID 等原始协议未完整编写；不是全部按钮无损透传 |
| 键鼠与混合输入 | 通用映射、按钮合并、摇杆方向、鼠标体感、断连归零 | [架构](../01_ARCHITECTURE.md)及[混合输入](../../ControllerForwardingTool/docs/31_HYBRID_INPUT_MAPPING.md) | 应用内状态，不是实体 HID 协议；游戏兼容性需实测 |
| VIIPER 应用协议 | TCP 控制、28/20/33 字节输入与 34/2/388/6 字节反馈 | [VIIPER](VIIPER.md) | 不构成对后端全部 USB 实现的审计 |
| NS1 USB/IP | 单设备 DEVLIST/IMPORT/SUBMIT/UNLINK | [USB/IP](USBIP.md) | 通用 USB、多设备、等时传输和完整错误处理 |

## 发现及修正

| 原文档问题 | 源码事实及本次处理 |
| --- | --- |
| Xbox/PS5 资料称 NS1 不支持体感，PS5→NS1 为零 | `Ns1ReportEncoder` 已转换 IMU，`UsbIpNs1Device` 按 `0x40` 开关；新文档和归档中的过时结论已改正 |
| NS2 文档把参考工程的 15 条初始化作为当前流程 | `ObservedSequence` 实为 14 条，无无协商 `15/03`；逐条列出当前字节，并区分独立注册 |
| 混入“写描述符 133”和参考工程工厂校准流程 | 当前 BLE 实现请求 Windows 高吞吐参数，未实现这些参考流程；历史内容明确归档 |
| NS2 只记 FD2 部分偏移，缺少电量、按键、注册/震动具体格式 | 补齐解析约束、按钮掩码、电压/档位、注册四阶段及 33 字节震动布局 |
| NS1 主要描述 Eden 输入端，缺少本项目设备端应答 | 新增描述符、报告、子命令 ACK/实际效果、SPI 与 IMU 转换；Eden 研究保留参考性质 |
| VIIPER 内部消息与真实 USB 报文虽有提示但无完整字段表 | 新增四种内部输入及反馈表，明确相同长度不代表相同格式 |
| PS5 与 Edge 高级反馈边界不清 | 普通 PS5 使用 388 字节 HID/PCM 调度器，Edge 6 字节只消费前两字节普通强度 |
| 实现声明容易被理解为完整支持 | 标出固定电量、只 ACK 的 NS1 命令、仅声明但未生成的 Report ID、未写回 LED 与零填充字段 |
| 引用依赖本机工作区或可选构建目录 | 当前文档改为仓库内源码链接、随附归档及成员路径；历史绝对路径保留为环境记录，不当作可移植入口 |

## 代码核对入口

- NS2：[Fd2Decoder](../../ControllerForwardingTool/Protocol/Ns2/Fd2Decoder.cs)、[Ns2Battery](../../ControllerForwardingTool/Protocol/Ns2/Ns2Battery.cs)、[初始化](../../ControllerForwardingTool/Bluetooth/Ns2InitProfile.cs)、[注册](../../ControllerForwardingTool/Bluetooth/Ns2PairingProtocol.cs)、[BLE 传输](../../ControllerForwardingTool/Bluetooth/Ns2BleTransport.cs)。
- NS1：[报告编码](../../ControllerForwardingTool/Protocol/Ns1/Ns1ReportEncoder.cs)、[设备应答](../../ControllerForwardingTool/VirtualDevice/UsbIpNs1Device.cs)、[USB/IP 服务](../../ControllerForwardingTool/VirtualDevice/UsbIpPrototypeServer.cs)。
- 跨型号：[输入桥](../../ControllerForwardingTool/Input/BridgeInput.cs)、[输出编码](../../ControllerForwardingTool/VirtualDevice/VirtualProfiles.cs)、[NS2 内部帧](../../ControllerForwardingTool/VirtualDevice/Ns2VirtualReport.cs)、[VIIPER 客户端](../../ControllerForwardingTool/VirtualDevice/ViiperProtocolClient.cs)。
- 反馈：[普通/NS1/NS2 转换](../../ControllerForwardingTool/VirtualDevice/Source/Pro2OutputPacketMapper.cs)、[BLE 编码](../../ControllerForwardingTool/VirtualDevice/Source/Pro2BleRumblePacketEncoder.cs)、[PS5 调度](../../ControllerForwardingTool/VirtualDevice/Source/DualSenseHapticFeedback.cs)、[持续播放](../../ControllerForwardingTool/Bluetooth/BleRumblePlayback.cs)。

## 验证边界与剩余缺口

现存 [ControllerProtocolChecks](../../tools/ControllerProtocolChecks/Program.cs) 覆盖 NS1 输入/IMU/SPI、反馈转换、报告率、TCP 回环、USB 注册 / 恢复、键鼠捕获、多源映射、摇杆编辑及草稿预览；它不是所有协议的完整回归集。本次 1.0.1 版本与文档同步不更改协议实现，不执行实体注册写入、驱动挂载或固件操作。

仍缺少可重复的完整测试：FD2 全部字段/异常值、14 条初始化 ACK 语义、注册所有错误分支、每种 VIIPER 输入/反馈格式、PS5 PCM/Edge 差异、多适配器和多固件抓包对照。现有历史验证记录不能替代这些当前可运行的测试。新增硬件结论应记录型号、固件、连接方式、原始报文、动作及接收端实际结果。

本次执行 `dotnet run --project tools/ControllerProtocolChecks -c Release --no-restore`，**419 项通过**；Release 构建和配置检查通过，详见[构建验证记录](../04_BUILD_AND_VALIDATION.md)。早前 34 项检查及初始化命令比对属于上一轮记录，本次未重复硬件验收。

当前完整性应理解为“已说明主要实现路径及其限制”，不能理解为“未说明的字段必为零”或“所有声明的设备能力均已实现”。[返回协议目录](README.md)
