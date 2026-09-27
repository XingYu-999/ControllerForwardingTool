# PS5 / PS5 Edge：当前输入、输出与反馈

[协议目录](README.md) · [VIIPER 字段表](VIIPER.md) · [USB/蓝牙 HID 与 CRC 参考](reference/14_PS5_CONTROLLER_PROTOCOL.md)

核对日期：2026-09-27。当前虚拟身份为 DualSense `054C:0CE6` 与 Edge `054C:0DF2`。实体输入交给 SDL，应用自己处理的是标准状态映射和随附 VIIPER haptic 消息。

## 实体 USB/蓝牙输入

[SdlGamepadService](../../ControllerForwardingTool/Input/SdlGamepadService.cs) 读取标准按键、摇杆、扳机和 SDL 成功启用的传感器。USB/蓝牙头、CRC、工厂校准和增强模式由 SDL 处理，C# 层没有第二套 PS5 HID/CRC 解码器。

SDL 提供 gyro rad/s 和 accel m/s²；本项目转换为 °/s 和 g，进行静置零偏/平滑等处理，再由 [WindowsInputMapper](../../ControllerForwardingTool/Input/BridgeInput.cs) 转到统一坐标。按物理位置映射面键，保留 L2/R2 行程。触摸板点击可作为按钮，触摸坐标没有进入 `ControllerState`。

随附 SDL 源码位于 [SDL3-3.4.16.tar.gz](../../drivers/sdl3/3.4.16/SDL3-3.4.16.tar.gz) 内 `SDL3-3.4.16/src/joystick/hidapi/SDL_hidapi_ps5.c`。常见实体 USB `01` 输入、蓝牙增强 `31` 输入/输出和 Feature `05/09/20` 等硬件资料保留在[参考文档](reference/14_PS5_CONTROLLER_PROTOCOL.md)，它们不是应用内部消息定义。

## 虚拟输入 33 字节

普通 PS5 使用后端类型 `dualsensehaptic`，Edge 为 `dualsenseedge`。两者输入布局相同：四个有符号 8 位摇杆、u32 按钮、单独方向位、两个扳机、10 字节零填充、三轴 gyro 和 accel，偏移详见 [VIIPER](VIIPER.md)。

内部 Y/B/A/X 映到 PS 的方块/叉/圆/三角。Edge 额外编码 GL/GR；普通 PS5 不编码这两个背键。体感使用 `(GyroX, GyroZ, -GyroY)`，各轴应用线路倍率/反向；加速度为 `(AccelX, AccelZ, -AccelY)×2`，均饱和到 i16。

当前没有生成触摸位置、麦克风音频或自适应扳机数据。源没有有效 IMU 时输出零体感；源有 IMU 时也可以转 NS1，且 NS1 需由主机启用 IMU。旧“PS5 转 NS1 始终零体感”的结论已过时。

## 普通 PS5 的 388 字节反馈

[DualSenseHapticFeedback](../../ControllerForwardingTool/VirtualDevice/Source/DualSenseHapticFeedback.cs) 先检查帧恰为 388 字节、有效负载长度≤384，然后按 kind 分派。头为 kind u8、length u16 LE、一个未读取字节，负载从 4 开始。

### kind 1：HID 输出

要求负载至少 5 字节且 Report ID 为 `02`。从 ID 后的公共块读取：

| 公共偏移 | 当前使用 |
| --- | --- |
| 0 | `validFlag0`，bit0 为兼容震动 v1、bit1 为 haptics select |
| 2 / 3 | weak / strong |
| 38（存在时） | `validFlag2`，bit2 为兼容震动 v2 |

上述三个选择位任一置位就采用普通兼容强度；兼容模式下拒绝 PCM 触觉输出。切到音频触觉模式时清理音频处理状态并发停振，不再把普通马达字节当作有效震动。其他灯光、音频端点设置和自适应扳机字段没有完整反向转发到源手柄。

### kind 2：PCM 触觉

处理四声道 i16 小端帧，每帧 8 字节；只取第 3/4 声道（帧偏移 4/6）作为左右触觉，前两路普通声音不转成震动。处理器按 16 倍降采样、3000 Hz 频谱分析的约定解释数据，窗口 64、步长 36；这些是当前算法参数，不是实体 HID 包字段。

PCM 经幅度/频谱转换为 NS2 震动表达，是近似转码，不是四声道音频原样输出。回传到 Windows 输入设备时还会降为 SDL 普通马达能力。该路径不能证明任意输入手柄都具备原生 HD 触觉。

## Edge 的 6 字节反馈

Edge 不走普通 PS5 的 388 字节调度器。当前会话固定读取 6 字节，mapper 只使用前两字节 weak/strong，其余四字节忽略，返回持续普通强度。当前未实现 Edge 完整高级配置、所有功能键语义或 HD 音频回传。

## 未覆盖部分

实体蓝牙 CRC、所有 Feature 布局及固件差异由 SDL 版本承担；本项目文档不声称完整覆盖 Sony 协议。触摸轨迹、完整灯光、自适应扳机、麦克风/扬声器交互、认证及任意固件命令不在通用转发范围。PS5/Edge 音频保护只是 Windows 默认端点管理，不是设备协议透传。
