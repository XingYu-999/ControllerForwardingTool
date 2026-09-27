# Xbox 手柄通信协议：XInput、USB 与蓝牙

> 历史研究归档（1.0.1 文档同步；原研究日期保留）：硬件报文资料保留原核查日期，当前应用接入与内部消息见 [Xbox 当前协议](../XBOX.md)。当前 NS1 已支持体感；本目录不声称包含全部 Xbox 固件、无线适配器或认证协议。

核查日期：2026-09-26。微软公开的 XInput 是**应用接口**；Xbox 360 USB、Xbox One / Series GIP、蓝牙 HID 是不同的传输格式，不能把 `XINPUT_GAMEPAD` 内存直接当作 USB 报文。本资料的报文部分来自开源驱动实现，不是微软完整硬件协议或主机认证规范。

## 来源与复核点

- 微软 [XINPUT_GAMEPAD](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad)：标准按键掩码、独立扳机、摇杆范围。
- Linux [xpad.c](https://github.com/torvalds/linux/blob/master/drivers/input/joystick/xpad.c)：`xpad360_process_packet`、`xpadone_process_packet`、GIP 初始化和震动包。
- SDL **release-3.4.16** [SDL_hidapi_xboxone.c](https://github.com/libsdl-org/SDL/blob/release-3.4.16/src/joystick/hidapi/SDL_hidapi_xboxone.c)：USB GIP、不同蓝牙固件的输入、Guide、电量与震动。
- SDL 同版本 [SDL_hidapi_xbox360.c](https://github.com/libsdl-org/SDL/blob/release-3.4.16/src/joystick/hidapi/SDL_hidapi_xbox360.c)：Xbox 360 HIDAPI 适配。

项目内可复核 SDL 源码：`tools/sdl-build/SDL3-3.4.16/src/joystick/hidapi/`。核查的 Linux `xpad.c` SHA-256：`19DD00FC4B0341EF18575A87B9B3FBD5F809E4588FF2909E2C357AF2F2358517`。Linux 链接指向可变分支，后续应对照实际版本重新确认。

## 1. Windows 应用层：XInput

`XINPUT_GAMEPAD` 字段如下；只有字段语义，不含 USB Report ID、传输长度、GIP 头或蓝牙封装。

| 字段 | 类型 / 范围 | 意义 |
| --- | --- | --- |
| wButtons | WORD | 十字键、Start / Back、L3 / R3、LB / RB、A / B / X / Y 掩码 |
| bLeftTrigger / bRightTrigger | BYTE，0…255 | 两个独立模拟扳机，0 松开 |
| sThumbLX / sThumbRX | SHORT，−32768…32767 | 水平摇杆，正数向右 |
| sThumbLY / sThumbRY | SHORT，−32768…32767 | 垂直摇杆，正数向上 |

标准按键掩码：十字键上/下/左/右为 `0001/0002/0004/0008`，Start/Back 为 `0010/0020`，L3/R3 为 `0040/0080`，LB/RB 为 `0100/0200`，A/B/X/Y 为 `1000/2000/4000/8000`（均十六进制）。文档未定义的保留位不应当作可移植 Guide 接口。

LT / RT 不是 `wButtons` 的两个开关。检测“按下”可在应用层设阈值，但原始行程必须保留。XInput 的这个结构不提供陀螺仪、触摸位置、独立 Elite 背键或扳机阻力；不要根据设备名称捏造这些数据。

## 2. Xbox 360 有线 USB 输入

依据 `xpad360_process_packet` 的常见 20 字节输入。偏移以收到的整个 USB 数据包为起点，所有多字节数按小端读取。

| 偏移 | 字节数 | 内容 |
| --- | --- | --- |
| 0 | 1 | 包类型，标准输入为 `00` |
| 1 | 1 | 常见输入长度字段为 `14`（20 字节）；必须检查实际收到长度 |
| 2 | 1 | bit0…3 上/下/左/右；bit4 Start、5 Back、6 L3、7 R3 |
| 3 | 1 | bit0 LB、1 RB、2 Guide；bit4…7 A/B/X/Y |
| 4 / 5 | 1 + 1 | LT / RT，0…255 |
| 6 / 8 / 10 / 12 | 2 × 4 | LX / LY / RX / RY，有符号 16 bit；原始 Y 正数向上 |
| 14…19 | 6 | 本项目不依赖的保留/扩展数据 |

`xpad` 为 Linux 坐标约定对 Y 做位反转；SDL 再以自己的标准轴 API 暴露值。解析代码必须明确自己处在原始报文层还是标准轴层，不能照抄一次反向后又反向一次。

Xbox 360 无线接收器使用额外封装和设备槽位状态；`xpad360w_process_packet` 在确认有效输入后跳过 4 字节封装再调用 360 解析器。它不是 Xbox One 蓝牙协议。

## 3. Xbox One / Series USB：GIP

常见 GIP 小包的 4 字节头可理解为命令、选项、序号、负载长度。ACK、内部命令、长报文与分片有额外规则，不能把所有包都当作固定输入结构。

`xpadone_process_packet` 对主输入命令 `0x20` 的公共字段处理如下：

| 全包偏移 | 内容 |
| --- | --- |
| 0…3 | GIP 包头；检查命令及实际长度后才能访问后续字段 |
| 4 | bit2 Menu、bit3 View、bit4…7 A/B/X/Y |
| 5 | bit0…3 上/下/左/右，bit4 LB、5 RB、6 L3、7 R3 |
| 6…7 / 8…9 | LT / RT，无符号小端；常见原生范围 0…1023 |
| 10…11 / 12…13 | LX / LY，有符号小端 16 bit |
| 14…15 / 16…17 | RX / RY，有符号小端 16 bit |

Guide 可通过独立 `0x07` 包上报，部分包需要回 ACK。震动命令为 `0x09`；初始化、开启输入、序号管理和设备专属初始化序列同样重要，单独解码 `0x20` 不构成完整驱动。

Series Share、Elite 背键和配置档位会随固件/包型改变，`xpad` 和 SDL 都有长度、型号或映射条件分支。不要将某一个偏移推广到全部 Xbox 手柄。Xbox 无线适配器协议也不能与蓝牙 HID 混用。

## 4. Xbox One S / Series 蓝牙 HID

SDL 的 `HIDAPI_DriverXboxOneBluetooth_HandleStatePacket` 区分旧 16 字节包和更长的新包。以下是这两类解析路径共享的轴字段，偏移含 Report ID：

| 偏移 | 内容 |
| --- | --- |
| 0 | 输入 Report ID `01` |
| 1…2 / 3…4 | LX / LY，无符号小端 16 bit，以 `8000` 为中心 |
| 5…6 / 7…8 | RX / RY，同上 |
| 9…10 / 11…12 | LT / RT，小端，常见范围 0…1023 |
| 13 | Hat：1 上、2 右上、3 右、4 右下、5 下、6 左下、7 左、8 左上，其余居中 |
| 14 起 | 按键区；必须按固件/长度选择解析路径 |

蓝牙摇杆进入 SDL 时无需再按 USB/XInput 的 Y 规则反转。旧固件 Guide 可使用独立 Report `02`；SDL 还处理 Report `04` 电量。长包中的 Share、Guide、背键位置不是上述旧包的简单追加。输出震动也有蓝牙专用格式，不能直接发送 USB GIP `09` 包。

## 5. 本项目如何使用

本项目通过 SDL 的 XInput / HIDAPI 后端读取 Windows 设备，**不新增一套原生 Xbox USB/蓝牙解析器**。SDL 负责设备初始化和归一化，本项目读取标准面键、6 轴及可用传感器，再转为内部状态。

- SDL 摇杆 Y 为向下正，进入内部 Nintendo 坐标时取反；Xbox 输出编码再使用向上正。
- 扳机在内部单独保存 0…255 行程，Xbox / PS5 输出保留模拟量，Nintendo 输出采用大于 50% 的数字按下规则。
- Xbox 输入缺少体感时，NS2 / PS5 / NS1 的体感字段为零；当前 NS1 已支持转发其他输入源提供的有效体感。
- 本项目的 `VirtualReportEncoder` 发送给 VIIPER 的 Xbox 数据是 **20 字节内部状态消息**，字段位置与上面的真实 Xbox 360 USB 包不同。VIIPER 再生成 Windows 接收的设备报文，二者不能混用。
- 身份模拟仅用于本机 Windows；不包含 Xbox 主机认证、密钥提取或固件刷写。

当前映射与验证见 [Xbox 当前协议](../XBOX.md)和[核对结果](../COVERAGE.md)；历史多类型输入记录见[原文](../../../ControllerForwardingTool/docs/12_MULTI_CONTROLLER_INPUT.md)。
