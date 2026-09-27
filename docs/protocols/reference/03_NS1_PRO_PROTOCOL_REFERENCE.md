# NS1 Pro 通信协议参考：实体手柄、Eden 输入实现与虚拟设备边界

> 历史研究归档：本文保留 Eden 与实验设备端的早期观察。当前本项目已实现虚拟 NS1 输入、IMU 与震动转码，见 [NS1 当前协议](../NS1_PRO.md)。下文的拟议目标、绝对路径、行号及实测记录均属于历史背景。

> **非 Nintendo 官方协议手册。** 截至 2026-09-25，本项目未取得 Nintendo 面向公众发布的完整 NS1 Pro USB／蓝牙 HID 字节级规范。本文的报文细节是 Eden 与另一参考工程的源码观察，不能标为官方规定。用户已用**实体 NS1 Pro 手柄连接 Eden 并实际使用**；该实测确认 Eden 能使用这只手柄，但不单独判定 Eden 此次选择了下述哪一条输入路径。Nintendo [有线通信帮助](https://en-americas-support.nintendo.com/app/answers/detail/a_id/26315/~/how-to-enable%2Fdisable-pro-controller-wired-communication)只说明使用方式，不提供完整报文定义。

本文用于 `NS2ProWin11` 项目的协议研究。路径均指本机克隆仓库 `D:\github\eden`；行号以本次查阅的源码为准。Eden 的 README.md（历史本机路径：`D:/github/eden/README.md`）说明它是 Switch 1 模拟器，采用 GPL-3.0-or-later。研究行为与复制源码是两回事；独立实现时须单独处理许可证。

## 1. 先分清两个方向

```text
实体 NS1 Pro ── USB／系统蓝牙 HID ──> Eden 的 SDL3 或自有 HID 输入驱动
                                        └─> 按键、摇杆、体感等抽象输入
                                             └─> EmulatedController
                                                  └─> Npad Fullkey ──> 被模拟的 Switch 游戏

NS2ProWin11 的拟议目标：实体 NS2 Pro ── BLE ──> 状态映射 ──> Windows 虚拟 NS1 Pro
```

Eden 完成的是**主机程序读取实体 NS1 Pro**，再把输入提交给模拟器内部的虚拟 Switch 控制器。其设置页的“Pro 控制器”表示被模拟主机侧的 `NpadStyleIndex::Fullkey` 类型；“输入设备”可绑定实体手柄或其他设备。用户的实机结果证实本机接入成功，但截图不能揭示当前使用 SDL 路径还是 Eden 自有 HID 路径。Eden 的内部 Npad 对象**不是 Windows 可枚举的虚拟 USB/HID 外设**，所以不能将 Eden 的输入驱动直接当作 `NS2ProWin11` 的虚拟设备输出驱动。

源码依据：`src/hid_core/frontend/emulated_controller.cpp` 第 32–35、61–64 行将 `ProController` 映射到 `Fullkey`；`src/hid_core/resources/npad/npad.cpp` 第 545–562 行把 Fullkey 状态写进模拟器的 `fullkey_lifo`。

## 2. Eden 如何接入实体 NS1 Pro

Eden 具有两条互斥倾向的输入路径，选哪条由配置决定：

| 输入路径 | 源码行为 | 与本项目的关系 |
| --- | --- | --- |
| SDL3 手柄路径 | `enable_procon_driver` 默认 `false`；`SDLDriver` 启用 `SDL_HINT_JOYSTICK_HIDAPI_SWITCH`，通过 `SDL_OpenJoystick`／`SDL_OpenGamepad` 取得标准化输入 | 可作为“应用层读实体 NS1”的架构参考。SDL 替应用处理部分协议，不能从 Eden 源码反推出 SDL 内部所有报文操作 |
| Eden 自有 Pro HID 路径 | 开启 `enable_procon_driver` 时，用 `SDL_hid_enumerate(0x057E, 0)` 扫描；仅接受 `057E:2009` 为 Pro，按序列号去重，`SDL_hid_open` 打开并读取原始报文；同时关闭 SDL 的 Switch HIDAPI 手柄路径，避免重复设备 | 能看到 Eden 对 NS1 的初始化、子命令、报文解析和震动处理；它仍是**输入端** |

源码依据：`src/common/settings.h` 第 804 行、`src/input_common/drivers/sdl_driver.cpp` 第 485–518、667–685 行、`src/input_common/drivers/joycon.cpp` 第 20–30、82–100、103–178 行，以及 `src/input_common/helpers/joycon_driver.cpp` 第 696–716 行。`057E:2009` 是这里的设备识别条件，不表示只填 VID/PID 就能制造一个可用的虚拟 NS1 Pro。

自有 HID 路径的主要调用链如下：

1. 每约 5 秒枚举 Nintendo VID，筛选设备类型和序列号，打开 HID 句柄；见 `drivers/joycon.cpp` 第 82–100、169–212 行与 `helpers/joycon_driver.cpp` 第 35–53 行。
2. 建立协议对象，查询设备信息与颜色，读取摇杆和 IMU 校准，设置玩家灯，再启用震动、IMU 与完整输入模式；见 `helpers/joycon_driver.cpp` 第 56–136、273–358 行。
3. 输入线程以 3 毫秒读超时轮询 `SDL_hid_read_timeout`，按 Report ID 分派 `0x30`、`0x31`、`0x3F`；3 毫秒是轮询超时，**不是手柄 333 Hz 采样率**；见同文件第 139–185、188–270 行。
4. `JoyconPoller` 解码按键、摇杆和 IMU，经回调进入 `InputEngine::SetButton/SetAxis/SetMotion`；再按玩家配置绑定到 `EmulatedController` 和 Npad Fullkey；见 `helpers/joycon_protocol/poller.cpp` 第 169–213 行、`drivers/joycon.cpp` 第 463–485、636–751 行。

## 3. Eden 自有 HID 路径观察到的 Report ID

| 方向 | ID | Eden 中的用途与证据 |
| --- | --- | --- |
| 主机 → 手柄 | `0x01` | 带 8 字节震动字段的子命令输出；`joycon_types.h` 第 740–750 行 |
| 主机 → 手柄 | `0x10` | 单独发送震动；`joycon_types.h` 第 733–738 行 |
| 手柄 → 主机 | `0x21` | 子命令回复；`joycon_driver.cpp` 第 264–266 行，初始化时由 `common_protocol.cpp` 第 78–97 行同步读取 |
| 手柄 → 主机 | `0x30` | 标准完整输入；按键、摇杆、IMU；`joycon_types.h` 第 203–214、526–538 行 |
| 手柄 → 主机 | `0x31` | NFC/IR 模式输入；Eden 沿用与 `0x30` 相同的基本输入解码；`poller.cpp` 第 67–69 行 |
| 手柄 → 主机 | `0x3F` | 简单 HID 输入，方向摇杆为离散状态；`joycon_types.h` 第 518–524 行、`poller.cpp` 第 256–278 行 |
| 主机 → 手柄／反向 | `0x80`／`0x81` | Eden 的类型枚举有定义，但在本次查阅的 `src/input_common` 实际调用链中未找到发送 `0x80` 的代码；不能据此声称 Eden 初始化时必做 USB 专有握手 |

`0x80/0x81` USB 握手、USB 描述符和虚拟设备应答主要来自另一个参考工程 `D:\github\XinHeLianSheng-Pro2-Bridge\source\final-three-in-one\experiments\esp-switch1-r4\firmware\esp32s3_switch2_bridge\main\usb\` 的**USB 设备端实验实现**。它与 Eden 的实体手柄输入端属于协议两侧；详见第 8 节。

## 4. 子命令输出与初始化时序

Eden 的 `SubCommandPacket` 大小为 `0x31`（49）字节，字段按其 C++ 结构体排列：

| 字节偏移 | Eden 解释 |
| --- | --- |
| `0` | Report ID `0x01` |
| `1` | 递增并按 `0x0F` 回卷的计数器 |
| `2..9` | 8 字节震动字段／占位 |
| `10` | 子命令 ID |
| `11..48` | 最多 `0x26` 字节命令参数及填充 |

`common_protocol.cpp` 第 15–17、63–65、100–128 行按上述结构发送命令，等待 `0x21` 回复。其回复结构在偏移 `14` 记录子命令 ID，偏移 `13` 落在 14 字节公共头末尾；Eden 的这段代码**未显式校验 ACK 位**，不应把“收到回复”写成“ACK 确认成功”。此外，回复等待条件使用 `&&`，存在其中一个条件匹配便退出的可能；独立实现应分别严格核对 Report ID、子命令 ID、长度和 ACK。

源码能确认的初始化操作大致为：

| 阶段 | 子命令／访问 | 源码位置 |
| --- | --- | --- |
| 查询固件与设备信息 | `0x02` | `generic_functions.cpp` 第 37–49、112–118 行 |
| 关闭低功耗模式 | `0x08`，参数 `0` | 同文件第 26–30 行 |
| 读取序列号、颜色、设备类型和校准 | `0x10` SPI 读，工厂区与用户区按有效标记选择 | `common_protocol.cpp` 第 165–190 行；`calibration.cpp` 第 18–133 行 |
| 设置玩家灯 | `0x30` | `generic_functions.cpp` 第 131–139 行 |
| 允许震动 | `0x48` | `rumble.cpp` 第 19–24 行 |
| 启用 IMU／设置灵敏度 | `0x40`／`0x41` | `generic_functions.cpp` 第 55–69 行 |
| 切换到完整报告 | `0x03`，参数 `0x30`；之后还调用 `0x04` | `generic_functions.cpp` 第 21–24、32–35 行；`joycon_driver.cpp` 第 350–357 行 |

Eden 还枚举了 `0x31` 玩家灯读取、`0x38` Home 灯、NFC/IR/MCU 等命令，但“枚举中存在”不等于此连接流程每次都会发送。第三方手柄可能不接受配置命令；Eden 在设备信息查询失败时标记 `input_only_device`，说明它对兼容性留有退路（`joycon_driver.cpp` 第 93–111、289–291 行）。

## 5. 输入报告布局与解码

以下偏移从 HID **完整报告的 Report ID 字节**开始，描述 Eden 自有 HID 路径的读法，不保证所有连接方式、固件和第三方设备字节完全一致。

### `0x30` 完整输入

| 偏移 | Eden 读取方式 |
| --- | --- |
| `0` | Report ID `0x30` |
| `1` | 报告计数／计时字节 |
| `2` | 电池状态；Eden 将高位拆成充电标记和电量状态 |
| `3` | 右侧按钮组：Y、X、B、A、R、ZR 等 |
| `4` | 共享按钮组：Minus、Plus、摇杆按下、Home、Capture 等 |
| `5` | 左侧按钮组：方向键、L、ZL 等 |
| `6..8` | 左摇杆 X/Y，每轴 12 位、合计 3 字节 |
| `9..11` | 右摇杆 X/Y，同上 |
| `12` | 震动／附加状态字节 |
| `13` 起 | IMU 原始数据。另一参考实现将 `13..48` 解释为三组各 12 字节样本；**Eden 当前 `InputReportActive` 只复制 0x29（41）字节，其结构体为 24 字节运动数组加 4 字节扩展，而实际 `GetMotionInput` 仅取首组 6 个有符号 16 位值**。因此不能称 Eden 验证了三组全部被处理 |

Eden 对 Pro 的按钮合成表达式为 `button[2] | button[0] << 8 | button[1] << 16`，位定义见 `joycon_types.h` 第 61–84 行，解析见 `poller.cpp` 第 169–185 行。摇杆 12 位解包见 `poller.cpp` 第 188–204 行；实际输出要再用中心、正负行程校准，校准区读取见 `calibration.cpp` 第 18–88 行。IMU 有轴重排和符号翻转，见 `poller.cpp` 第 350–374、206–212 行；这些是 Eden 的坐标转换策略，不是原始协议字段含义。

### `0x3F` 简单输入

Eden 的 `InputReportPassive` 是 14 字节：`0` 为 `0x3F`，`1..2` 为按钮位，`3` 为摇杆状态字节，`4..13` 在该结构中标为未知。对 Pro，摇杆状态字节的低／高 4 位分别代表左／右摇杆的九种离散方向或中立，无法提供 `0x30` 的 12 位模拟精度；见 `joycon_types.h` 第 86–113、518–524 行与 `poller.cpp` 第 256–278 行。因此目标若需要精确摇杆和 IMU，应完成 `0x30` 模式初始化。

## 6. 震动和模拟器内部映射

`0x10` 输出在 Eden 中为 10 字节：Report ID、计数器、8 字节震动数据。启用震动先发子命令 `0x48`；非零震动由频率／振幅编码成 8 字节，零幅度发停止用的默认缓冲区。见 `rumble.cpp` 第 19–55 行及 `joycon_types.h` 第 733–738 行。Eden 为避免积压在输入线程里消费震动队列，超过 6 项时丢弃旧项，见 `joycon_driver.cpp` 第 169–178 行。NS2 Pro 的 BLE 震动编码不同，不能直接转发这 8 字节。

输入端解析完成后，Eden 用 `SetButton`、`SetAxis`、`SetMotion` 交给输入引擎，再把实体按钮映射到 Switch 游戏的 A/B/X/Y、方向、肩键等逻辑输入。`ProController → Npad Fullkey → fullkey_lifo` 是**模拟器内部状态写入**，没有创建 Windows 虚拟手柄；见 `drivers/joycon.cpp` 第 463–485、636–751 行、`hid_core/frontend/emulated_controller.cpp` 第 32–35 行、`hid_core/resources/npad/npad.cpp` 第 545–562 行。NS2 的 C、GL、GR 在 NS1 Pro 中无直接同名键，需要产品层自定义映射。

## 7. 对 `NS2ProWin11` 的实施启示

1. 若只需让 Eden 游戏接受 NS2 输入，可研究它的 SDL／输入设备绑定机制；**不要把 Eden 的内部 Npad 当成系统虚拟设备**。若目标还包括 Steam 或普通 Windows 游戏，则需要独立的系统输出路径。
2. 若 Windows 需要枚举出虚拟 NS1 Pro，应实现 USB/HID 设备端：描述符、VID/PID 兼容识别、主机握手、`0x3F`／`0x30` 输入、`0x21` 子命令应答、校准区、震动与灯光。Eden 的实体输入端源码不能证明这些设备端行为已经完成。
3. 在纯 C# 层可完成字节解析、摇杆与 IMU 校准、输入状态转换以及报文编码；能否把这些报文作为 Windows 虚拟 USB/HID 设备呈现，取决于另选的驱动／USB-IP／硬件输出方案，而非 C# 与 C++ 的逐帧计算速度。
4. 使用实机记录“USB 或蓝牙连接方式、固件、实际 Report ID 和长度、子命令请求与回复、游戏侧效果”。不能拿 Eden 的 `0x30` 读取成功推断虚拟设备会被 Steam、Windows 或实体 Switch 自动接受。

## 8. 证据边界与待验证事项

- **已确认：**用户的实体 NS1 Pro 能在 Eden 中使用；Eden 源码有 SDL3 路径和可选的自有 `057E:2009` HID 路径；自有路径读取、解析和映射上文所列报文。
- **当前未知：**用户这次实测在 Eden 中具体走 SDL3 还是自有驱动，以及实体手柄是 USB 还是系统蓝牙 HID。设置页截图与源码本身无法给出这一点；可通过 `enable_procon_driver` 设置及 Eden 日志中的 `Preferring joycon driver`、设备枚举记录判定。
- **未由 Eden 证明：**Windows 虚拟 NS1 USB 枚举、`0x80/0x81` USB 初始化完整时序、所有三组 IMU 样本处理、NFC/amiibo 完整兼容、实体 Switch 主机认证。旧版文档把 USB 握手写成 Eden 输入路径的必经步骤，这一点已经修正。
- **设备端补充来源：**`D:\github\XinHeLianSheng-Pro2-Bridge\source\final-three-in-one\experiments\esp-switch1-r4\firmware\esp32s3_switch2_bridge\main\usb\hid_report.h`、`usb_descriptors.h`、`usb_hid_device.c` 是另一个项目的 USB 模拟实验，实现边界需单独实机验证。
- **许可证：**Eden README 标注 GPL-3.0-or-later，其相关源文件还保留 yuzu 的 GPL-2.0-or-later 声明。本文只归纳行为和事实，没有复制 Eden 的实现代码；若未来移植源码，应先审查相应文件及完整依赖的许可证要求。
