# 当前实现文档

按 2026-09-27 仓库源码整理，适用于当前 `ControllerForwardingTool` 1.0.0。本文档描述已实现的行为；构建、模拟协议检查和实体硬件验收分别记录，不把历史测试数量当作当前结果。

| 文档 | 内容 |
| --- | --- |
| [手柄通信协议目录](protocols/README.md) | NS2、NS1、Xbox、PS5/Edge、VIIPER、USB/IP 及原始研究资料 |
| [协议完整性与代码一致性核对](protocols/COVERAGE.md) | 已修正文档错误、当前实现边界与测试缺口 |
| [架构与数据流](01_ARCHITECTURE.md) | 模块职责、线程边界、统一状态、两种输出后端、震动反馈和测试页 |
| [使用与排障](02_USER_GUIDE.md) | 驱动、NS2 BLE、Windows 输入、注册回连、测试校准和常见故障 |
| [配置与生命周期](03_CONFIGURATION_AND_LIFECYCLE.md) | 五条独立线路、字段与默认值、保存范围、数据迁移、自启和托盘 |
| [构建、发布与验证](04_BUILD_AND_VALIDATION.md) | 可执行工程路径、发布方式、检查范围和实机验收步骤 |
| [手柄发现与持续振动修复](28_TESTER_DISCOVERY_AND_SUSTAINED_RUMBLE.md) | 当前设备列表与映射参考区的区别、持续马达状态和流式反馈的处理 |

## 其他入口

- [项目 README](../README.md)
- [安装包制作与安装范围](../ControllerForwardingTool/InnoSetup/README.md)
- [第三方来源与许可证](../THIRD_PARTY_NOTICES.md)
- [早期方案、协议研究与迭代记录](../ControllerForwardingTool/docs/README.md)

`ControllerForwardingTool/docs/` 保留历史迭代编号，便于旧链接继续访问；其中四份协议正文已移至 `protocols/reference/`，原位置只保留指向当前协议与历史研究的跳转页。该目录中的 r3—r8、旧工作区路径、发布目录、测试数量以及“待实现”描述均有阶段背景；当前功能以本目录说明和源码为准。两个目录各有一个编号 28 的文档，主题不同，引用时使用完整相对路径。

维护实现文档时，应同时核对实际类型、界面文案、默认值和现存检查工程；外部协议资料的原始来源保留在历史研究文档中。
