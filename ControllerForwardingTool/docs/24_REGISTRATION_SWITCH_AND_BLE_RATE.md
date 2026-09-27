# 注册开关与 BLE 输入速率

> 历史记录（1.0.1 文档同步，2026-09-27）：正文保留当时的设计、功能状态与验证结果，不作为当前操作说明。当前键鼠 / 混合输入、全输入映射、NS2 USB 连接及默认配置见[当前文档目录](../../docs/README.md)和[1.0.1 更新日志](../../CHANGELOG.md)。

日期：2026-09-26。

## 注册开关

“扫描与连接”新增“将电脑注册到手柄（默认关闭）”。配置项 `RegisterHostOnSync` 自动保存到当前用户配置；新配置和不含该字段的旧配置均为关闭。保存失败会恢复原值并显示错误。

只有开关开启、且连接开始时收到新鲜 SYNC 广播，才执行四阶段注册。连接期间开关不可改动，避免把已经开始的注册误认为被开关中途撤销。关闭开关只禁止后续写入，不清除手柄内部已有注册、不删除本地注册证据，也不阻止普通唤醒回连。

用户已确认上一版能够回连。本机 21:12 的日志也记录了四阶段注册回复校验成功，随后“普通唤醒、目标主机与本机一致”、GATT Success 和有效 FD2 输入。再次接回 NS2 后是否需要重新注册电脑，仍取决于手柄当前保存的主机信息。

## 16 Hz 调查与改动

测试页原有 BLE 速率由有效报文计数增量除以真实统计窗口时间得到，独立于 33 ms UI 刷新定时器。修改虚拟 USB 输出频率不会增加实体 BLE 输入数据。

公开研究记录默认 60 ms BLE 连接间隔对应约 16.7 Hz。当前代码此前仅保持 GATT 会话，未请求高吞吐连接参数；这与用户的 16 Hz 现象吻合，但旧日志未记录实际协商间隔，因此不能把它当成已测量的原因。

新版每次 GATT 成功后请求 Windows `ThroughputOptimized` 参数并持有请求对象到断开，失败则继续使用系统参数，不中断输入。断开时解除参数监听、释放请求。界面显示 `ConnectionInterval × 1.25 ms` 和从机延迟；参数变动以及每约 10 秒的有效帧率会写入日志。

较短间隔可能增加耗电、减少同一适配器可并发的 BLE 连接容量。协商结果由 Windows、驱动和设备共同决定，提交请求不等于已达到某个帧率。其他项目报告 Windows 原生 BLE 可达到约 15 ms / 66.7 Hz，当前机器的实际结果需要新版日志确认。

## 验证

Release 构建 0 警告、0 错误；115 项检查通过，包含新旧配置默认关闭、启用后保存、关闭后保留既有记录，以及原有完整注册测试。未开启新程序操作用户手柄，未实测这次连接间隔优化。

发布目录：`artifacts/Publish/ble-settings-rate/ControllerForwardingTool`。用户已有注册可直接保持开关关闭，重新连接后在蓝牙输入预览或手柄测试页查看间隔与实际帧率。

## 来源

- [NS2 BLE 连接间隔研究](https://github.com/ndeadly/switch2_controller_research/blob/master/bluetooth_interface.md)
- [Microsoft：请求首选连接参数](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice.requestpreferredconnectionparameters)
- [Microsoft：读取当前连接参数](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice.getconnectionparameters)
- [Microsoft：连接间隔单位](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothleconnectionparameters.connectioninterval)
- [S2P-XInput-Lite 原生 BLE 实测说明](https://github.com/duoduo-88/S2P-XInput-Lite)
