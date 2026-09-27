# NS1 Pro：当前虚拟 USB/HID 实现

[协议目录](README.md) · [USB/IP](USBIP.md) · [核对结果](COVERAGE.md)

核对日期：2026-09-27。实体 NS1 USB/系统蓝牙输入由 SDL 处理；本文描述本项目自己实现的虚拟 NS1 **设备端**。它已支持 IMU 与震动转码，不能继续按早期“仅按键、摇杆”的原型说明理解。

## 描述符与报告

[UsbIpNs1Device](../../ControllerForwardingTool/VirtualDevice/UsbIpNs1Device.cs) 提供 `057E:2009`，产品字符串 `Pro Controller`，序列号 `NS2PROWIN11-LOCAL`，USB/IP bus ID 为 `1-1`。描述符由本项目组合，不声称逐字节复制零售手柄。

配置包含一个 HID 接口、64 字节中断 IN `0x81`（间隔 1 ms）和 OUT `0x01`（8 ms）。端点间隔不等于应用实际采样率。HID 描述符声明输入 ID `30/21/81/3F`、输出 `01/10/80/82`；**声明某 ID 不代表完整实现其行为**：当前周期输入只有 `0x30`，未生成 `0x3F` 简单模式，也没有 `0x82` 专门处理。

控制请求实现 GET_DESCRIPTOR、GET/SET_CONFIGURATION、GET_STATUS、GET_INTERFACE，以及 HID GET_REPORT、GET_IDLE、SET_REPORT、SET_IDLE、GET_PROTOCOL。SET_REPORT 缺少 Report ID 时按 `wValue` 补前缀再交给 OUT 处理。其他请求多返回空负载，没有完整的标准请求验证和 STALL 状态模型；这是一套兼容子集。

## `0x30` 输入：64 字节

布局从 HID Report ID 开始，见 [Ns1ReportEncoder](../../ControllerForwardingTool/Protocol/Ns1/Ns1ReportEncoder.cs)：

| 偏移 | 内容 |
| --- | --- |
| 0 | `30` |
| 1 | 每次编码递增的 byte 计时值，自动回绕 |
| 2 | 固定 `80` 电量占位，不传递实体 NS2 电池状态 |
| 3 | Y/X/B/A 位 `01/02/04/08`，R/ZR 位 `40/80` |
| 4 | Minus/Plus/R3/L3/Home/Capture 位 `01/02/04/08/10/20` |
| 5 | Down/Up/Right/Left 位 `01/02/04/08`，L/ZL 位 `40/80` |
| 6–8 / 9–11 | 左/右 12 位摇杆打包；范围 0–4095 |
| 12 | 当前为零 |
| 13–24 / 25–36 / 37–48 | 三个 12 字节 IMU 槽，当前重复同一个最新样本 |
| 49–63 | 当前为零 |

摇杆打包为 `b0=X低8位`、`b1=(X>>8)|(Y<<4)`、`b2=Y>>4`。原始 C/GL/GR 不直接写入 NS1 报告；若先通过映射转换成受支持按钮，则按映射结果编码。

IMU 会话初始关闭，主机 `0x40` 子命令启用后才填充；源失效 250 ms 后状态归零。每槽按 i16 小端顺序写 `(AccelY, -AccelX, AccelZ)` 和 `(GyroY, -GyroX, GyroZ) × 13371/(936×16.384)`，四舍五入并饱和到 i16。加速度使用项目的 4096 counts/g 约定，合成 SPI 校准与此转换配套。三槽重复不表示三次独立物理采样。

## OUT、握手与子命令

`0x80 <command>` 得到 64 字节 `0x81 <command>`；`01` 命令额外提供合成设备类型/地址，其余基本回显并补零。这不是完整零售 USB 初始化状态机。

`0x01` 至少 11 字节，`[1]` 为宿主计数、`[2..9]` 震动、`[10]` 子命令、`[11..]` 参数。回复 `0x21` 为 64 字节：`[1..11]` 当前输入头、`[13]` ACK、`[14]` 子命令、`[15..]` 数据。

| 子命令 | ACK/条件 | 当前实际效果 |
| --- | --- | --- |
| `02` | `82` | 合成设备信息（含固定版本、类型与地址） |
| `03` | 参数为 `30` 才回 `80`，其他 `00` | 仅接受完整输入模式；没有实现其他模式切换 |
| `40` | 有参数回 `80` | 参数非零启用 IMU，零关闭 |
| `48` | 有参数回 `80` | 参数零在反馈转换层触发停振；没有完整的长期“禁用震动”状态机 |
| `30` / `31` | `80` / `B0` | 保存/读取玩家灯掩码；不代表实体输入手柄 LED 已改变 |
| `10` | 请求至少 16 字节、读取长度 ≤29 回 `90`，否则 `00` | SPI 合成读取，回复从 15 回显地址/长度，从 20 填数据 |
| `04/08/38/41` | `80` | 接受但没有相应完整设备功能状态；IMU 灵敏度并未因 `41` 改变 |
| 其他 | `00` | 未支持，不回成功 ACK |

待回复队列最多 64 条，满时丢弃最旧回复；初始化应答优先于源帧等待。GET_REPORT 与中断 IN 共用读取逻辑，返回长度不超过主机请求。

## SPI 区与震动

SPI 默认填 `FF`，已合成的区域包括：`0x6020` IMU 校准、`0x603D` 摇杆校准、`0x6050` 颜色/类型、`0x6080` 与 `0x6098` 死区/比例块。它们匹配虚拟报告范围，**不是实体 NS2 的存储镜像**。未实现写入或完整 flash 空间。

[Pro2OutputPacketMapper](../../ControllerForwardingTool/VirtualDevice/Source/Pro2OutputPacketMapper.cs) 处理 `0x10`（至少 10 字节）和 `0x01` 中的 8 字节震动，两侧各 4 字节。提取对数幅度并转换为普通高低频强度，保留左右区别；原始频率/波形不完整保真。转换后的普通状态持续到替换/停止，输出停止时清零，详见 [NS2 震动写回](NS2_PRO.md)。

## 覆盖边界

当前未实现完整 `0x3F/0x31` 输入模式、NFC/IR/amiibo、真实电量同步、全部灯光控制、完整 SPI、主机认证及零售设备所有 USB 错误行为。NS1 输入/IMU/震动的合成验证见 [ControllerProtocolChecks](../../tools/ControllerProtocolChecks/Program.cs)，通过不等于所有游戏和主机均兼容。原 Eden 与设备端研究保留在[参考资料](reference/03_NS1_PRO_PROTOCOL_REFERENCE.md)。
