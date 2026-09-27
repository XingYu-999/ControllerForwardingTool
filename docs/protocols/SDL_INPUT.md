# Windows / SDL 输入：标准映射与其他手柄

[协议目录](README.md) · [Xbox](XBOX.md) · [PS5](PS5.md)

核对日期：2026-09-27。本应用除四类主要协议外，还能通过 SDL 识别 DualShock 3、DualShock 4 和通用设备。这里说明实际消费的 SDL 接口数据，避免把“能识别布局”误当作已独立实现该型号的全部硬件协议。

## 职责与标准数据

[SdlGamepadService](../../ControllerForwardingTool/Input/SdlGamepadService.cs) 通过 P/Invoke 调用随附 SDL 3.4.16，USB/系统蓝牙初始化、HID/XInput 后端及具体固件适配由 SDL 承担。所有本机调用由 [GamepadMonitor](../../ControllerForwardingTool/Input/GamepadMonitor.cs) 的专用线程执行，UI 消费快照。

| SDL 数据 | 本应用读取方式 |
| --- | --- |
| 标准轴 0/1/2/3 | LX/LY/RX/RY，i16 按正负端分别归一化为 −1..1 |
| 标准轴 4/5 | 左/右扳机，经 `TriggerLevels` 限制为 0..1，再转 0..255 |
| 标准按钮 0..3 | 面键物理位置，按设备布局命名后由输入桥统一成 Nintendo 命名 |
| 标准按钮 4..14 | Minus/Home/Plus、L3/R3、L/R、四方向 |
| 标准按钮 15/16/17 | 映到 Capture/GR/GL；是否可用取决于 SDL 设备映射 |
| 按钮 20 | PlayStation 布局下映到 Touchpad 点击 |
| 按钮 21 | Nintendo 且 PID `2069` 时映到 C |
| 其余标准按钮 | 可显示原始状态，但当前不全部进入转发按钮集合 |
| Sensor 1/2 | accel m/s²、gyro rad/s；转换为 g、°/s 后校准 |
| Joystick Power | 可用百分比；缺失保持未知 |

标准快照读取 26 个按钮和 6 个轴，但转发不是 26 个按钮无损透传。未知布局读取原始按钮（最多 128）、轴（最多 32）和 Hat（最多 8），不猜标准含义，不能作为 `Standard=false` 转发源。

## 型号和能力边界

[ControllerLayouts](../../ControllerForwardingTool/Input/ControllerLayout.cs) 优先识别 NS2 `057E:2069`、NS1 `057E:2009`、PS5 `054C:0CE6`、Edge `054C:0DF2`，其余用 SDL 类型识别 Xbox、DualShock 3/4、DualSense、Switch Pro，否则用通用布局。该表服务于布局/映射，不是全型号兼容清单。

DualShock 3/4、第三方兼容设备的原始 USB/蓝牙、触摸、压力键或扩展报文没有在本项目逐字节重新实现或完整记录；接入能力以 SDL 实际返回的数据为准。即使界面有对应外观，也不代表该设备有传感器或支持震动。

输入桥要求所选设备仍存在、标准映射有效且快照不超过 250 ms，否则发布中立。Windows NS2 USB 输入不能同时输出 NS2 身份，以避免独占接口冲突；应用自己的虚拟输出通过序列号/会话归属排除作为输入。

输入 Hz 优先按 gyro 传感器事件的接收时间戳统计；否则统计轴/按钮/Hat 事件，同时间戳去重，不计每轮 `UPDATE_COMPLETE`。因此无动作时事件速率可能下降，不能将它等同物理 USB 轮询率。普通反馈通过 `SDL_RumbleJoystick` 发送高低频强度和时长，没有通用自适应扳机、扬声器或 LED 透传。

依赖源码见 [SDL3 归档](../../drivers/sdl3/3.4.16/SDL3-3.4.16.tar.gz)，其中 `src/joystick/hidapi/` 含各设备驱动。本文核对的是 C# 与 SDL 的边界，未把全部依赖驱动等同于本项目已审计的协议。
