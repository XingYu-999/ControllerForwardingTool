# USB/IP：NS1 内置服务的传输子集

[协议目录](README.md) · [NS1 HID](NS1_PRO.md) · [VIIPER](VIIPER.md)

核对日期：2026-09-27。实现：[UsbIpPrototypeServer](../../ControllerForwardingTool/VirtualDevice/UsbIpPrototypeServer.cs)、[UsbIpNs1Device](../../ControllerForwardingTool/VirtualDevice/UsbIpNs1Device.cs)、[UsbIpClient](../../ControllerForwardingTool/VirtualDevice/UsbIpClient.cs)。以下描述内置 NS1 服务，不宣称复现 VIIPER 后端内部的全部 USB/IP 行为。

## 层次与字节序

Windows 的 usbip-win2 客户端将本机 TCP 服务导入为虚拟 USB。USB/IP 头采用**大端**；嵌入的 USB setup 字段和 HID 报告采用各自的小端约定。将两层字段直接按同一字节序解释会出错。

服务只监听 `127.0.0.1`；单设备 bus ID `1-1`，最多一个成功导入会话。构造函数默认端口 3240，但应用会话一般传入自动空闲端口或用户设置，不能假定实际总是 3240。

## 列举与导入

首部 8 字节：u16 版本 `0111`、u16 操作码、u32 状态。服务器检查版本与操作码。

| 请求 | 回复 | 当前行为 |
| --- | --- | --- |
| `8005` DEVLIST | `0005` | 8 字节首部 + u32 设备数 1 + 312 字节设备记录 + 4 字节 HID 接口记录 |
| `8003` IMPORT + 32 字节 ASCII bus ID | `0003` | 成功返回首部 + 312 字节设备记录，再进入传输循环；ID 错误/已被导入时 status=1 |

312 字节设备记录依次为 path[256]、busid[32]、u32 busnum/devnum/speed、u16 VID/PID/bcdDevice，以及 6 个单字节设备类/配置/接口计数。当前 path=`/virtual/ns1-pro`，bus/dev=1，speed=2（full speed），VID/PID=`057E:2009`，一个配置、一个 HID 接口。

## 导入后的 48 字节头

| 偏移 | 字段与当前解析 |
| --- | --- |
| 0 | u32 command：1 SUBMIT、2 UNLINK；回复 3 RET_SUBMIT、4 RET_UNLINK |
| 4 | u32 sequence，回复回显 |
| 8 | devid 字段，当前服务器未严格核验 |
| 12 | direction：0 OUT、1 IN |
| 16 | endpoint：当前处理 0 控制、1 中断 |
| 20 | transfer flags，当前未完整解释 |
| 24 | SUBMIT 的 i32 传输长度；UNLINK 的目标请求 sequence |
| 28–39 | 其他传输字段，当前无完整等时/分片语义 |
| 40–47 | USB setup：request type、request、value/index/length（后面三项为 u16 LE） |

SUBMIT 长度必须为 0–65536；OUT 先读完负载。端点 0 调 `HandleControl`；端点 1 OUT 调 `HandleInterruptOut`；端点 1 IN 等待新状态/应答。返回 RET_SUBMIT 头中 `[24]` 为 actual_length，IN 后接实际数据，OUT 不附数据；`[32]` 写 `FFFFFFFF`。未处理端点会返回零数据，而非完整 USB 错误状态。

## 并发、取消与节奏

挂起 IN 单独执行，接收循环继续处理控制/OUT/UNLINK；写回应答串行化，避免 TCP 帧交错。最多 64 个待完成 IN，重复 sequence 或超限结束连接。UNLINK 查找并取消对应待完成 IN，等其结束后回 RET_UNLINK；已不存在的目标也回当前实现的零状态。

NS1 输入只保留最新源状态；子命令应答优先。默认跟随源帧，固定频率或目标参考值按配置执行。源超时 250 ms 后中立，跟随源模式下约 100 ms 中立保活。断开时取消挂起请求，释放导入标志，允许重新导入。

## 实现边界

当前没有通用多设备导出、远程公开服务、完整等时传输、任意 USB 类支持、全字段合法性检查或完整错误码/STALL 模型。其他四种输出由 VIIPER 服务生成设备，应用通过同一个 USB/IP 客户端进行挂载；本机服务能返回合成报告不等于已验证 Windows 驱动和游戏。

源码内 `ControllerProtocolChecks` 包含 NS1 后端 TCP 输入/反馈回环，未挂载驱动；测试覆盖的具体范围见[核对报告](COVERAGE.md)。
