# 构建、发布与验证

核对日期：2026-09-27。以下命令从仓库根目录运行。主解决方案是 `ControllerForwardingTool.slnx`，仅包含桌面应用；`tools/` 中的检查项目需单独执行。

## 1. 构建环境

- Windows 11 x64、.NET 10 SDK，使用支持 `.slnx` 的 IDE 或直接使用 `dotnet`。
- 主工程目标框架为 `net10.0-windows10.0.22621.0`，最低平台声明 `10.0.22000.0`，`PlatformTarget=x64`。
- NuGet 引用固定为 Avalonia 12.1.2、CommunityToolkit.Mvvm 8.4.2、Svg.Controls.Skia.Avalonia 12.0.0 等，实际列表以 [csproj](../ControllerForwardingTool/ControllerForwardingTool.csproj) 为准。
- 仓库随附 SDL 3.4.16、libusb 1.0.30、VIIPER haptic 后端和 usbip-win2 0.9.7.7 安装器。构建不安装系统驱动。

```powershell
dotnet restore ControllerForwardingTool.slnx
dotnet build ControllerForwardingTool.slnx -c Release --no-restore
```

需要运行开发版时执行 `dotnet run --project ControllerForwardingTool -c Release`，这会启动实际桌面应用并按用户配置执行扫描/恢复行为，不属于隔离测试。

## 2. 现有检查工程

| 工程 | 覆盖范围 | 运行条件/副作用 |
| --- | --- | --- |
| [ControllerProtocolChecks](../tools/ControllerProtocolChecks/Program.cs) | NS1 报告与 IMU、SPI、震动转码、速率去重、NS1 本地 USB/IP 输入及反馈 | 合成状态与本机 TCP 回环，不挂载 Windows 驱动，不连接实体手柄 |
| [SettingsPathChecks](../tools/SettingsPathChecks/Program.cs) | 数据目录、旧设置迁移、已有新配置优先、损坏数据、迁移失败 | 使用隔离临时路径，不读写真实用户配置 |
| [SingleInstanceChecks](../tools/SingleInstanceChecks/Program.cs) | 目录身份、跨进程唤醒、并发启动、ACK/超时和重启 | 启动检查子进程，使用独立命名管道标识，不运行主应用 |
| [WindowPlacementChecks](../tools/WindowPlacementChecks/Program.cs) | 窗口恢复、隐藏/重开、最小化退出与屏幕边界 | 需要 Windows 桌面，会短暂显示测试窗口；使用临时配置，不创建主 ViewModel 或扫描 BLE |

```powershell
dotnet run --project tools/ControllerProtocolChecks -c Release
dotnet run --project tools/SettingsPathChecks -c Release
dotnet run --project tools/SingleInstanceChecks -c Release
# 需要交互桌面，会显示测试窗口
dotnet run --project tools/WindowPlacementChecks -c Release
# 可选：协议检查后渲染手柄控件图片
dotnet run --project tools/ControllerProtocolChecks -c Release -- --render artifacts/controller-diagrams
```

这几个程序通过异常/退出码报告失败，不是解决方案内由 `dotnet test` 自动发现的测试套件。部分项目的程序集名为 `ControllerForwardingTool.Checks`，应传入实际工程路径，不能运行不存在的根目录 `ControllerForwardingTool.Checks` 工程。

历史文档提到的 112/115/161 等检查数量、`--render-routes` 等参数及旧 `NS2ProWin11.Checks` 属于当时记录；当前 `ControllerProtocolChecks` 的可选参数只有 `--render <目录>`。现存检查没有覆盖所有 BLE 注册、线路草稿和持续震动场景，不能把历史通过记录当作可重现的完整回归集。

## 3. 发布与打包

```powershell
# 框架依赖版，目标机器需要 .NET 10 运行时
dotnet publish ControllerForwardingTool/ControllerForwardingTool.csproj -c Release -r win-x64 --self-contained false -o artifacts/Publish/local/ControllerForwardingTool
# 自包含版，选择新的空输出目录
dotnet publish ControllerForwardingTool/ControllerForwardingTool.csproj -c Release -r win-x64 --self-contained true -o artifacts/Publish/local-self-contained/ControllerForwardingTool
# 使用仓库发布配置
dotnet publish ControllerForwardingTool/ControllerForwardingTool.csproj -c Release -p:PublishProfile=ControllerForwardingTool
```

当前 [pubxml](../ControllerForwardingTool/Properties/PublishProfiles/ControllerForwardingTool.pubxml) 的 `PublishDir` 为 `D:\Temp\Publish\ControllerForwardingTool\Release`，`PublishUrl` 为其父目录。CLI 发布使用 `PublishDir`；IDE 使用时应核对实际生成位置。Inno Setup 脚本默认读取 `Release` 子目录，避免只按旧说明中的父目录查找 EXE。

```powershell
# 同时生成运行 ZIP 与第三方源码 ZIP；标签不能复用
./tools/package-release.ps1 -Version 1.0.0-local -SelfContained
```

[package-release.ps1](../tools/package-release.ps1) 的产物：

| 路径 | 用途 |
| --- | --- |
| `artifacts/build-<标签>/` | 本次发布的独立构建产物 |
| `artifacts/Publish/<标签>/ControllerForwardingTool/` | 完整运行目录 |
| `artifacts/ControllerForwardingTool-win-x64-<标签>.zip` | 运行包 |
| `artifacts/ControllerForwardingTool-third-party-sources-<标签>.zip` | 第三方源码、构建脚本和许可证包，不是本项目完整源码快照 |

任一目标已存在时脚本拒绝覆盖，使用新标签重试。`-Version` 只是文件/目录标签，不改变 csproj 中的 `1.0.0`。省略 `-SelfContained` 生成框架依赖包。脚本最后输出两个 ZIP 的 SHA-256。

发布保留 `LICENSE`、`THIRD_PARTY_NOTICES.md`、本机 DLL、`licenses`、VIIPER 和 USB/IP 安装器。运行目录不包含开发 docs、README、PDB 和第三方源码压缩包；源码通过配套 ZIP 提供。直接 `dotnet publish` 不会清理旧文件，使用空目录，避免遗留历史 EXE 或文档。

安装包使用 [Inno Setup 说明](../ControllerForwardingTool/InnoSetup/README.md)，要求自包含载荷，安装脚本本身不执行 `dotnet publish`。通过 `/DMyPublishDir` 可指定其他已发布目录。SDL 重建入口为 [build-sdl.ps1](../tools/build-sdl.ps1)；普通文档或 C# 修改无需重建第三方库。

## 4. 实机验收范围

自动构建只证明 C#/XAML 和引用能编译；协议检查证明指定合成输入下的编码与本机服务行为。以下场景必须单独在实际设备上验收，并记录系统、适配器、固件、输入/输出模式与日志：

1. NS2 SYNC 首次连接、默认关闭注册、主动开启注册后的最终确认、普通唤醒及重启后回连。
2. Windows 输入热插拔，同型号实体设备与本应用输出共存、输入断开归零和防止转发回环。
3. 五种输出分别挂载，在上方已枚举设备卡回读按键、摇杆、扳机和支持的传感器；不能用映射参考图替代回读。
4. 游戏发出持续震动、强度更新和停止，停止会话/断开后马达清零；HD 流与普通马达分别检查。
5. 线路切换、单独保存映射、未保存草稿、保存失败和重启后的配置恢复。
6. 同目录重复启动、托盘恢复、关闭/退出、音频保护，以及安装版的当前用户/所有用户自启入口。

本次文档与关于页调整不包含重新安装驱动、写入手柄注册或真实游戏兼容性验收。验证结果应注明实际执行的命令和限制，保留历史观察的原始上下文。

## 5. 本次核对结果（2026-09-27）

- `dotnet build ControllerForwardingTool.slnx -c Release --no-restore`：通过，0 警告、0 错误，包含关于页源码链接的 XAML 编译。
- `ControllerProtocolChecks`：34 项协议、反馈与报告率检查通过。
- `SettingsPathChecks`：配置路径与迁移检查全部通过。
- `SingleInstanceChecks`：跨进程唤醒、并发启动、重启和超时回退检查全部通过。
- 本次未运行 `WindowPlacementChecks`、实际浏览器点击、BLE/游戏实机验收或安装器编译。

首次沙箱构建因 Avalonia 构建任务无法访问本机许可缓存目录而失败；允许构建进程访问后重跑通过，未修改依赖或项目编译设置。
