# Xbox：SDL 输入与 Xbox 360 虚拟输出

[协议目录](README.md) · [VIIPER 字段表](VIIPER.md) · [USB/GIP/蓝牙参考](reference/13_XBOX_CONTROLLER_PROTOCOL.md)

核对日期：2026-09-27。当前应用不直接实现 Xbox 360 USB、Xbox One/Series GIP 或蓝牙 HID 解析器。实体设备由随附 SDL 3.4.16 后端读取，虚拟输出固定为 `045E:028E` 的 Xbox 360 身份，不能把它描述为原生 Series/One 输出。

## 输入路径

[SdlGamepadService](../../ControllerForwardingTool/Input/SdlGamepadService.cs) 枚举 SDL Joystick/Gamepad，读取标准按键、轴、可用电量/传感器和原始控件。转发要求 `Standard=true`；未知 HID 仅做原始测试。按键/能力由 SDL 映射决定，不凭型号假定 Elite 独立背键、扳机阻力或体感存在。

[WindowsInputMapper](../../ControllerForwardingTool/Input/BridgeInput.cs) 按物理面键位置转为 Nintendo 命名的内部 `ControllerState`：Xbox A/B/X/Y 对应内部 B/A/Y/X。SDL 摇杆 Y 向下，进入内部向上坐标时反向；输出 Xbox 再编码为向上为正的 i16。

左右扳机经 [TriggerLevels](../../ControllerForwardingTool/Input/TriggerLevels.cs) 从 SDL 标准轴独立读取，内部保留 0–255 模拟值。目标为 Nintendo 时以行程大于 0.5 设置 ZL/ZR；Xbox/PS5 保留行程。XInput 应用结构、真实 USB 报告和本项目内部消息是三层不同格式。

## 输出与反馈

应用→VIIPER 为 20 字节内部消息，`0..3` 按钮、`4/5` 扳机、`6/8/10/12` 摇杆、`14..19` 零填充；详见 [VIIPER](VIIPER.md)。这与真实 Xbox 360 USB 的包型/长度头不同。

VIIPER→应用为 2 字节普通马达强度：strong、weak。映射器将其转为持续状态，经 [BLE 震动编码](NS2_PRO.md) 或 [WindowsRumble](../../ControllerForwardingTool/Input/WindowsRumble.cs) / SDL 普通震动接口返回所选输入设备。单次启动强度可以持续到收到新强度或停止；不能因宿主不重复发送而自动当作 HD 流超时。

## 范围与一致性结论

- Xbox 输出没有 IMU；没有传感器的 Xbox 输入转其他身份时也不生成虚假体感。
- **NS1 已经支持有效源体感转发。** 旧文档“NS1 始终不转发体感”已修正，不能由 Xbox 源缺少 IMU 推断 NS1 后端不支持。
- 不直接实现 GIP 状态机、Xbox Wireless 适配器协议、主机认证、固件命令或独立 Elite 配置。
- 原始硬件报文研究覆盖常见 360 USB、One/Series GIP 和部分蓝牙布局，不是所有固件的完整字节级规范。

可复查的 SDL 源码位于 [SDL3-3.4.16.tar.gz](../../drivers/sdl3/3.4.16/SDL3-3.4.16.tar.gz) 内 `SDL3-3.4.16/src/joystick/hidapi/SDL_hidapi_xbox360.c`、`SDL_hidapi_xboxone.c`。历史 `tools/sdl-build/` 路径只是可选解包/构建产物，不是仓库保证存在的源码位置。
