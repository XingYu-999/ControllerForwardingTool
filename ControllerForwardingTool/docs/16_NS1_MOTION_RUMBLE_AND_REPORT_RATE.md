# NS1 体感、震动与测试页速率修复

2026-09-26。本页描述本次实现，替代历史文档中“NS1 体感和震动尚未接入”的能力说明。

- NS2 / Edge 测试图背键移入握把，按下状态和标签均不遮挡外框。
- 虚拟页和概览页直接显示正在模拟的模式；运行中的模式卡保留强调样式。停止后恢复“已选择 / 未启动”。
- NS1 `0x30` 报文填充三个 IMU 槽位。目前三个槽位重复最新源采样，不伪造额外采样精度。主机 `0x40` 控制 IMU 启停，断线 250 ms 后清零。
- NS2 到 NS1 的原始轴转换为 `(Y, -X, Z)`。加速度保持 4096 counts/g；角速度从 16.384 counts/(°/s) 转换为与工厂 SPI 校准 `13371 / 936` 一致的单位。符号翻转和量化采用饱和运算。
- NS1 `0x10` 和 `0x01` 报文中的左右震动包解码后，转换为现有 NS2 输出链路使用的低、高频强度。保留左右侧差异；BLE 继续应用震动倍率；Windows 输入经 SDL 普通双马达回传。停振和 `0x48=0` 生成零强度。此实现不完整重现原始 HD 频率和波形。
- NS1 本地 USB/IP 服务现在和其他模式共用反馈处理，触发 `RumbleReceived`；诊断不再仅计数主机命令而忽略震动。实际发出的 `0x30` 报文计入输出统计。

## Hz 的含义

BLE 输入按有效报文数 / 实际时间显示上传速率。Windows 带体感手柄按最近一秒的 SDL 传感器报文接收时间估测，报文内多个传感器子采样去重。UI 刷新次数、SDL 轮询次数、`UPDATE_COMPLETE` 事件均不作为上传率。

SDL 未提供连续传感器报文的设备显示“上传速率 — Hz”，另列可观测的状态变化率；静止时可能为 0，不能据此推断硬件的 USB 轮询率。不同设备独立统计，断开显示 0，尚未收满一秒显示“—”。NS1 映射预览中的 BLE 速率不是虚拟输出速率。

## 验证

`tools/ControllerProtocolChecks` 检查六轴方向与物理单位、三个 IMU 槽位、SPI 校准、IMU 启停、断线归零、计数器更新、NS1 振动强度与左右分离、停振、截断报文、BLE 编码、速率去重与过期。端到端测试通过真实本地 TCP / USB/IP 把反馈送入 `VirtualControllerSession`，验证实体回传事件；不安装或挂载系统驱动。

```powershell
dotnet run --project tools/ControllerProtocolChecks
# 同时渲染 NS2 / Edge 测试图供检查
dotnet run --project tools/ControllerProtocolChecks -- --render artifacts/validation
```

主项目构建和自动检查不能替代实体 NS2 + 游戏实测。仍需在游戏内检查三轴方向、持续振动与停振。

协议依据：[SDL Switch 驱动](https://github.com/libsdl-org/SDL/blob/release-3.4.16/src/joystick/hidapi/SDL_hidapi_switch.c)、[NS1 震动幅度表](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/rumble_data_table.md)。本项目独立实现转换，未复制这些驱动的源代码。
