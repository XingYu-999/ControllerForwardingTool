# r6：多类型输入与摇杆坐标

更新：2026-09-26。本文描述本项目实现；硬件协议资料见 [Xbox](13_XBOX_CONTROLLER_PROTOCOL.md) 和 [PS5](14_PS5_CONTROLLER_PROTOCOL.md)。

## 使用方法

1. 打开「虚拟手柄」，在「输入手柄」选择输入类型。
   - **NS2 Pro · 直连蓝牙**：保留原来的 BLE 扫描、连接、自动重连流程。
   - **Windows 手柄 · USB / 系统蓝牙**：插入 Xbox、Switch Pro、PlayStation 或 SDL 已知映射的其他手柄；蓝牙设备需先在 Windows 配对，再从设备列表选择实体手柄。
2. 选择 PS5 / NS2 PRO / XBOX / PS5 EDGE / NS1 PRO 图片卡片，启动虚拟 USB。例：Xbox 输入 + NS1 PRO 输出，或 Switch Pro 输入 + NS2 PRO 输出。
3. 在手柄测试中选择输出设备进行验证。选择测试设备不会更换虚拟页的输入设备。开始游戏前离开测试页，释放 NS2 虚拟 USB 的独占初始化接口。
4. 更换输入源或输出身份前停止输出。所选实体手柄断开时输出立即归零；后台输入线程停止更新时，虚拟输出的 250 ms 超时保护归零。重新插拔产生新设备 ID 后，停止输出并重新选择设备。

输入类型可随「应用并保存」持久化。Windows 设备 ID 仅本次进程有效，重启后必须重新选择设备，不会猜测或自动切换到另一个手柄。未知原始 HID 仍可在测试页读取，但没有标准映射时不作为转换输入，以免把错误的轴、按键送入游戏。

**NS2 USB 实体 → NS2 USB 输出**目前不开放：这两个设备共享同一 SDL/WinUSB 独占访问策略。请改用 NS2 直连蓝牙作为输入；NS1 / Xbox / PS → NS2 不受此限制。

## 默认映射与缺少的功能

面键保持**物理位置**，例如 Xbox A（下方）→ Nintendo B（下方），而不是保持字母。配置按输入能力降级，不合成硬件不存在的信号，本版不增加键盘替代键。

| 输入位置 / 功能 | Nintendo 输出 | Xbox 输出 | PlayStation 输出 |
| --- | --- | --- | --- |
| 下 / 右 / 左 / 上面键 | B / A / Y / X | A / B / X / Y | × / ○ / □ / △ |
| 十字键、左右摇杆、摇杆按下 | 对应位置 | 对应位置 | 对应位置 |
| 左 / 右肩键 | L / R | LB / RB | L1 / R1 |
| 左 / 右扳机 | ZL / ZR，行程大于 50% 按下 | LT / RT，保留 0–255 行程 | L2 / R2，保留 0–255 行程 |
| View / Back / Create、Menu / Options | −、+ | View、Menu | Create、Options |
| Home / Guide / PS | Home | Guide | PS |
| 截图 / Share（输入驱动提供时） | Capture | 无对应输出 | 触摸板按下 |
| C / 背键 | 目标和源均支持时转发 | 无对应输出 | Edge 可转发两枚背键；普通 PS5 不支持 |
| 加速度、角速度 | NS2 可转发；当前 NS1 输出仍为零 | 为零 | 源具有有效传感器样本时转发 |
| 触摸点位置、麦克风、音频、自适应扳机阻力 | 不转发 | 不转发 | 不转发 |

源没有陀螺仪时，输出角速度、加速度和体感时间戳均为零；不再用固定重力值伪造体感。源有传感器时使用该设备的校准结果。到「手柄测试」选择**同一个实体输入设备**再校准，虚拟页的 NS2 BLE 摇杆中心/行程校准不会应用到 SDL 已归一化的其他设备。

普通震动反馈可送回所选 Windows 输入设备：通过 SDL 双马达接口近似输出强弱；没有震动能力的设备不会产生震动。HD Rumble 频率、PS5 音频触觉与自适应扳机无法无损转换。**当前 NS1 输出后端尚未实现游戏震动反馈转码**，不会因改换输入手柄而获得该能力。

## Y 轴修复依据

参考 `XinHeLianSheng-Pro2-Bridge/source/final-three-in-one/windows/v60_viiper_app/VirtualPadPackets.cs`：NS2 原始 Y 值向上增大，Xbox 编码保留方向，PS 编码需要反向。此前测试页给 NS2 原始 Y 再加了一次负号，造成上下颠倒。

本版统一为：

| 层 | X 正方向 | Y 正方向 |
| --- | --- | --- |
| NS2 原始 12 bit / 内部 ControllerState | 右 | 上 |
| SDL 标准摇杆 | 右 | 下 |
| 测试图逻辑坐标 | 右 | 上（绘制时换算屏幕坐标） |
| Xbox 输出 | 右 | 上 |
| PS 输出 | 右 | 下 |

`StickCoordinates` 映射中心 2048、负端 0、正端 4095；SDL 仅在进入内部状态时反转 Y。NS2 测试图直接使用内部 Y。USB 输出协议并未因修复显示而统一取反。

SDL 的 Switch Pro 驱动在打开设备时以工厂标定行程的 70% 初始化动态范围，随后根据观测极值扩大范围。初次读取 NS1 可能显示更大的行程；沿边缘转动两个摇杆一圈后再比较线性比例。此行为来自 SDL 的 `LoadStickCalibration` / `ApplyStickCalibration`，不应通过反复缩放发送端报文补偿，否则其他接收程序的行程会变小。

## 线程、回环与缺失保护

- `GamepadMonitor` 的 SDL 专用线程发布不可变快照，直接推送转换输入，不依赖 30 Hz UI 刷新。
- `ControllerInputBridge` 串行处理输入切换及数据发布；非选中来源的回调不能覆盖当前输入，BLE 断线也不会清空正在使用的 Windows 输入。
- `BridgeInputGuard` 排除本程序带 `NS2PROWIN11-` 序列号的设备，以及挂载后新增且符合输出 VID/PID 的设备；挂载前已有的同型号实体设备仍可使用。
- 输出运行期间锁定输入选择，避免把自己的输出选回输入。运行中再插入与输出同 VID/PID 的实体设备可能被保守排除；先停止输出，再重新插入并选择。
- 不支持的轴、缺失传感器、非有限读数默认归零。震动使用可替换的最新请求及短时续发，不堆积无限队列；切换来源和停止输出时清除反馈。

## 验证

```powershell
dotnet build ControllerForwardingTool.slnx -c Release
dotnet run --project NS2ProWin11.Checks -c Release
# 使用已安装驱动挂载临时输出；用合成的实体输入快照验证实际 Windows 回读，结束后解除挂载
dotnet run --project NS2ProWin11.Checks -c Release -- --attach-bridge
```

自动检查覆盖坐标方向、面键位置、扳机行程、缺失体感为零、校准体感坐标、选中设备断线、陈旧数据、来源切换和虚拟设备排除。

本机已实际挂载并回读 Xbox → NS1、NS1 → NS2、Xbox → PS5、PS5 → Xbox 四条链路的合成输入，验证方向、按键、模拟扳机及断线归零。该验证证明 Windows 输出链路，不等于四种实体手柄与所有游戏均已实测。此次桌面自动操作被用户按 Esc 停止，最终 UI 交互未完成实测。

## 发布记录

`tools/package-release.ps1 -Version 20260926-r6` 使用独立构建与发布目录，不覆盖已运行的旧版本。运行包 47 项、57,308,595 字节，已检查 ZIP 完整性及 docs / PDB / 源码归档排除规则。默认包需 .NET 10 运行时，完整保留 DLL、驱动安装器和许可证。

| 产物（仓库 artifacts 目录） | SHA-256 |
| --- | --- |
| NS2ProWin11-win-x64-20260926-r6.zip | `579f44eb07c348271b2122ad2e23456f72ef1a8142e6036d2fa33a119e20c20c` |
| NS2ProWin11-third-party-sources-20260926-r6.zip | `ff408df4509f444ba2ecffa64e4e0c2c02c74b8f9607bc8cf31d5cf694fc2c1f` |

配套第三方源码包为 11 项、16,768,292 字节，重新分发时请与运行包一起提供。本轮未修改 SDL / libusb / VIIPER 二进制。
