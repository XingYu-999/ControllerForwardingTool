# NS2 Pro 通信协议参考：官方资料核查与非官方观察

> 历史研究归档：本文保留 2026-09-25 的参考工程观察，不描述当前应用的完整实现。当前初始化为 14 条，注册、电量与震动细节见 [NS2 当前协议](../NS2_PRO.md)。本机参考仓库路径仅用于追溯当时环境。

> **不是 Nintendo 官方协议手册。** 截至 2026-09-25，公开 Nintendo 页面可确认 Switch 2 Pro Controller 具有 Bluetooth、NFC 等通信能力，但本次核查未发现面向公众的 GATT UUID、初始化命令和 FD2 字节布局规范。[Nintendo 产品规格](https://www.nintendo.com/sg/hardware/switch2/accessories/controller.html)。以下低层细节来自参考工程 `source/final-three-in-one/windows/v60_viiper_app/` 的实现观察；需要用真实手柄、固件版本和抓包验证。

## 1. 协议位置

本方案使用 Win11 的 BLE Central / GATT Client 直接连接真实 NS2 Pro。它不是 NS1 Pro 的 Bluetooth Classic HID，也不是让 Windows 自带的游戏手柄 HID 驱动直接识别真实设备。Windows 端可使用[微软 BLE GATT 客户端 API](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client)。

数据路径：`广告筛选 → 按地址打开 BluetoothLEDevice → 发现 GATT → 命令/ACK 初始化 → FD2 Notify → 解码按钮/摇杆/IMU → 写回震动`。

## 2. 已观察的 GATT 通道

| 用途 | UUID | 依据与确定性 |
| --- | --- | --- |
| FD2 主输入通知 | `ab7de9be-89fe-49ad-828f-118f09df7fd2` | 参考实现固定 UUID；非官方，需设备枚举确认 |
| 旧输入通知 | `7492866c-ec3e-4619-8258-32755ffcc0f8` | 参考实现固定 UUID；该项目最终 live 路线要求 FD2 与 IMU |
| 命令 ACK | `c765a961-d9d8-4d36-a20a-5315b111836a` | 参考实现固定 UUID |
| 命令写入 | `649d4ac9-8eb7-4e6c-af44-1ea54fe5f005` | 参考实现固定 UUID |
| 震动写入 | `cc483f51-9258-427d-a939-630c31f72b05` | 参考实现固定 UUID；并非把 USB 报文原样写入 |

来源：`D:\github\XinHeLianSheng-Pro2-Bridge\source\final-three-in-one\windows\v60_viiper_app\Pro2BleInputSource.cs` 第 61–67 行。参考项目还用制造商字段 `0x0553` 和广告名称寻找候选，但它们只能作**候选筛选**，不具备鉴别真伪的能力（同文件第 1508–1545 行）。UUID 与 handle 均应动态发现；不要把某次连接的属性 handle 硬编码为标准。

## 3. 初始化与通知时序

参考实现发送一个 15 条命令的数组，每条发向命令特征并等待 ACK，超时会记日志继续；随后尝试读取工厂 IMU 校准块，尝试向 FD2 附近的非 CCCD 描述符写入小端 `133` 作为期望报告速率，再写 CCCD 开启 Notify。**133 是请求值，不是已验证的实际 Hz。** 之后必须等待可解析的 FD2 和实时 IMU 样本，不能只凭 GATT 连接成功宣布游戏可用。见参考文件第 77–100、742–909、1043–1147 行。

这组命令的逐项语义没有 Nintendo 官方公开说明。新工程应按“捕获到的版本化初始化脚本”记录完整原始字节、命令序号、ACK 和超时；禁止用推测的字段名编成“官方命令表”。第一阶段可只实现必要命令并用实机记录逐步确认。

## 4. FD2 输入报文：参考实现的字节观察

以下偏移从**FD2 Notify 的 payload 第一个字节**算起，不包含 BLE ATT/GATT 包头。参考解析器拒绝短于 60 字节的 FD2，并对按键高位做基本合理性检查；这是该软件的解析约束，不代表所有固件只会发 60 字节。参考实现（历史本机路径：`D:/github/XinHeLianSheng-Pro2-Bridge/source/final-three-in-one/windows/v60_viiper_app/Pro2HidReportParser.cs`）。

| 偏移 | 参考解释 | 验证状态 |
| --- | --- | --- |
| `4..7` | 小端 `uint32` 按钮位集合 | 源码解析行为已确认；各位与实际按键须逐键测量 |
| `10..12` | 左摇杆两轴的打包 12 位数据 | 源码解析行为已确认；中心/行程依设备校准 |
| `13..15` | 右摇杆两轴的打包 12 位数据 | 同上 |
| `42..45` | 小端 `uint32` 运动时间戳（微秒） | 源码按此读取；时间基准待验证 |
| `48..59` | 一组 IMU 样本，参考实现按 3 个轴加速度、3 个轴角速度解析 | 坐标、单位与标定需真机验证 |

12 位轴解包、按钮位掩码、IMU 的具体坐标转换见参考文件 `Pro2HidReportParser.cs` 第 272–365 行。开发时应分别保存**原始值**、**校准后值**和**输出到 NS1 的值**，便于定位方向颠倒与精度损失。不能把参考实现的过滤策略误当成手柄协议本身。

## 5. 输出与异常

- 反馈方向由游戏的虚拟 NS1 输出命令开始，先解析为抽象的左右震动意图，再编码为 NS2 Pro BLE 震动报文；参考文件 `Pro2OutputPacketMapper.cs`、`Pro2BleRumblePacketEncoder.cs` 和 `Pro2BleInputSource.cs` 第 2415–2525 行展示了限速、合并和写入队列的思路。
- GATT Write 成功只说明 Windows 接口接受本次操作，不保证真实马达效果。停振、切换模式和断连必须单独测试。
- 固件可能改变广告名称、UUID、数据长度或初始化要求。版本发现、原始报文保存、未知字段保留、失败后释放 GATT 对象都是必要设计。

## 6. 还需实机补齐的“协议空白”

设备固件版本与地址类型、全部初始化命令的必要性、ACK 编码、FD2 完整字段表、GL/GR/C 键、IMU 单位/温度/校准、震动编码与停止语义、加密/配对要求、不同蓝牙适配器的连接参数。填写这些内容时应附“手柄型号/固件/抓包日期/原始报文/实验动作/期望与实际”，并标记为**实测观察**，不能改称 Nintendo 官方规定。
