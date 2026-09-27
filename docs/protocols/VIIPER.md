# VIIPER：本项目使用的内部通信格式

[协议目录](README.md) · [NS2](NS2_PRO.md) · [Xbox](XBOX.md) · [PS5 / Edge](PS5.md)

核对日期：2026-09-27。本文格式是 C# 应用与随附 **VIIPER haptic 分支**之间的 TCP 消息，不是厂商 USB/蓝牙原始输入报告，也不能推断为所有 VIIPER 版本都兼容。NS1 完全不走此通道。

源码：[ViiperProtocolClient](../../ControllerForwardingTool/VirtualDevice/ViiperProtocolClient.cs)、[VirtualControllerSession](../../ControllerForwardingTool/VirtualDevice/VirtualControllerSession.cs)、[VirtualProfiles](../../ControllerForwardingTool/VirtualDevice/VirtualProfiles.cs)、[Ns2VirtualReport](../../ControllerForwardingTool/VirtualDevice/Ns2VirtualReport.cs)。随附后端源码归档为 [viiper-haptic-source.tar.gz](../../drivers/viiper/viiper-haptic-source.tar.gz)；本文核对应用侧收发契约，不声称重新审计/重建整个后端。

## TCP 控制与流

API/USB 服务均绑定 `127.0.0.1`，端口自动选择或来自当前线路。启动校验后端固定 SHA-256，随后 `ping`、`bus/create`、添加设备、核对类型与 VID/PID，再打开流。NS2 创建参数额外包含 `source_paced=true`、`input_interval_ms=4`；四种设备均带 `NS2PROWIN11-<Mode>` 序列号。

控制请求为 UTF-8 `path[ 空格 payload]\0`，每个请求使用独立 TCP 连接，读到 EOF，再去掉尾部 NUL/空白解析。普通请求 5 秒、创建设备 20 秒超时。JSON 响应含数值 `status` 时视为问题对象并抛错。

| 路径 | 请求负载 | 用途 |
| --- | --- | --- |
| `ping` | 无 | 就绪探测 |
| `bus/list` | 无 | 总线列表 |
| `bus/create` | `0` | 创建总线，读取 `busId` |
| `bus/<id>/list` | 无 | 设备列表 |
| `bus/<id>/add` | JSON：`type`、可选 `deviceSpecific` | 创建设备，读取 `busId/devId/vid/pid/type` |
| `bus/<id>/remove` | 设备 ID | 客户端提供此方法 |
| `bus/remove` | 总线 ID | 客户端提供此方法 |
| `bus/<id>/<devId>` | 无 | NUL 终止握手后，连接成为双向二进制流 |

二进制帧本身没有额外长度前缀，依据设备类型按固定尺寸读写，所有多字节字段小端。流读取用 `ReadExactAsync` 拼齐，不能把一次 TCP read 当成一帧。当前会话停止以 USB/IP detach 和关闭自身后端为主，不依赖调用上述 remove 方法。

| 身份 | type | 应用→后端 | 后端→应用 |
| --- | --- | --- | --- |
| NS2 `057E:2069` | `ns2pro` | 28 | 34 |
| Xbox `045E:028E` | `xbox360` | 20 | 2 |
| PS5 `054C:0CE6` | `dualsensehaptic` | 33 | 388 |
| Edge `054C:0DF2` | `dualsenseedge` | 33 | 6 |

## NS2 输入 28 字节

| 偏移 | 字段 |
| --- | --- |
| 0–3 | u32 按键掩码；bit0–20 依次为 B、A、Y、X、R、ZR、Plus、R3、Down、Right、Left、Up、L、ZL、Minus、L3、Home、Capture、GR、GL、C |
| 4/6/8/10 | u16 LX/LY/RX/RY，内部 0–4095、Y 向上 |
| 12/14/16 | i16 Accel X/Y/Z |
| 18/20/22 | i16 Gyro X/Y/Z |
| 24–27 | u32 运动时间戳，微秒 |

此掩码与 FD2 原始掩码、`ControllerButtons` 枚举值均不同。

## Xbox 输入 20 字节

| 偏移 | 字段 |
| --- | --- |
| 0–3 | u32 按键：Up/Down/Left/Right `1/2/4/8`；Plus/Minus `10/20`；L3/R3 `40/80`；L/R `100/200`；Home `400`；内部 B/A/Y/X 映到 `1000/2000/4000/8000`（均十六进制） |
| 4/5 | u8 左/右模拟扳机 |
| 6/8/10/12 | i16 LX/LY/RX/RY，Y 向上 |
| 14–19 | 当前编码为零 |

轴中心 2048→0，负端 0→−32768，正端 4095→32767。真实 Xbox 360 USB 的前两字节通常是包型/长度，而这里前四字节是按钮；即使长度同为 20 也不能混用。

## PS5 / Edge 输入 33 字节

| 偏移 | 字段 |
| --- | --- |
| 0/1/2/3 | i8 LX/LY/RX/RY（用 byte 保存补码），范围 −128–127，Y 反向 |
| 4–7 | u32 按键掩码 |
| 8 | 方向位：Up/Down/Left/Right=`1/2/4/8`，**不是 HID Hat 值** |
| 9/10 | u8 左/右扳机 |
| 11–20 | 当前编码为零，不传触摸点等数据 |
| 21/23/25 | i16 `(GyroX×Pitch, GyroZ×Yaw, -GyroY×Roll)`，分别应用反向设置 |
| 27/29/31 | i16 `(AccelX, AccelZ, -AccelY)×2` |

所有体感结果四舍五入并饱和至 i16。按钮掩码：内部 Y/B/A/X→`10/20/40/80`；L/R/ZL/ZR→`100/200/400/800`；Minus/Plus/L3/R3→`1000/2000/4000/8000`；Home→`10000`，Capture 或 Touchpad→`20000`。Edge 额外 GL/GR→`00400000/00800000`，普通 PS5 忽略这两个背键。上述掩码均十六进制。

## 反馈格式

入口：[Pro2OutputPacketMapper](../../ControllerForwardingTool/VirtualDevice/Source/Pro2OutputPacketMapper.cs)、[DualSenseHapticFeedback](../../ControllerForwardingTool/VirtualDevice/Source/DualSenseHapticFeedback.cs)。

| 身份 | 布局及处理 |
| --- | --- |
| NS2 34 字节 | `0..15` 左块、`16..31` 右块、`32` 标志、`33` 玩家灯掩码。标志 bit0 为震动、bit1 为灯；无震动标志返回不映射。灯可保留在对象中，但没有实体 LED 写回 |
| Xbox 2 字节 | `[0]` strong、`[1]` weak，传入 `BuildOrdinaryPacket(weak,strong)` |
| Edge 6 字节 | `[0]` weak、`[1]` strong；当前转换忽略余下四字节，仅普通震动 |
| PS5 388 字节 | `[0]` kind（1 HID、2 PCM）、`[1..2]` u16 有效负载长度（≤384）、`[3]` 当前不读取、`[4..]` 负载；必须收到完整 388 字节 |

普通 PS5 会话使用 388 字节调度器；不能因为通用 mapper 中还存在 DualSense 的两字节分支，就把实际 PS5 流误写成两字节。未知 kind 由调度器拒绝，长度和 HID ID 分别检查；PCM 只使用触觉声道，见 [PS5](PS5.md)。

转为 raw02 后经 BLE 或 SDL 回到输入设备。普通强度是持续状态，HD/PCM 是流式状态。输入发送保留最新值；250 ms 失去源数据后中立，频率策略见[架构](../01_ARCHITECTURE.md)。
