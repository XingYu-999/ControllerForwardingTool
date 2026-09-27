# 协议研究与迭代记录索引

当前版本为 **1.0.1**，实现说明统一从[根目录 docs](../../docs/README.md)进入，版本变化见[更新日志](../../CHANGELOG.md)。本目录保留阶段记录及下列输入 / 映射专题。手柄协议正文及原始研究已集中到[协议目录](../../docs/protocols/README.md)，旧文件名仅保留跳转入口。

| 当前主题 | 文档 |
| --- | --- |
| 数据流、模块和线程 | [架构说明](../../docs/01_ARCHITECTURE.md) |
| 连接、注册、测试与排障 | [使用指南](../../docs/02_USER_GUIDE.md) |
| 线路配置、用户数据和桌面生命周期 | [配置说明](../../docs/03_CONFIGURATION_AND_LIFECYCLE.md) |
| 现存检查工程和发布命令 | [构建与验证](../../docs/04_BUILD_AND_VALIDATION.md) |
| NS2 USB 注册与接口恢复 | [USB 注册与连接](../../docs/29_NS2_USB_REGISTRATION.md) |

## 1.0.1 输入与映射专题

| 文档 | 内容 |
| --- | --- |
| [28 键盘 / 鼠标输入](28_KEYBOARD_MOUSE_INPUT.md) | 独立键鼠输入、F8 捕获、系统回读和默认键位 |
| [29 所有输入方式的按键映射](29_ALL_INPUT_BUTTON_MAPPING.md) | 入口条件、多源按钮与模拟扳机、旧字段兼容 |
| [30 弹窗快速配置](30_TARGET_FIRST_MAPPING_UI.md) | 单项多来源、摇杆双页、逐项配置与草稿预览 |
| [31 实体手柄与键鼠补充](31_HYBRID_INPUT_MAPPING.md) | 补充开关、体感来源、实体轴与断连保护 |

编号沿用迭代文件名；28 同时有键鼠输入、独立线路和根目录测试修复文档，29 也有根目录 USB 注册文档。引用时使用完整路径。

## 历史文档目录

| 文件 | 主题与阅读说明 |
| --- | --- |
| [01 BLE → NS1 实施方案](01_BLE_TO_NS1_IMPLEMENTATION.md) | 最初设计与接口建议，不是当前架构清单 |
| [02 NS2 协议参考](../../docs/protocols/NS2_PRO.md) | 公开研究、输入格式与资料来源 |
| [03 NS1 协议参考](../../docs/protocols/NS1_PRO.md) | HID、子命令和虚拟设备边界 |
| [04 驱动与许可](04_DRIVER_AND_LICENSE.md) | 随附组件来源、哈希与许可记录 |
| [05 UI 设计规则](05_UI_DESIGN_RULES.md) | 页面与视觉规范的设计背景 |
| [06 原型状态](06_PROTOTYPE_STATUS.md) | 早期实现及各阶段验证，不能作为当前未实现清单 |
| [07 连接与虚拟 USB](07_CONNECTION_AND_VIRTUAL_USB.md) | 早期连接流程；当前操作请看使用指南 |
| [08 测试、校准与稳定性](08_TESTER_CALIBRATION_AND_STABILITY.md) | 手柄布局、静置校准及 NS1 卡死修复 |
| [09 浅色工作台](09_ARENA_AND_CONTROLLER_LAB.md) | r3 多身份输出与反馈处理 |
| [10 扳机与概览](10_TRIGGER_READOUT_AND_OVERVIEW.md) | r4 扳机行程和界面流程 |
| [11 体感与发布](11_MOTION_CALIBRATION_AND_PUBLISH.md) | r5 姿态、校准与运行包精简 |
| [12 多类型输入](12_MULTI_CONTROLLER_INPUT.md) | r6 Windows 源和历史合成回读验证 |
| [13 Xbox 协议](../../docs/protocols/XBOX.md) | XInput、USB/GIP 与蓝牙资料 |
| [14 PS5 协议](../../docs/protocols/PS5.md) | DualSense/Edge 的 HID、CRC、IMU 和能力边界 |
| [15 产品图片](15_CONTROLLER_PRODUCT_IMAGES.md) | 输出卡片素材来源 |
| [16 NS1 体感与震动](16_NS1_MOTION_RUMBLE_AND_REPORT_RATE.md) | 已实现 IMU/震动回传及报告率修复，更新早期原型边界 |
| [17 NS2 唤醒回连](17_NS2_WAKE_RECONNECT.md) | 历史地址探测方案，后续改为广播触发 |
| [18 主机注册提案](18_NS2_HOST_REGISTRATION_PROPOSAL.md) | 历史待实现方案；实现见 23、开关见 24 |
| [19 SYNC 与用户数据](19_SYNC_RECOVERY_AND_USER_DATA.md) | 广播过滤恢复、配置与日志入口 |
| [20 GATT 与遗忘](20_BLE_SESSION_RECOVERY_AND_FORGET.md) | 会话释放、删除历史和系统配对清理 |
| [21 回连调查与列表](21_NS2_RECONNECT_RESEARCH_AND_LIST_UI.md) | 回连机制研究和列表修复 |
| [22 SYNC 连接时序](22_SYNC_AUTO_CONNECT_TIMING.md) | 新鲜广播、失败冷却及串行连接 |
| [23 主机注册实现](23_NS2_HOST_REGISTRATION.md) | 四阶段交换、校验及历史验证记录 |
| [24 注册开关与 BLE 速率](24_REGISTRATION_SWITCH_AND_BLE_RATE.md) | 默认关闭注册、连接间隔请求 |
| [25 按键映射](25_BUTTON_MAPPING_AND_PREVIEW.md) | NS2 自定义映射与预览 |
| [26 映射对照与电量](26_MAPPING_COMPARISON_AND_BATTERY.md) | 图形对照与电量档位 |
| [27 源帧发送与测试卡片](27_SOURCE_PACING_AND_TESTER_CARDS.md) | 输出节奏；测试卡片行为后由根目录 28 更新 |
| [28 独立输出线路](28_OUTPUT_ROUTE_SETTINGS.md) | 五条线路、草稿隔离和迁移 |
| [根目录 28 手柄发现与持续振动](../../docs/28_TESTER_DISCOVERY_AND_SUSTAINED_RUMBLE.md) | 当前发现列表、折叠映射参考区和持续马达反馈 |

## 已被后续实现替代的说明

- 主机注册已实现，且由默认关闭的 `RegisterHostOnSync` 控制；“完整注册待授权”仅属于历史提案。
- NS1 输出已经支持 IMU 和震动转码；“IMU 中立、震动未实现”只描述早期原型。
- 测试页上方是实际连接列表，下方五种身份始终为映射参考，不自动切换为真实回读。
- 映射覆盖 NS2 USB / BLE、Windows 标准手柄及键鼠；入口仅在虚拟输出运行且已被 Windows 识别时开放。映射页使用草稿预览，测试页读取实际设备。
- 新建 NS1 线路启用键鼠补充并使用专用预设；其他线路和旧配置缺省时关闭补充，不能将“默认关闭”套用到全部线路。默认值见当前配置文档。
- 1.0.1 加载线路或启动时无实体输入会选择键鼠；运行中断线仍整体归零。
- 数据目录为 `%LOCALAPPDATA%\ControllerForwardingTool`；旧 `NS2ProWin11` 目录仅用于兼容导入，不是当前保存位置。
- 删除 BLE 历史会断开匹配的当前连接并尝试清理 Windows 配对；旧“保留当前连接”描述已过时。
- 当前检查工程位于 `tools/`。旧测试数量、发布路径和渲染命令保留为阶段记录，可运行范围见构建文档。

## 来源和证据边界

Nintendo、Xbox、PS5 研究文档引用厂商应用接口及社区逆向资料，不构成完整官方协议或固件兼容性保证。历史实测仅适用于当时的设备、环境和版本；合成输入、Windows 虚拟设备回读与实体无线/游戏验收应分别理解。

第三方许可和分发要求以仓库 [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md) 及随附许可证为准。后续维护保留历史结论的日期，同时在当前文档更新实现状态。
