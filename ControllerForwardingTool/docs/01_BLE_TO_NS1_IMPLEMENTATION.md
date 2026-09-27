# NS2 Pro BLE 接入 Win11 并输出虚拟 NS1 Pro：实施方案

> 历史记录（2026-09-27 标注）：下文保留当时的设计、功能状态与验证结果。当前实现、使用方法和可运行检查工程请从[当前文档目录](../../docs/README.md)查阅；历史“待实现”、地址探测、测试卡片及工程路径描述可能已被后续实现替代。

> 2026-09-26 实现更新：本文件保留早期架构方案。当前已实现自动 BLE 连接流程、SDL 通用测试、VIIPER NS2 USB 与 C# NS1 USB 自动挂载；采用开源后端和运行库。当前行为和验证边界见 [06](06_PROTOTYPE_STATUS.md) 与 [07](07_CONNECTION_AND_VIRTUAL_USB.md)。下文“本阶段只编写文档”等表述仅指最初文档阶段。

## 1. 目标与边界

真实 NS2 Pro 通过电脑蓝牙适配器向 .NET 10 程序提供输入；程序解析按键、摇杆和 IMU，转换为统一状态；输出端在 Windows 中创建**虚拟 USB NS1 Pro**，让 Steam/游戏识别为 Switch 1 Pro。游戏的灯光、震动等输出命令反向转换并发给真实手柄。

```text
NS2 Pro  --BLE GATT-->  Avalonia/.NET 10 用户态程序
                           | 扫描、连接、通知、解析、状态映射
                           v
                      NS1 Pro 输出服务  --虚拟 HID/USB 驱动--> Windows / Steam / 游戏
                           ^                                    |
                           +-------- 震动/灯光反馈 ---------------+
```

“通过蓝牙连接电脑”只描述输入段；输出段建议以虚拟 USB 呈现。若要求 Windows 蓝牙列表中再出现一只可被其他设备配对的 NS1 Pro，那是另一项蓝牙外设模拟工程，不是本方案的目标。输出 `057E:2009`、产品名或 Nintendo 商标仅用于兼容性研究；正式分发时应核实设备标识、商标及相关授权。

## 2. 对当前项目的影响

现有 `ControllerForwardingTool.csproj` 是 `net10.0`、Avalonia `12.1.2`。WinRT BLE API 应将目标框架改成 Windows 专用 TFM，例如 `net10.0-windows10.0.22621.0`，并按最低运行系统设置 `SupportedOSPlatformVersion`；Avalonia UI 与 BLE 实现分层，避免 UI 线程处理报文。微软的[桌面应用调用 Windows Runtime API 文档](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-enhance)给出了 .NET 10 TFM 示例。**本阶段只编写文档，没有修改项目文件。**

建议的程序集/命名空间分工：

| 模块 | 责任 | 外部依赖 |
| --- | --- | --- |
| `Bluetooth/` | 适配器检查、广告扫描、候选选择、GATT 会话、重连 | Windows Runtime BLE API |
| `Protocol/Ns2/` | 初始化序列、FD2 校验和解析、校准、震动写回 | 仅协议数据类型 |
| `Core/` | 不可变/值类型手柄状态、映射规则、时间戳、断连归零 | 无 UI/驱动依赖 |
| `Protocol/Ns1/` | `0x3F`/`0x30` 输入报告、`0x21` 子命令回复、输出命令解析 | 仅协议数据类型 |
| `VirtualDevice/` | 虚拟设备创建/移除、输入提交、输出接收 | **需选择驱动方案** |
| `ViewModels/`、`Views/` | 扫描、连接、状态、日志、设置与故障提示 | Avalonia 12 |

接口建议：`INs2Transport` 只产出原始通知并提供命令写入；`INs2Decoder` 将字节解析为 `ControllerState`；`INs1Encoder` 产生输出报告并处理主机命令；`IVirtualGamepad` 负责设备生命周期。这能允许离线回放报文、单测映射，而不要求测试机安装驱动。

## 3. BLE 输入实现

1. **扫描。** 使用 `BluetoothLEAdvertisementWatcher`，记录地址、地址类型、名称、RSSI 和制造商数据。参考实现以 Nintendo 公司字段 `0x0553` 或名称匹配为候选，再对保存过的地址优先排序。广播名称只是筛选线索，必须在连接后以 GATT 特征和实时有效报文确认设备。[微软 BLE GATT 客户端](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client)。
2. **连接。** 使用 `BluetoothLEDevice.FromBluetoothAddressAsync(address, addressType)`，枚举未缓存 GATT 服务和特征。先发现命令、ACK、输入、震动通道；没有必要通道时退出并释放对象。不要要求用户先在 Windows“添加蓝牙设备”中完成普通 HID 配对；这个方案的应用自己作为 GATT 客户端。
3. **初始化。** 串行发送经过实机确认的命令，给 ACK 设置超时并记录原始结果。参考项目 `Pro2BleInputSource.cs` 有 15 条初始化报文，但其语义未被 Nintendo 官方确认；第一版应把命令序列做成可版本化的只读 profile，不要把未知字节解释成官方字段。
4. **订阅与解析。** 写 CCCD 开启 FD2 Notify。输入通知只做轻量复制、时间戳和入队；解析器校验最小长度、字段范围和序列，再输出统一状态。FD2 参考布局见 [02_NS2_PRO_PROTOCOL_REFERENCE.md](02_NS2_PRO_PROTOCOL_REFERENCE.md)。没有收到可解析的实时报告时，不宣布“连接成功”。
5. **断线处理。** 立即向虚拟设备发送中立状态，停止震动写回，释放 GATT 事件与会话；按有限退避重扫上次地址。不要把短暂通知间隔当作按键松开，也不要在长时间断线后继续保持按下状态。日志记录连接阶段与失败代码，避免记录全量个人设备地址。

参考工程可见调用链：`windows/v60_viiper_app/Pro2BleInputSource.cs`（扫描/GATT/输出）、`Pro2HidReportParser.cs`（FD2 解析）、`Pro2BleRumblePacketEncoder.cs`（震动）。这些是**研究来源**，新实现不要逐行翻译源文件。

## 4. NS1 Pro 输出实现

先定义“目标应用能识别什么”。Steam 可能接受某些通用 HID，但若要求以 NS1 Pro 身份及陀螺仪/震动完整工作，仅改名称和 VID/PID 不够；还要实现 HID 报告描述符、USB 初始化、子命令回复、输入报告和主机反馈。[03_NS1_PRO_PROTOCOL_REFERENCE.md](03_NS1_PRO_PROTOCOL_REFERENCE.md)列出需要验证的最小集合。

| 路线 | 具体工作 | 本项目适用性 |
| --- | --- | --- |
| 自有 Windows HID 驱动 | 用 WDK 编写并签名 UMDF/KMDF HID 源驱动，应用通过受限 IOCTL/管道送入状态 | 最符合“不复用现成开源输出项目”，但开发与发布成本最高。微软[UMDF HID 文档](https://learn.microsoft.com/en-us/windows-hardware/drivers/wdf/creating-umdf-hid-minidrivers)可作起点。 |
| USB/IP 驱动 + 自有设备服务 | 安装 `usbip-win2`；自己实现本机 USB/IP 服务和 NS1 USB 行为，由驱动挂载 | 可用于原型。已复制的**第三方 BSD-2-Clause 开源驱动安装器**只是驱动，不含现成 NS1 服务；不能复制 VIIPER 的 GPL 代码后称为独立实现。 |
| 外接 USB 开发板 | .NET 程序接 BLE 后向板发送状态，板通过 USB HID 接电脑 | 不需 Windows 虚拟驱动，但增加硬件和第二条传输链路。 |

本次只把现有参考仓库中的 USBIP 安装器作为**可选原型依赖**复制到解决方案根目录的 `drivers/usbip-win2/`，未安装、未加入构建或自动执行。若最终决定完全不使用第三方开源驱动，应移除这个目录并选择自有驱动/硬件方案。[驱动清单](04_DRIVER_AND_LICENSE.md)。

## 5. 映射与性能规则

- 对输入保留“设备采样时间、主机收到时间、送入虚拟设备时间”三个时间点，统计中位数和 P95。BLE 速率请求与实际通知速率分开记录；虚拟 USB 轮询频率不能被当成真实手柄采样率。
- 按钮转换用明确映射表；摇杆以校准中心/行程转成目标范围；NS2 的额外 GL/GR/C 键需要用户映射策略，NS1 Pro 没有完全对应的原生按钮。IMU 要定义轴方向、比例及缺样时行为。
- 使用有限容量队列。高频摇杆状态可以丢弃旧样本取最新；按钮边沿不能因队列覆盖而丢失。断连发布一次中立帧，再按需要低频保活。
- C# 的 `ReadOnlySpan<byte>`/`BinaryPrimitives` 足以解析几十字节报文。先测量再决定是否优化；不应为“可能更快”而给每一帧增加 C++ DLL 调用。微软[减少分配与复制的性能指南](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/)适用于热路径。
- 反馈单独排队并限速，震动停止命令优先处理。游戏主机输出必须按目标协议解析，不能把 NS1 振动字节直接发给 NS2 BLE 特征。

## 6. 实施顺序与验收

1. **采集原型：** Avalonia 中显示候选与连接状态；保存脱敏的 FD2 报文样本。验收：按键/摇杆/IMU 在 10 分钟会话中持续更新，断连和重新连接可恢复。
2. **纯逻辑层：** 对样本实现 NS2 解析、统一状态与 NS1 `0x3F`/`0x30` 编码。验收：固定样本映射结果一致，边界长度不会越界，断连归零正确。
3. **最小虚拟设备：** 完成一个 NS1 USB 设备实例，先验证枚举和按钮/摇杆，再补子命令、陀螺仪和震动。验收：Windows 设备管理器与 Steam 能看到目标设备，重复启动/退出不留下设备残留。
4. **双向闭环：** 实机验证震动、LED、睡眠回连、蓝牙干扰、不同 Windows 11 蓝牙适配器。记录实际 BLE/虚拟输出速率及端到端 P95 延迟。
5. **发布前：** 固定依赖版本和哈希，检查驱动签名与安装/卸载流程，按实际分发内容审查许可证及设备身份使用。

当前仓库文档与参考工程**不能证明**第 3、4 步已经适用于新的 Avalonia 项目。尤其 NS1 USB 输出的驱动/设备服务是本项目尚未开发的核心部分。
