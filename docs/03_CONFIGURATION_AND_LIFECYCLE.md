# 配置与桌面生命周期

适用版本：**1.0.1**；核对日期：2026-09-27。实现入口：[BridgeOptions](../ControllerForwardingTool/VirtualDevice/BridgeOptions.cs)、[OutputRouteOptions](../ControllerForwardingTool/VirtualDevice/OutputRouteOptions.cs)、[AppDataPaths](../ControllerForwardingTool/Core/AppDataPaths.cs)。

## 1. 文件与迁移

所有可写运行数据位于 Windows 当前用户的 `%LOCALAPPDATA%\ControllerForwardingTool`，不依赖程序安装位置。

| 路径 | 内容 |
| --- | --- |
| `bridge-settings.json` | 当前模式、五条线路及键鼠 / 摇杆映射、设备历史/名称/注册证据、USB 序列号与蓝牙地址、BLE 摇杆校准、体感算法参数、软件设置 |
| `window-placement.json` | 窗口位置、普通尺寸及窗口状态 |
| `logs/app-*.log` | 自动运行日志；约 5 MB 轮转，最多保留 10 个 |
| `logs/diagnostics-*.txt` | 手动诊断导出，不随运行日志轮转删除 |
| `runtime/` | 应用与后端/客户端工作目录，容纳依赖的相对路径写入 |

`BridgeOptions.Load` 在新配置不存在时读取 `%LOCALAPPDATA%\NS2ProWin11\bridge-settings.json`。有效旧配置写入临时文件后移动到新位置，不覆盖已有新文件，旧文件保留。迁移写入失败时本次仍使用已读到的旧值；损坏 JSON 或无法读取时回退默认值。已有新配置即使损坏，也不会自动改用旧配置覆盖。

保存时先归一化，写同目录随机临时文件，再替换目标。旧 `diagnostics/` 目录及旧目录日志不自动迁移。手动编辑前应从托盘退出应用，避免下次保存覆盖编辑结果。设置页的「打开目录」进入用户数据目录，配置和日志可在其中查看；升级和卸载保留用户数据。

## 2. 五条线路与保存范围

`OutputRoutes` 以 `VirtualControllerMode` 为键，包含 `Ns2Pro`、`Ns1Pro`、`DualSense`、`Xbox360`、`DualSenseEdge`。JSON 使用当前 `System.Text.Json` 默认规则：字典的枚举键为枚举名称，`Mode` / `InputKind` 等枚举属性为数值。

顶层输出字段继续作为运行快照和旧版迁移来源保留。旧配置没有线路字典或字典为空时，用旧参数分别初始化五条线路；后续保存某条线路不影响其他线路。若已有非空字典但缺少某条线路，选择该线路使用默认值。

首次运行或配置不可读时，`BridgeOptions.CreateDefault` 默认选择 NS2 实体输入 → NS1 Pro，采用 1.0.1 的 NS1 线路预设：A/B 互换、Plus 不输出、启用键鼠补充、纯键鼠鼠标模式为陀螺仪，体感来源为自动（实体优先）；键鼠左摇杆方向为 W/X/A/D，键位 K/L/J/I → B/A/Y/X、Q/E → L/R、1/4 → ZL/ZR、C → R3、Z/鼠标右键 → L3、方向键 → 十字键、空格/N/M/B → Plus/Minus/Home/Capture、鼠标左键 → A。GL/GR 映射到 L3/R3，其余按键和实体摇杆直通。键鼠方向仅用于纯键鼠主输入，补充模式不覆盖实体轴。默认值不包含设备地址、配对记录或设备校准。

加载线路或启动时若保存的实体输入不可用，界面通过 `SelectAvailableInput` 选择键鼠；因此配置中的 NS2 默认输入不表示必须连接实体手柄才能启动。运行中实体断开只归零，不自动改为键鼠。NS1 默认补充开关为 true，其他新建线路为 false；已有旧配置缺少该字段时也为 false，不套用新建 NS1 预设覆盖用户设置。

五条线路的振动默认均为 0.7x。NS1、Xbox 360、DualSense 默认 GL → L3、GR → R3；NS2 Pro 和 DualSense Edge 默认保留 GL/GR 背键。映射页「恢复默认并保存」载入当前目标的默认按键、键鼠和摇杆映射。已有配置和用户自定义映射继续保留，升级不会强制覆盖。

| 操作 | 保存/应用范围 |
| --- | --- |
| 选择输出卡片 | 恢复该线路保存值或本次内存草稿；不能在输出运行中切换 |
| 应用并保存此线路 | 保存当前线路参数和映射，更新运行选项；保存失败保留草稿和旧运行值 |
| 启动输出 | 先保存当前线路，再创建后端；端口实际绑定发生在启动时 |
| 映射页应用并保存/恢复默认并保存 | 提交当前线路按键、键鼠覆盖/方向、实体摇杆、鼠标模式、补充开关和体感来源，不提交震动、频率等其他草稿 |
| 映射弹窗确认/取消 | 确认写入内存草稿供本页预览；取消丢弃本次未确认内容，不改变已有草稿 |
| 切换键鼠补充开关 | 运行中立即启停补充并清空旧补充状态；保存映射后跨启动保留 |
| 保存软件设置 | 只提交自启、恢复输出与托盘行为，保留线路配置 |
| 修改主机注册开关 | 立即持久化；保存失败恢复原值，连接期间不可修改 |
| 保存陀螺仪参数 | 保存全局算法设置，不保存当前采样零偏 |

线路按输出身份划分，不再按 USB/BLE 输入拆分。Windows 具体设备的 SDL ID 不保存在配置中。`StickProfiles` 按 NS2 BLE 地址存档并在各线路共用；是否应用校准由线路决定。注册历史和体感算法参数也属于全局设置。

## 3. 线路字段

下表为归一化后的默认值及范围；界面可能用百分数等形式显示。

| 字段 | 默认值 | 语义与范围 |
| --- | --- | --- |
| `InputKind` | NS1 为 `Ns2Ble`（0），其他线路为 `KeyboardMouse`（2） | `Ns2Ble` 兼容枚举名同时涵盖 NS2 USB 优先 / BLE 接续；`WindowsGamepad`（1）为 SDL 输入；非法值回退键鼠 |
| `Ns2Buttons` | 按目标的默认映射 | 沿用旧字段名，现在应用于所有输入；NS1 预设见上文 |
| `MouseMode` | NS1 为 `Gyroscope`（1），其他为 `RightStick`（0） | 纯键鼠鼠标用途；非法值回退右摇杆，Xbox 不输出体感 |
| `GyroSource` | `Automatic`（0） | 实体优先，无体感时用鼠标；另有 `Controller`（1）、`Mouse`（2），补充关闭时保留实体体感 |
| `KeyboardMouseSupplementEnabled` | 新建 NS1 为 true，其他/旧配置缺省为 false | 允许实体输入叠加键鼠；不限制纯键鼠主输入，不等于自动开启 F8 捕获 |
| `KeyboardOverrides` | NS1 预设，其他为空 | Windows 虚拟键码 → 输出按钮；None 禁用该键，Esc/F8/Alt/Windows 键不可绑定 |
| `KeyboardStickBindings` | NS1 左轴 W/X/A/D，其他左轴 WASD | 方向 → 键码；纯键鼠方向配置，自定义按钮优先于方向绑定 |
| `ControllerSticks` | 左右摇杆各自直通 | 实体摇杆整组轴映射，源校准与死区后应用 |
| `PushHz` | 0 | 0 跟随源，-1 目标参考频率，66/125/250 固定频率；其他值归零 |
| `ApiPort` / `UsbPort` | 0 | 自动端口；有效手动值 1024–65535。界面拒绝相同非零端口；NS1 后端不使用 API 端口 |
| `RumbleGain` | 0.7 | 0–3 倍；非有限值回退 0.7 |
| `AudioGuard` | true | PS5/Edge 输出运行期间保护 Windows 默认音频端点 |
| `GyroPitch` / `GyroYaw` / `GyroRoll` | 1 | PS5/Edge 体感编码倍率，0.1–4 |
| `InvertPitch` / `InvertYaw` / `InvertRoll` | false | 对应轴反向 |
| `StickDeadzone` | 0 | 0–0.3，对应界面 0–30% |
| `RadialDeadzone` | true | 圆形径向死区；false 为轴向死区 |
| `UseStickCalibration` | true | 应用可用的设备摇杆档案，目前 BLE 路径读取 |

首次运行的全局 `Mode` 默认 `Ns1Pro`；旧配置缺省或非法模式仍按兼容规则回退 `Ns2Pro`。归一化对浮点值进行有限性检查并限幅，非法端口回到自动值。配置格式没有独立 schema 版本字段；兼容迁移由 `Normalize` 和 `Load` 实现，不建议删除顶层字段后自行重建文件。

## 4. 蓝牙记录与体感参数

`BleDevices` 包含 `Remembered`、`Forgotten`、`Aliases`。地址和地址类型共同标识设备；别名最长 40 字符。成功连接更新记录顺序；旧 `LastBleDevice` 迁入历史后清空。删除记录清除名称和注册证据，保留遗忘地址以阻止普通回连；新鲜 SYNC 允许重新接入。界面的删除流程还会断开匹配的当前连接并尝试清理 Windows 配对，失败时保留本地删除结果并提示系统配对未确认清理。

`RegisterHostOnSync` 默认 false，启用仅控制之后满足条件的注册，不代表已完成绑定。注册证据保存主机地址和确认时间，不保存 LTK 或挑战材料。自动连接开关不写入 `BridgeOptions`，每次启动默认开启。

`Ns2UsbAddresses` 保存 USB 工厂序列号 → 经校验交换取得的手柄蓝牙地址，用于拔线后匹配同一设备；非法地址和空序列号在归一化时丢弃。旧配置缺失时为空，不表示设备未注册。USB 当前注册状态以读取结果为准，详见 [USB 注册说明](29_NS2_USB_REGISTRATION.md)。

全局 `Motion` 字段来自 [GyroOptions](../ControllerForwardingTool/Input/GyroOptions.cs)：

| 字段 | 默认 | 有效范围/作用 |
| --- | --- | --- |
| `AutoCalibrate` | true | 自动静置零偏校准 |
| `SmoothSmallMotion` | false | 细微动作平滑 |
| `AngularThreshold` | 首次运行 1.6 °/s；旧配置缺省/非法值 1.5 °/s | 0.2–5 |
| `AccelerationThreshold` | 首次运行 0.05 g；旧配置缺省/非法值 0.035 g | 0.01–0.15，重力向量变化门限 |
| `StationarySeconds` | 3 s | 2–10，自动静置时长 |

参数编辑立即作用于传感器处理，点击保存后才跨启动保留。实际校准器与零偏在内存中；Windows 校准器按当前 SDL ID 管理，设备移除后清理。

## 5. 自启、托盘与重复启动

软件默认值：`LaunchAtLogin`、`AutoStartOutput`、`StartInTray`、`MinimizeToTray` 均为 false，`CloseToTray` 为 true。软件设置保存后窗口行为立即生效，启动行为在下次启动时生效。

`LaunchAtLogin` 在界面初始化时以实际 Startup 入口为准。应用保存当前用户 Startup 快捷方式，并清理该用户旧 Run 项；安装器管理所有用户 Startup 入口，普通用户不能通过设置页取消所有用户入口。安装器首次默认勾选自启，与便携版初始配置不同；详见[安装包说明](../ControllerForwardingTool/InnoSetup/README.md)。

`AutoStartOutput` 在启动初始化后、满足驱动等启动条件时尝试恢复保存模式。Windows 输入设备 ID 不持久化；加载线路或启动时没有可用实体输入会选择键鼠。键鼠输出启动后仍需 F8 开始控制；运行中实体断开不自动接管为键鼠。

`Program` 在初始化 Avalonia 之前启动 `SingleInstanceService`。同一程序目录使用命名管道唤醒已有窗口，收到确认的新进程退出；不同程序目录可以分别启动。通信失败或超时允许正常启动，不能当作严格的全系统进程互斥。不同目录副本仍使用同一当前用户配置目录。

托盘支持打开窗口、日志、设置和退出。启动到托盘时收到的激活请求优先显示窗口；窗口位置由 `WindowPlacementStore` 恢复。真正退出时停止输出、解除本会话 USB/IP 挂载、清理 BLE/SDL 资源与自身后端，不卸载系统驱动，也不删除用户配置。
