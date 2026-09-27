# 手柄转发工具 / Controller Forwarding Tool

Windows 11 x64 桌面手柄转发与测试工具。读取 **NS2 Pro 直连 BLE** 或 **Windows 已连接的标准映射手柄**，在本机创建 NS2 Pro、NS1 Pro、Xbox 360、PS5 或 PS5 Edge 虚拟 USB 手柄，并将游戏震动反馈返回输入设备。

[源代码](https://github.com/XingYu-999/ControllerForwardingTool) · [文档目录](docs/README.md) · [使用与排障](docs/02_USER_GUIDE.md) · [构建与验证](docs/04_BUILD_AND_VALIDATION.md)

当前项目版本为 **1.0.0**。技术栈为 .NET 10、Avalonia 12.1.2、CommunityToolkit.Mvvm、SDL 3.4.16、libusb 1.0.30、VIIPER haptic 后端与 usbip-win2 0.9.7.7。版本来自仓库配置及随附组件，不表示上游最新版本。

## 文档快速入口

| 内容 | 快速入口 |
| --- | --- |
| 开发与使用 | [文档总目录](docs/README.md) · [架构与数据流](docs/01_ARCHITECTURE.md) · [使用与排障](docs/02_USER_GUIDE.md) |
| 配置与发布 | [配置与生命周期](docs/03_CONFIGURATION_AND_LIFECYCLE.md) · [构建与验证](docs/04_BUILD_AND_VALIDATION.md) · [安装包制作](ControllerForwardingTool/InnoSetup/README.md) |
| 实现记录 | [设备发现与持续振动](docs/28_TESTER_DISCOVERY_AND_SUSTAINED_RUMBLE.md) · [历史文档索引](ControllerForwardingTool/docs/README.md) |
| 许可与素材 | [项目许可证](LICENSE) · [第三方声明](THIRD_PARTY_NOTICES.md) · [开源图标来源](ControllerForwardingTool/Assets/OpenSource/SOURCES.md) · [手柄图片来源](ControllerForwardingTool/docs/15_CONTROLLER_PRODUCT_IMAGES.md) |

## 手柄通信协议快速入口

协议资料统一放在 [`docs/protocols/`](docs/protocols/README.md)，区分实体手柄、应用内部消息和 USB/IP 传输。

| 协议 | 快速入口 |
| --- | --- |
| Nintendo | [NS2 Pro：BLE、输入、注册与震动](docs/protocols/NS2_PRO.md) · [NS1 Pro：USB/HID、IMU、SPI 与子命令](docs/protocols/NS1_PRO.md) |
| Xbox / PlayStation | [Xbox](docs/protocols/XBOX.md) · [PS5 / PS5 Edge](docs/protocols/PS5.md) |
| 软件传输层 | [Windows / SDL 输入](docs/protocols/SDL_INPUT.md) · [VIIPER 内部通信](docs/protocols/VIIPER.md) · [USB/IP](docs/protocols/USBIP.md) |
| 核对与资料 | [完整性及代码一致性报告](docs/protocols/COVERAGE.md) · [原始研究资料](docs/protocols/README.md#原始研究资料) |

当前文档覆盖主要实现路径，已修正“NS1 不支持体感”、NS2 初始化条数等过时说明；未实现功能和未知协议字段见核对报告，不代表完整厂商协议或所有固件兼容。

## 功能与边界

- **输入与连接**：NS2 Pro BLE 扫描、自动连接、设备历史、本地名称、断线恢复，以及默认关闭的 SYNC 主机注册；Windows 手柄通过 SDL 接入 USB 或已由系统配对的蓝牙连接。
- **五种输出**：每种身份独立保存输入类型、NS2 按键映射、震动倍率、推送频率、死区、校准开关、PS5 体感参数和端口。同一应用实例运行一条输出线路。
- **测试与校准**：设备卡片、按键/摇杆/扳机读数、可用电量与体感、震动测试、静置陀螺仪校准、姿态预览及 NS2 BLE 摇杆校准。
- **运行管理**：配置与日志入口、诊断导出、驱动管理、自启与托盘设置、窗口位置恢复，以及同一程序目录的重复启动唤醒。

| 输出卡片 | 实际输出身份 | 后端 | 主要能力 |
| --- | --- | --- | --- |
| NS2 PRO | `057E:2069` | VIIPER `ns2pro` | 按键、摇杆、IMU、C/GL/GR、原生震动反馈 |
| NS1 PRO | `057E:2009` | C# USB/IP + HID 应答 | 按键、摇杆、IMU、NS1 震动转码 |
| XBOX | `045E:028E`（Xbox 360） | VIIPER `xbox360` | 按键、摇杆、模拟扳机、双马达 |
| PS5 | `054C:0CE6` | VIIPER `dualsensehaptic` | 按键、摇杆、模拟扳机、体感、普通/HD 震动处理 |
| PS5 EDGE | `054C:0DF2` | VIIPER `dualsenseedge` | 按键、摇杆、模拟扳机、体感、背键、普通震动 |

能力取决于输入设备和游戏：缺少的体感、背键等不会自动补出；Nintendo 输出把模拟扳机转换为数字按键。未知 HID 可用于原始读数测试，转发要求 SDL 标准映射。自定义按键映射目前面向 NS2 输入。

虚拟 USB 只呈现在本机 Windows 中，不能据此把电脑 USB 接口当作 Switch 主机的实体手柄。软件推送频率不等于物理 BLE 报告率或游戏实际接收率；兼容性与震感仍需具体设备和游戏验证。

## 快速开始

1. 完整解压运行包，启动 `ControllerForwardingTool.exe`。框架依赖版需要 .NET 10 运行时；自包含版已附带运行时。
2. 需要虚拟输出时，在概览或虚拟手柄页打开「配置 USB/IP 驱动」，手动执行「安装 / 修复」并刷新状态。只读取、测试实体输入无需该虚拟驱动。
3. 在「虚拟手柄」选择输出卡片和输入类型。NS2 BLE 首次连接长按顶部 SYNC；Windows 手柄先接入 USB 或完成系统蓝牙配对，再手动选择输入设备。
4. 调整参数，点击「应用并保存此线路」，然后启动虚拟 USB。NS2 BLE 模式允许先启动输出等待输入；Windows 模式需选定就绪的输入手柄。
5. 在「手柄测试」上方选择 Windows 已枚举的设备卡片，检查实际输入。本应用输出标为「模拟手柄 · 本应用」；下方默认折叠的「模拟手柄」区仅是已保存映射的参考预览。
6. 切换输出身份前停止当前输出。默认关闭窗口仍在托盘运行，需要彻底结束时使用托盘「退出」。

已连接过的 NS2 可尝试 L + R 唤醒，无响应时重新长按 SYNC。需要写入电脑主机信息时，在连接前主动开启「将电脑注册到手柄（默认关闭）」；只在新鲜 SYNC 配对广播对应的连接中执行注册。关闭该开关不会清除手柄已有注册。具体流程见[使用指南](docs/02_USER_GUIDE.md)。

## 架构与目录

```text
NS2 BLE / Windows SDL → ControllerInputBridge → ControllerState
                                                │
                         ┌──────────────────────┴─────────────────────┐
                         ↓                                            ↓
                C# NS1 USB/IP 服务                       VIIPER 进程（其余四种输出）
                         └──────────────────────┬─────────────────────┘
                                                ↓
                                    usbip-win2 → Windows / 游戏
                                                │
                                  震动反馈 → BLE / SDL 输入设备
```

| 路径 | 责任 |
| --- | --- |
| `ControllerForwardingTool/Core/` | 统一状态、用户数据、日志、单实例、窗口和自启管理 |
| `ControllerForwardingTool/Bluetooth/` | WinRT BLE 扫描、GATT、初始化、主机注册和震动写回 |
| `ControllerForwardingTool/Input/` | SDL 专用线程、输入桥、映射、校准、测试分析和输入回环过滤 |
| `ControllerForwardingTool/Protocol/` | NS2 FD2 解码、电量解析与 NS1 报告编码 |
| `ControllerForwardingTool/VirtualDevice/` | 线路配置、输出会话、USB/IP、VIIPER 协议及反馈转换 |
| `ControllerForwardingTool/ViewModels/`、`Views/` | MVVM 页面、状态编排与自绘测试控件 |
| `drivers/` | 随附本机库、后端、驱动安装器、许可证和配套源码包 |
| `tools/` | 检查工程、SDL 构建与发布打包脚本 |
| `docs/` | 当前实现文档；早期方案与迭代记录保留在 `ControllerForwardingTool/docs/` |

详细线程模型、数据流、反馈生命周期与实现入口见[架构说明](docs/01_ARCHITECTURE.md)。

## 配置与数据

数据保存在 `%LOCALAPPDATA%\ControllerForwardingTool`：`bridge-settings.json` 保存线路和全局设置，`window-placement.json` 保存窗口位置，`logs/` 保存运行日志与诊断导出，`runtime/` 用作运行工作目录。新配置不存在时可导入旧 `%LOCALAPPDATA%\NS2ProWin11\bridge-settings.json`，保留旧文件。

各输出卡片的未保存草稿只在本次运行中保留；「保存软件设置」独立保存自启和托盘选项。Windows 输入设备选择不会按设备 ID 持久化。默认关闭窗口后继续后台运行，其他软件启动选项默认关闭；安装版首次安装默认勾选开机自启。字段、默认值及迁移规则见[配置与生命周期](docs/03_CONFIGURATION_AND_LIFECYCLE.md)。

## 构建与检查

在 Windows 11 x64 上安装 .NET 10 SDK，从仓库根目录执行：

```powershell
dotnet build ControllerForwardingTool.slnx -c Release
dotnet run --project tools/ControllerProtocolChecks -c Release
dotnet run --project tools/SettingsPathChecks -c Release
dotnet run --project tools/SingleInstanceChecks -c Release
```

解决方案只包含主应用，检查工程需单独运行。窗口检查、可选手柄图渲染及验证范围见[构建与验证](docs/04_BUILD_AND_VALIDATION.md)。历史文档中的 `ControllerForwardingTool.Checks` 是部分检查工程的程序集名，仓库根目录不存在同名工程。

```powershell
# 框架依赖发布
dotnet publish ControllerForwardingTool/ControllerForwardingTool.csproj -c Release -r win-x64 --self-contained false -o artifacts/Publish/local/ControllerForwardingTool
# 打包自包含运行包和配套第三方源码包；每次使用未占用的版本标签
./tools/package-release.ps1 -Version 1.0.0-local -SelfContained
```

打包脚本输出 `artifacts/ControllerForwardingTool-win-x64-<标签>.zip` 和 `artifacts/ControllerForwardingTool-third-party-sources-<标签>.zip`；标签不修改程序集版本。当前发布配置的 `PublishDir` 为 `D:\Temp\Publish\ControllerForwardingTool\Release`，`PublishUrl` 为其父目录；CLI 与安装器默认载荷使用 `Release` 子目录。安装版制作、安装范围和自启任务见 [Inno Setup 说明](ControllerForwardingTool/InnoSetup/README.md)。

## 许可证与来源

项目许可证见 [LICENSE](LICENSE)。第三方代码、库、驱动与素材的来源、许可和分发文件见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。发布时保留随附许可证，并提供对应的第三方源码包。

关于页提供本仓库及参考项目链接，显示应用版本、编译时写入的构建时间和实际框架版本。协议研究与历次验证记录见[历史文档索引](ControllerForwardingTool/docs/README.md)；历史记录不代表本次检出的实机验收结果。
