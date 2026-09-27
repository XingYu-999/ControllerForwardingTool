# 配置与桌面生命周期

核对日期：2026-09-27。实现入口：[BridgeOptions](../ControllerForwardingTool/VirtualDevice/BridgeOptions.cs)、[OutputRouteOptions](../ControllerForwardingTool/VirtualDevice/OutputRouteOptions.cs)、[AppDataPaths](../ControllerForwardingTool/Core/AppDataPaths.cs)。

## 1. 文件与迁移

所有可写运行数据位于 Windows 当前用户的 `%LOCALAPPDATA%\ControllerForwardingTool`，不依赖程序安装位置。

| 路径 | 内容 |
| --- | --- |
| `bridge-settings.json` | 当前模式、五条线路、设备历史/名称/注册证据、BLE 摇杆校准、体感算法参数、软件设置 |
| `window-placement.json` | 窗口位置、普通尺寸及窗口状态 |
| `logs/app-*.log` | 自动运行日志；约 5 MB 轮转，最多保留 10 个 |
| `logs/diagnostics-*.txt` | 手动诊断导出，不随运行日志轮转删除 |
| `runtime/` | 应用与后端/客户端工作目录，容纳依赖的相对路径写入 |

`BridgeOptions.Load` 在新配置不存在时读取 `%LOCALAPPDATA%\NS2ProWin11\bridge-settings.json`。有效旧配置写入临时文件后移动到新位置，不覆盖已有新文件，旧文件保留。迁移写入失败时本次仍使用已读到的旧值；损坏 JSON 或无法读取时回退默认值。已有新配置即使损坏，也不会自动改用旧配置覆盖。

保存时先归一化，写同目录随机临时文件，再替换目标。旧 `diagnostics/` 目录及旧目录日志不自动迁移。手动编辑前应退出应用，避免下次保存覆盖编辑结果。配置入口在文件不存在时先保存；已存在时直接打开，关联程序不可用时尝试记事本。

## 2. 五条线路与保存范围

`OutputRoutes` 以 `VirtualControllerMode` 为键，包含 `Ns2Pro`、`Ns1Pro`、`DualSense`、`Xbox360`、`DualSenseEdge`。JSON 使用当前 `System.Text.Json` 默认规则：字典的枚举键为枚举名称，`Mode` / `InputKind` 等枚举属性为数值。

顶层输出字段继续作为运行快照和旧版迁移来源保留。旧配置没有线路字典或字典为空时，用旧参数分别初始化五条线路；后续保存某条线路不影响其他线路。若已有非空字典但缺少某条线路，选择该线路使用默认值。

| 操作 | 保存/应用范围 |
| --- | --- |
| 选择输出卡片 | 恢复该线路保存值或本次内存草稿；不能在输出运行中切换 |
| 应用并保存此线路 | 保存当前线路参数和映射，更新运行选项；保存失败保留草稿和旧运行值 |
| 启动输出 | 先保存当前线路，再创建后端；端口实际绑定发生在启动时 |
| 保存映射/恢复默认映射 | 只提交当前线路映射，不提交震动、频率等其他草稿 |
| 保存软件设置 | 只提交自启、恢复输出与托盘行为，保留线路配置 |
| 修改主机注册开关 | 立即持久化；保存失败恢复原值，连接期间不可修改 |
| 保存陀螺仪参数 | 保存全局算法设置，不保存当前采样零偏 |

线路按输出身份划分，不再按 USB/BLE 输入拆分。Windows 具体设备的 SDL ID 不保存在配置中。`StickProfiles` 按 NS2 BLE 地址存档并在各线路共用；是否应用校准由线路决定。注册历史和体感算法参数也属于全局设置。

## 3. 线路字段

下表为归一化后的默认值及范围；界面可能用百分数等形式显示。

| 字段 | 默认值 | 语义与范围 |
| --- | --- | --- |
| `InputKind` | `Ns2Ble`（0） | `WindowsGamepad`（1）为 SDL 输入；非法值回退 BLE |
| `Ns2Buttons` | 默认映射 | NS2 源按键映射，按目标支持能力呈现 |
| `PushHz` | 0 | 0 跟随源，-1 目标参考频率，66/125/250 固定频率；其他值归零 |
| `ApiPort` / `UsbPort` | 0 | 自动端口；有效手动值 1024–65535。界面拒绝相同非零端口；NS1 后端不使用 API 端口 |
| `RumbleGain` | 1 | 0–3 倍 |
| `AudioGuard` | true | PS5/Edge 输出运行期间保护 Windows 默认音频端点 |
| `GyroPitch` / `GyroYaw` / `GyroRoll` | 1 | PS5/Edge 体感编码倍率，0.1–4 |
| `InvertPitch` / `InvertYaw` / `InvertRoll` | false | 对应轴反向 |
| `StickDeadzone` | 0 | 0–0.3，对应界面 0–30% |
| `RadialDeadzone` | true | 圆形径向死区；false 为轴向死区 |
| `UseStickCalibration` | true | 应用可用的设备摇杆档案，目前 BLE 路径读取 |

全局 `Mode` 默认 `Ns2Pro`。归一化对浮点值进行有限性检查并限幅，非法端口回到自动值。配置格式没有独立 schema 版本字段；兼容迁移由 `Normalize` 和 `Load` 实现，不建议删除顶层字段后自行重建文件。

## 4. 蓝牙记录与体感参数

`BleDevices` 包含 `Remembered`、`Forgotten`、`Aliases`。地址和地址类型共同标识设备；别名最长 40 字符。成功连接更新记录顺序；旧 `LastBleDevice` 迁入历史后清空。删除记录清除名称和注册证据，保留遗忘地址以阻止普通回连；新鲜 SYNC 允许重新接入。界面的删除流程还会断开匹配的当前连接并尝试清理 Windows 配对，失败时保留本地删除结果并提示系统配对未确认清理。

`RegisterHostOnSync` 默认 false，启用仅控制之后满足条件的注册，不代表已完成绑定。注册证据保存主机地址和确认时间，不保存 LTK 或挑战材料。自动连接开关不写入 `BridgeOptions`，每次启动默认开启。

全局 `Motion` 字段来自 [GyroOptions](../ControllerForwardingTool/Input/GyroOptions.cs)：

| 字段 | 默认 | 有效范围/作用 |
| --- | --- | --- |
| `AutoCalibrate` | true | 自动静置零偏校准 |
| `SmoothSmallMotion` | false | 细微动作平滑 |
| `AngularThreshold` | 1.5 °/s | 0.2–5 |
| `AccelerationThreshold` | 0.035 g | 0.01–0.15，重力向量变化门限 |
| `StationarySeconds` | 3 s | 2–10，自动静置时长 |

参数编辑立即作用于传感器处理，点击保存后才跨启动保留。实际校准器与零偏在内存中；Windows 校准器按当前 SDL ID 管理，设备移除后清理。

## 5. 自启、托盘与重复启动

软件默认值：`LaunchAtLogin`、`AutoStartOutput`、`StartInTray`、`MinimizeToTray` 均为 false，`CloseToTray` 为 true。软件设置保存后窗口行为立即生效，启动行为在下次启动时生效。

`LaunchAtLogin` 在界面初始化时以实际 Startup 入口为准。应用保存当前用户 Startup 快捷方式，并清理该用户旧 Run 项；安装器管理所有用户 Startup 入口，普通用户不能通过设置页取消所有用户入口。安装器首次默认勾选自启，与便携版初始配置不同；详见[安装包说明](../ControllerForwardingTool/InnoSetup/README.md)。

`AutoStartOutput` 在启动初始化后、满足启动条件时尝试恢复保存模式。Windows 输入源不持久化，未选择就绪设备时不会自动恢复该线路输出。BLE 模式可先创建中立虚拟输出等待输入。

`Program` 在初始化 Avalonia 之前启动 `SingleInstanceService`。同一程序目录使用命名管道唤醒已有窗口，收到确认的新进程退出；不同程序目录可以分别启动。通信失败或超时允许正常启动，不能当作严格的全系统进程互斥。不同目录副本仍使用同一当前用户配置目录。

托盘支持打开窗口、日志、设置和退出。启动到托盘时收到的激活请求优先显示窗口；窗口位置由 `WindowPlacementStore` 恢复。真正退出时停止输出、解除本会话 USB/IP 挂载、清理 BLE/SDL 资源与自身后端，不卸载系统驱动，也不删除用户配置。
