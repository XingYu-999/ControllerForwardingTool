# 手柄通信协议

按 2026-09-27 的 **1.0.1** 源码核对。这里区分**实体手柄报文**、**应用内部状态/VIIPER 消息**和**USB/IP 传输**；长度与偏移不能跨层套用。偏移均从 0 开始，数值前缀 `0x` 表示十六进制；未特别说明的多字节手柄字段采用小端。

| 快速入口 | 覆盖内容 |
| --- | --- |
| [NS2 Pro](NS2_PRO.md) | BLE 广播、GATT、14 条初始化、FD2、电量、主机注册与震动 |
| [NS2 USB 注册与连接](../29_NS2_USB_REGISTRATION.md) | libusb 管理接口、注册读取 / 写入 / 删除、SDL 占用恢复、USB / BLE 接续 |
| [NS1 Pro](NS1_PRO.md) | 本项目 USB/HID 描述符、输入、IMU、子命令、SPI、震动及未实现范围 |
| [Xbox](XBOX.md) | SDL 输入边界、Xbox 360 输出、模拟扳机和双马达 |
| [PS5 / PS5 Edge](PS5.md) | SDL 传感器、33 字节输入、普通/HD 反馈、Edge 差异 |
| [Windows / SDL 输入](SDL_INPUT.md) | 标准按钮/轴/传感器、DualShock 3/4、通用 HID 与依赖边界 |
| [VIIPER 内部通信](VIIPER.md) | TCP 命令、设备流、四种身份的逐字段输入和反馈格式 |
| [USB/IP 传输](USBIP.md) | NS1 内置服务的列举、导入、SUBMIT/UNLINK、控制与中断传输 |
| [完整性与代码一致性核对](COVERAGE.md) | 已修正错误、源码依据、尚未覆盖的能力与测试缺口 |

当前四类手柄、五种输出身份及主要跨进程传输均有实现说明。它们不是厂商完整协议手册：未知字段、未实现功能和未实测固件均明确保留为限制，不能由 VID/PID 或模拟检查推断完全兼容。

1.0.1 的键鼠和混合输入在应用内生成 / 合并 `ControllerState`，不新增实体手柄协议。全输入映射、鼠标体感和配置入口见[架构](../01_ARCHITECTURE.md)、[键鼠输入](../../ControllerForwardingTool/docs/28_KEYBOARD_MOUSE_INPUT.md)与[混合输入](../../ControllerForwardingTool/docs/31_HYBRID_INPUT_MAPPING.md)。

## 原始研究资料

原有协议资料已集中到本目录的 `reference/`，保留历史来源与背景。当前行为以以上文档及链接源码为准。

- [NS2 BLE 早期参考观察](reference/02_NS2_PRO_PROTOCOL_REFERENCE.md)
- [NS1 / Eden / 实验设备端研究](reference/03_NS1_PRO_PROTOCOL_REFERENCE.md)
- [Xbox XInput、USB/GIP 与蓝牙报文参考](reference/13_XBOX_CONTROLLER_PROTOCOL.md)
- [PS5 USB/蓝牙 HID、CRC 与校准参考](reference/14_PS5_CONTROLLER_PROTOCOL.md)

原 `ControllerForwardingTool/docs/` 中四个协议文件仅保留跳转入口，旧链接仍可访问。[其他开发文档](../README.md) · [项目首页](../../README.md)
