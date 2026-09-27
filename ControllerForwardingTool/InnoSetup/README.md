# ControllerForwardingTool 安装包

`ControllerForwardingTool.iss` 为手柄转发工具打包，首次安装提供安装范围选择：

| 安装范围 | 默认安装位置 | 安装权限 |
| --- | --- | --- |
| 仅为当前用户安装（默认） | `%LOCALAPPDATA%\Programs\ControllerForwardingTool` | 不请求管理员权限 |
| 为所有用户安装 | `C:\Program Files\ControllerForwardingTool` | 请求管理员权限 |

目录通过 `{autopf}` 按所选范围解析，可在向导中修改；开始菜单和桌面快捷方式也遵循所选范围。
目录页点击“下一步”时会实际尝试创建可写临时文件；目标目录不存在时，先验证目录能否创建。检查后关闭并自动删除临时文件，仅清理本次创建的空目录，不修改已有文件或权限。
无法写入时停留在目录页，提示更换可写目录；当前用户模式下还会提示重新选择“为所有用户安装”。检查不依赖 `Program Files` 等路径名单，所有用户模式也会检查实际权限。安装开始前再次检查，静默安装失败时记录原因并停止。
此检查验证目录的创建和写入能力；已有安装文件被占用、文件自身的权限限制、磁盘空间变化等仍由安装器在实际安装时处理。
升级时自动沿用已有安装范围和目录，不再显示范围选择，避免无意间创建另一份安装。
命令行支持 `/CURRENTUSER` 和 `/ALLUSERS` 指定范围。需要更换范围时，应先卸载旧范围的安装再重新安装；用户配置保留，旧全用户安装的卸载仍需要管理员权限。
目标为 Windows 11 x64 / .NET 10 自包含文件夹发布。
中文语言文件采用 [Inno Setup 源码仓库 is-6_7_3 的社区简体中文翻译](https://github.com/jrsoftware/issrc/blob/is-6_7_3/Files/Languages/Unofficial/ChineseSimplified.isl)，保留原作者说明。

## 附加任务

安装前的“选择附加任务”页参考提供的 SortingDebugServer 安装器，提供以下选项，并在升级时保留上次选择：

| 选项 | 首次默认 | 行为 |
| --- | --- | --- |
| 创建桌面快捷方式 | 勾选 | 随安装范围创建；升级取消时删除对应范围的快捷方式 |
| 尝试固定到任务栏 | 不勾选 | 以原登录用户发送 Windows Shell 请求；不保证成功，不支持时请手动固定 |
| 开机时自动启动 | 勾选 | 在对应范围的 Startup 文件夹创建 `手柄转发工具.lnk`，实际在登录 Windows 后启动 |
| 以管理员身份启动 | 不勾选 | 为当前安装路径设置 RUNASADMIN；取消或卸载时移除该标记，保留其他兼容性标记 |

当前用户安装使用 `{userstartup}`，所有用户安装使用 `{commonstartup}`（脚本统一使用 `{autostartup}`）。取消自启后重新安装会删除对应范围的启动快捷方式，卸载时也会移除。安装器不使用 Run 注册表项实现自启。

软件设置页也使用当前用户 Startup 快捷方式，并读取安装器创建的启动入口。保存设置时清理当前用户的两个旧 Run 项；已有所有用户入口时不再创建重复的当前用户入口。所有用户自启需要重新运行安装器取消，普通用户设置页不能关闭它。当前用户安装会清理该账户的旧 Run 项；全用户安装不会遍历或修改其他账户的注册表，其他账户可通过保存软件设置迁移旧入口。

Startup 自启无法保证启动需要提权的程序，因此不能同时选择自启和管理员启动；交互与静默安装均检查此组合。普通运行不需要管理员权限，驱动安装仍单独请求授权。

## 构建

先在 Visual Studio 使用 `ControllerForwardingTool.pubxml` 手动发布，目标目录为
`D:\Temp\Publish\ControllerForwardingTool\Release`（当前配置的 `PublishDir`），配置为 Release、win-x64、.NET 10 自包含文件夹发布。`PublishUrl` 仍为父目录；使用 IDE 时请核对实际生成位置，安装脚本默认读取 `Release` 子目录。
安装脚本直接读取该目录，不调用 `dotnet publish`，也不修改发布文件。

发布完成后，用 Inno Setup 6.5.0+ 打开 `ControllerForwardingTool.iss` 并点击“编译”，
无需设置额外参数。也可从 `D:\github\ControllerForwardingTool` 运行：

```powershell
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' '.\ControllerForwardingTool\InnoSetup\ControllerForwardingTool.iss'
```

脚本检查主程序、自包含运行时、SDL、libusb、VIIPER 和 USB/IP 安装器是否存在。
版本号读取主程序文件版本，输出为 `D:\Temp\Publish\ControllerForwardingTool\ControllerForwardingTool-Setup-<版本>-x64.exe`。

临时使用其他手动发布目录时，可通过 `/DMyPublishDir` 覆盖默认值：

```powershell
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' '/DMyPublishDir=D:\OtherPublish\ControllerForwardingTool' '.\ControllerForwardingTool\InnoSetup\ControllerForwardingTool.iss'
```

可通过 `/DMyOutputDir=输出目录` 覆盖安装包输出目录。
不要只打包 EXE：必须保留 DLL、drivers、licenses 和 THIRD_PARTY_NOTICES.md。
对外分发时沿用项目 `THIRD_PARTY_NOTICES.md` 所述的配套源码包方式；原有
`tools/package-release.ps1 -SelfContained` 可生成运行目录及对应源码包。

## 配置目录

程序使用 `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`
获取 Windows 当前用户的本机数据目录，不将可写配置放进 Program Files：

| 数据 | 位置 |
| --- | --- |
| 转发设置、摇杆和体感校准 | `%LOCALAPPDATA%\ControllerForwardingTool\bridge-settings.json` |
| 窗口位置 | `%LOCALAPPDATA%\ControllerForwardingTool\window-placement.json` |
| 自动保存的运行日志与新导出诊断 | `%LOCALAPPDATA%\ControllerForwardingTool\logs` |
| 应用及 VIIPER、USB/IP 子进程的运行工作目录 | `%LOCALAPPDATA%\ControllerForwardingTool\runtime` |

设置页提供「打开配置文件」「打开日志目录」。配置尚不存在时，打开操作先保存当前配置；已存在时不覆盖，使用系统关联程序打开，没有关联时尝试记事本。手动编辑前退出程序，下次启动读取变更。

运行日志按会话生成，单文件约 5 MB，最多保留 10 个 `app-*.log`；手动导出的 `diagnostics-*.txt` 不随轮转删除。旧版 `%LOCALAPPDATA%\ControllerForwardingTool\diagnostics` 目录保留，不自动搬迁。日志写入失败不改写安装目录，设置页显示错误。

首次加载且新配置不存在时，程序读取旧的 `%LOCALAPPDATA%\NS2ProWin11\bridge-settings.json`，
校验后导入新目录。旧文件保留；已有新文件优先；损坏的旧配置不会迁移。迁移写入失败时，
仍可在本次启动使用读到的旧设置，下次加载重试。旧诊断保留在旧目录，不自动移动。
新配置后续不再与旧版同步。窗口位置原本已使用新目录，保持兼容。

微软依据：[本机应用数据目录 API](https://learn.microsoft.com/en-us/dotnet/api/system.environment.specialfolder)
及 [Windows 已知目录](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid)。

仅为当前用户安装不请求提升权限；为所有用户安装时提升权限。普通运行不要求提升，安装器结束后的启动使用原登录用户。
安装器不初始化用户配置、不开放 Program Files 写权限，升级和卸载保留用户数据。
卸载仅移除安装文件及快捷方式，USB/IP 系统驱动仍由程序中的驱动面板单独管理。
升级或卸载前停止转发并从托盘退出程序。USB/IP 驱动安装、修复和卸载仍需要管理员权限，由程序中的驱动面板主动发起，不受软件安装范围影响。
