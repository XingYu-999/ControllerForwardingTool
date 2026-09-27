# 图形映射对照与 NS2 电量

> 历史记录（1.0.1 文档同步，2026-09-27）：正文保留当时的设计、功能状态与验证结果，不作为当前操作说明。当前键鼠 / 混合输入、全输入映射、NS2 USB 连接及默认配置见[当前文档目录](../../docs/README.md)和[1.0.1 更新日志](../../CHANGELOG.md)。

## 用户行为

- 启动虚拟手柄后，导航显示“按键映射”；停止时隐藏入口，当前映射页返回虚拟手柄页。
- 左图显示所选实体输入的原始状态，右图显示同一帧应用映射和摇杆处理后的输出状态。输出图形及目标名称随 NS1、NS2、Xbox、PS5、Edge 模式变化。
- 点击源图按键，再点击目标图按键，或通过下拉框配置。蓝色框标记当前配置，橙色标记实际按下；草稿必须应用并保存才影响输出。支持查看全部映射。
- 默认 GL → L3、GR → R3。自定义映射仍限 NS2 输入；其他输入支持查看对照。
- 对照不是 Windows 接收成功的证明；实际接收仍通过手柄测试页的 Windows 输入确认。没有新输入超过 250 ms，图形归零。
- 导航“蓝牙连接”改为“NS2 Pro 连接”；图标改为手柄加无线波纹，虚拟手柄使用屏幕加手柄图标。

## 电量依据及边界

参考 [Switch 2 HID 报文研究](https://github.com/ndeadly/switch2_controller_research/blob/master/hid_reports.md) 和 [BLE 特征映射](https://github.com/ndeadly/switch2_controller_research/blob/master/bluetooth_interface.md)。

- 标准 Battery Level 存在时保留其百分比。
- Pro 专用 0x09 报文：offset 1 的 bit 0 外接供电、bit 1 充电、bits 2–5 电量档位 0–9。显示“电量 7/9 档”等原始档位，不把档位或电压编造成精确百分比。
- 已验证有效的 FD2/0x05 报文：offset 0x1F 的两个小端字节为电池毫伏值。电压范围和报文长度校验失败时不显示错误值。FD2 的 offset 1 属于计数器，绝不能按 0x09 电源位解释。
- 连接和注册完成后，每 30 秒尝试读取专用 0x09 特征；每次限时 2 秒。可选读取不延迟连接、不切换输入报文、不增加第二路输入通知。失败时清除档位，继续显示 FD2 电压；断开清除电量。
- 本次未访问实体手柄。真实固件是否允许读取 0x09，以及电压值与实际剩余续航的关系，需要实机验证。

## 验证

`ControllerForwardingTool.Checks` 覆盖电量格式、无效值、输入/输出同帧快照、断开归零、各目标型号能力和按物理位置转换。原有连接注册和映射检查继续运行。

`--render-mapping <目录>` 使用未初始化的测试绑定对象，避免生产 ViewModel 构造器启动 SDL/蓝牙或读取用户设置；以真实 XAML 和控件离线渲染宽度 1100/790，并验证绑定、缩放后按键点击区域及源/目标配置命令。不会保存用户映射或启动虚拟驱动。

构建目录：`artifacts/mapping-comparison-build`。发布目录：`artifacts/Publish/mapping-comparison/ControllerForwardingTool`。
