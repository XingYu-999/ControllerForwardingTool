# NS2 回连机制调查与设备列表修复

后续状态：文中当时缺失的主机注册现已实现，见 [23_NS2_HOST_REGISTRATION.md](23_NS2_HOST_REGISTRATION.md)；以下保留当时的调查与验证记录。

调查日期：2026-09-26。本轮完成扫描列表过期清理、历史记录内重命名，以及下面的源码分析；没有把普通唤醒回连记作已修复。

## 本轮界面修改

- 扫描列表此前只有添加/更新，没有按最后广播时间移除的逻辑。现在每秒检查一次，约 6 秒没有新广播就移除未连接设备，并清除它的选中状态。连接尝试不会延长旧广播的有效期。已建立连接的手柄不因停止广播而被误删；断连事件或输入超时会移除对应扫描项。
- 历史记录独立保留。后台按保存地址进行探测，不再把没有新广播的地址设为“可用设备”。
- 每条历史记录右侧依次为“删除”“重命名”。点击重命名，在历史列表下展开编辑区，支持保存、取消、留空恢复原名；扫描区不再放名称输入框。编辑目标按行地址和地址类型固定，不随扫描选中项变化，离线设备也可改名。
- 手动/自动连接日志均增加广播目标与本机适配器是否一致的信息，以补足下面的诊断证据。

## 指定本地工程

检查的是 `D:\github\XinHeLianSheng-Pro2-Bridge\source\final-three-in-one` 中的 Windows BLE / VIIPER 源码，不把 Pico 输出端的蓝牙配对误认为 NS2 输入端配对。

| 位置 | 实際行为 | 与普通唤醒的关系 |
| --- | --- | --- |
| [MainViewModel.cs:1689](D:/github/XinHeLianSheng-Pro2-Bridge/source/final-three-in-one/windows/v60_viiper_app/MainViewModel.cs:1689) 的 `AutoReconnectLoopAsync` | 处理断连/输入超时，释放旧连接，继续扫描重试；输入正常时保存地址 | 提供重试调度，没有完成手柄端主机注册 |
| [Pro2BleInputSource.cs:741](D:/github/XinHeLianSheng-Pro2-Bridge/source/final-three-in-one/windows/v60_viiper_app/Pro2BleInputSource.cs:741) 的 `ConnectCandidateAsync` | FromBluetoothAddress、服务发现、订阅 ACK、发送初始化序列、等待 FD2 | 连接与读取输入流程 |
| [Pro2BleInputSource.cs:82](D:/github/XinHeLianSheng-Pro2-Bridge/source/final-three-in-one/windows/v60_viiper_app/Pro2BleInputSource.cs:82) | 初始化中有单独的 `0x15/0x03` | 未在这条 Windows 路径发现对应地址交换、密钥交换和确认，不能当作完整配对 |
| [ble_central.c:2557](D:/github/XinHeLianSheng-Pro2-Bridge/source/final-three-in-one/firmware/esp32s3_switch2_bridge/main/ble/ble_central.c:2557) | ESP 输入端优先扫描保存地址，并有后台重试 | 保存目标和重试仍不等于主机注册 |

因此，该项目存在“自动重连”处理，但不能从它的命名或界面文案推断已解决“首次 SYNC 后，普通按键即可回连 Windows”。没有在检查的 Windows 输入路径找到可直接补齐本项目缺口的完整注册流程。

## GitHub 上的实现

**Switch2Connect**：`discoverer.py` 把 Nintendo 厂商数据中的目标地址分成零值（配对）和本机地址（回连）。初次连接初始化后调用 `controller.pair(host_mac_value=...)`，普通回连不重复这一步。`controller.py` 的 `pair()` 依次发送 `0x15` 的 `01 / 04 / 02 / 03` 子命令；使用传入的实际适配器地址，防止多适配器机器选择错主机。这是本项目目前没有的关键阶段。[发现与分流源码](https://github.com/TommyWabg/Switch2Connect/blob/main/src/discoverer.py)、[配对源码](https://github.com/TommyWabg/Switch2Connect/blob/main/src/controller.py)。

**Pryxo 的 DS4Windows Switch 2 分支**：`Switch2PairingProtocol.GetPairingCommands()` 同样构造地址交换、两块配对数据和最终提交；文档明确要求首次 SYNC，在应用内完成专有 GATT 配对，之后普通按键唤醒。[协议源码](https://github.com/Pryxo/DS4Windows-Switch-2-Pro-Controller-and-Wireless-Support/blob/master/DS4Windows/DS4Library/InputDevices/Switch2PairingProtocol.cs)、[使用及机制说明](https://github.com/Pryxo/DS4Windows-Switch-2-Pro-Controller-and-Wireless-Support/blob/master/doc/switch2-bluetooth-pairing.md)。调查核实了源码和文档，没有安装或实测这些第三方软件，不能承诺其在当前适配器上工作。

## NS2 为什么可以 SYNC 连接，却不能普通唤醒回连

NS2 使用 BLE 和 Nintendo 自定义 GATT 接口。SYNC 模式广播的目标主机字段为零；普通回连广播携带保存的主机蓝牙地址。应用需要在首次配对阶段让手柄保存本机主机信息。L + R 的作用是唤醒手柄，应用并不是在连接建立前接收一条“L + R 重连命令”。[广播与配对研究](https://github.com/ndeadly/switch2_controller_research/blob/master/bluetooth_interface.md)。

完整注册包括主机地址交换、密钥材料交换、挑战确认和最终提交。研究记录的 LTK 由双方材料异或得到，挑战确认涉及反序后的 AES-128 ECB；最终提交才把主机信息持久化。标准 Windows SMP 配对不是这个过程，研究观察到控制器会拒绝它。输入通知可以在未完成注册时使用，所以“收到 FD2”“写入历史记录”都不足以证明普通唤醒已经可用。[协议研究](https://github.com/ndeadly/switch2_controller_research/blob/master/bluetooth_interface.md#pairing)。

本程序当前已经完成发现、GATT 输入初始化、PC 本地历史和失败重试，却尚未实现完整的主机注册。这个明确的软件缺口与用户现象吻合。上一轮移除单独的配对提交指令，也不等于补齐注册。

**诊断边界**：最新用户日志在 20:26:25 显示“普通唤醒、可连接提示=True”，随后链路 Disconnected、会话 Closed、GATT Unreachable。旧日志没有记录这次广播目标是否与本机一致，所以还不能断言一定是发往其他主机，也不能排除适配器状态或另一程序占用。此次补充的目标匹配日志用于区分这些情况。

## 后续实现与验收应解决什么

1. 在明确的 SYNC 配对流程中获取实际使用的适配器地址，核对目标控制器身份。
2. 实现完整注册，逐条匹配命令及子命令 ACK，验证地址、长度和挑战结果，确认成功后再提交；超时或校验失败不能标记注册成功。不能只复制固定数据包就宣称验证完成。
3. 注册状态和“曾连接成功”分开保存。普通回连保留 GATT 对象与会话，仅初始化输入，不反复改写注册信息。适配器更换后重新核对注册主机。
4. 验收至少覆盖一次 SYNC 注册、关闭/唤醒、重启程序后普通按键回连、删除后重新配对、多适配器；以匹配的目标广播和有效 FD2 输入为成功证据。

这些是源码分析得出的后续工作，不是本轮已完成的无线修复。本轮没有向用户手柄发送新的配对写入，也未清除其固件中的其他主机记录。

## 本轮验证

Release 构建 0 警告、0 错误，71 项无硬件检查通过。新增检查覆盖扫描项 6 秒过期、保存地址不冒充新广播、已连接设备不因广播静默而误删、地址类型隔离。新版窗口已经启动查看，扫描区不再显示名称编辑框。界面检查期间用户自行删除了真实历史记录并已确认，因此没有为了验证重命名而恢复该记录；重命名入口、保存/取消绑定已通过 XAML 构建，真实记录的完整改名操作未在本轮另行执行。

发布目录：`artifacts/Publish/ble-list-ui/ControllerForwardingTool/`。
