# NS2 主机注册实现

后续：用户已确认回连成功；注册现由默认关闭的独立开关控制，见 [24_REGISTRATION_SWITCH_AND_BLE_RATE.md](24_REGISTRATION_SWITCH_AND_BLE_RATE.md)。下文保留注册实现及当时验证记录。

日期：2026-09-26。补齐此前文档 21、22 中尚未实现的 SYNC 主机注册；普通唤醒回连仍需用户实机验收。

## 实现行为

1. 连接开始时确认收到新的 SYNC 配对广播。先完成现有 GATT 初始化并收到有效 FD2，随后注册本机。普通唤醒或缺少有效配对广播时不写入注册信息。
2. 枚举当前开启的 BLE 适配器，核对 WinRT 连接 ID 中的本机与手柄地址。该 ID 格式只是实现提示，不视作稳定 API；无法解析时只允许唯一适配器，多个适配器不猜默认值。
3. 依次执行 `0x15/01` 地址交换、`04` 随机密钥材料交换、`02` 随机挑战、`03` 提交。核对手柄返回地址，计算双方材料异或得到的 LTK，按协议字节序完成 AES-128 ECB 验证，验证失败不提交。
4. 注册回复必须匹配命令、子命令、方向、BLE 传输、ACK、最短长度及负载标志。其他命令的迟到回复不会推进注册。每条交换含写入及回复共限时 3 秒；失败中止本次事务，不在事务内重发可能已完成的提交。
5. 最终回复验证通过才显示“主机注册已确认”。历史记录保存注册主机地址与确认时间，不保存密钥。删除历史同时清除此证据与名称。旧版仅收到输入的记录仍显示“尚未确认主机注册”。

密钥与挑战不写入日志；日志仅显示阶段、匹配回复长度及掩码地址。最终提交回复丢失时明确报告状态未确认，不能据此断言手柄没有保存。连接失败提示在恢复扫描后保留。

## 验证

Release 编译和 112 项检查通过。新增检查覆盖已公开 AES 报文向量、四阶段模拟交换、随机材料、错误/截断回复、地址不符、挑战不符、逐阶段超时及取消、迟到回复、多适配器选择、配置往返与删除。未访问真实蓝牙硬件，也未把模拟测试等同于固件兼容性或真实回连成功。

## 用户验收

- 退出旧程序，启动 `artifacts/Publish/ble-registration/ControllerForwardingTool/ControllerForwardingTool.exe`，保留自动连接开启。
- 长按顶部 SYNC 一次，等待连接状态和历史记录显示“主机注册已确认”。仅显示输入正常、旧历史记录或设备已发现均不足以确认完成。
- 关闭手柄，等待软件恢复搜索，再用 L + R 或普通按键唤醒；不按 SYNC、不点击连接，检查是否恢复有效输入。
- 再验证退出/重启程序后的普通唤醒。使用软件的“断开连接”会主动关闭自动连接，测试前需重新开启开关。
- 若失败，保留日志；注册四个阶段和普通唤醒的目标主机字段可区分注册失败、目标不符与 GATT 不可达。

## 依据

- [协议命令与回复格式](https://github.com/ndeadly/switch2_controller_research/blob/master/commands.md#command-0x15---bluetooth-pairing)
- [LTK 与挑战计算、公开验证向量](https://github.com/ndeadly/switch2_controller_research/blob/master/bluetooth_interface.md#pairing)
- [Switch2Connect 配对阶段](https://github.com/TommyWabg/Switch2Connect/blob/main/src/controller.py)
- [DS4Windows 分支的配对命令顺序](https://github.com/Pryxo/DS4Windows-Switch-2-Pro-Controller-and-Wireless-Support/blob/master/DS4Windows/DS4Library/InputDevices/Switch2PairingProtocol.cs)

按协议独立实现，不复制第三方固定配对数据包。挑战回复使用公开测试向量确认顺序；不会为了通过未知固件回复而跳过校验。
