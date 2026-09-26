# 手柄转发工具 / Controller Forwarding Tool

Windows 11 / .NET 10 / Avalonia 桌面工具：NS2 Pro BLE 自动连接、Xbox / Switch Pro / PlayStation 等 Windows 手柄输入与测试、Nintendo / Xbox / PS5 / PS5 Edge 虚拟 USB 输出。

r7 起中文名称为「手柄转发工具」，英文名称为「Controller Forwarding Tool」，启动文件为 `ControllerForwardingTool.exe`。r8 将解决方案、项目目录、项目文件、命名空间和发布配置统一为 `ControllerForwardingTool`。请打开 `ControllerForwardingTool.slnx`，并将新版本解压到独立目录。设置、校准和窗口位置保存在 `%LOCALAPPDATA%\ControllerForwardingTool`；新配置不存在时自动从 `%LOCALAPPDATA%\NS2ProWin11\bridge-settings.json` 导入旧设置，保留旧文件且不覆盖已有新配置。如已启用开机启动，在新版设置页点击「保存软件设置」即可更新到新 EXE。

## 许可证

本项目原创代码采用 [MIT License](LICENSE)。任何人均可免费下载、使用、修改、合并、分发、再许可及商用（包括销售副本），无需通知作者或另行征得许可。复制或分发软件及其实质性部分时，须保留版权声明和许可声明；软件按原样提供，不作担保，完整条款以 `LICENSE` 为准。

版权署名为 `Copyright (c) 2026 Controller Forwarding Tool contributors`。第三方代码、库、驱动及素材继续遵循各自许可证，详见 [第三方声明](THIRD_PARTY_NOTICES.md)。构建和发布目录均附带 `LICENSE`。

## 使用

当前软件版本为 **1.0.0**。导航栏的「更新日志」页面列出当前版本的功能、改进与修复；「关于」页面同步显示版本号。
「关于」页面采用左侧 SVG 软件图标、右侧软件信息的布局，显示版本、编译时写入的构建时间（含本地时区）、实际 Avalonia / .NET 版本；源代码地址暂留空。下方提供开源组件和协议参考项目的仓库链接，官方项目图标随程序内嵌，离线可显示。图标来源见 `ControllerForwardingTool/Assets/OpenSource/SOURCES.md`。

2026-09-26 更新：修复测试图背键遮挡轮廓，增加运行模式提示、NS1 体感与震动回传，以及测试页 Hz 显示。测量方式、验证和实机验证边界见 [本次修复说明](ControllerForwardingTool/docs/16_NS1_MOTION_RUMBLE_AND_REPORT_RATE.md)。

1. 打开程序，在概览或虚拟手柄启动按钮旁点击「配置 USB/IP 驱动」。首次使用点击面板中的「安装 / 修复」完成系统向导，再刷新状态。
2. 在「虚拟手柄」选择输入类型：NS2 Pro 直连蓝牙，或 Windows 手柄。Windows 输入需先插入 USB / 连接系统已配对的蓝牙设备，再选择实体手柄。
3. 点击 **PS5 / NS2 PRO / XBOX / PS5 EDGE / NS1 PRO** 图片卡片选择输出身份，再启动虚拟 USB。使用 NS2 蓝牙输入时，首次长按顶部 SYNC；成功连接后程序会记住设备，后续可尝试按 L + R 唤醒回连，无响应时再长按 SYNC。使用 Windows 输入时转发所选设备。
4. 在「手柄测试」选择 Windows 手柄，检查真实的虚拟设备输入。NS1、Xbox、PlayStation 及通用 HID 可通过 USB 或已配对的系统蓝牙连接自动进入列表。
5. 开始游戏前切换到「概览」，释放 NS2 USB 测试接口。切换输出身份前先停止当前虚拟手柄。卸载 USB/IP 可从同页进入官方卸载向导。

手柄图按 Switch / Xbox / PlayStation 型号切换布局。提供陀螺仪状态与 3 秒静置校准；Windows / NS2 BLE 震动可设置时长和高、低频力度。浅色双栏工作台新增死区预览、摇杆覆盖与 RAW/CAL 读数。导航栏支持折叠。操作方法与 NS1 卡死修复说明见 [测试与校准](ControllerForwardingTool/docs/08_TESTER_CALIBRATION_AND_STABILITY.md)。

程序不会自动安装驱动。手动断开会暂停 NS2 自动连接；设置页可重新开启。输出设备只在本机 Windows 内有效，不代表可通过电脑 USB 接口连接 Switch 主机。

NS2 唤醒回连更新：保存成功连接设备的地址及地址类型，接受这些设备缺少名称/厂商字段的广播，并在扫描期间轮流尝试已记住地址。旧版本没有保存这项记录，升级后需先成功连接一次。此记录不等于主机配对/绑定；L + R 的实际无线效果仍需对应手柄和适配器验证。实现与实测步骤见 [回连说明](ControllerForwardingTool/docs/17_NS2_WAKE_RECONNECT.md)。

「蓝牙连接 → 已连接过的手柄」保存多个成功接入的 NS2 Pro，最近连接的排在前面。选中记录可删除；删除后不再自动连接该地址，当前连接保留。从上方扫描列表手动连接成功后会重新记住。上一版单设备记录自动迁入列表。

候选设备可填写本地名称并点击「保存名称」，留空保存恢复原名。名称最多 40 字符，重启后保留，并同步显示在历史列表。广播目标和可连接标志仅用于诊断，不再拦截 SYNC 连接。完整主机注册仍待授权实现，普通唤醒失败时需长按 SYNC。详见 [诊断及注册方案](ControllerForwardingTool/docs/18_NS2_HOST_REGISTRATION_PROPOSAL.md)。

设置页新增「打开配置文件」「打开日志目录」。配置、窗口位置、校准与手柄记录仍在 `%LOCALAPPDATA%\ControllerForwardingTool`；运行日志及新导出文件存入其 `logs` 子目录，程序和后台进程工作目录使用 `runtime` 子目录，不向 Program Files 写入运行时数据。运行日志自动轮转，单文件约 5 MB，最多保留 10 个；旧 `diagnostics` 目录中的导出保留。见 [SYNC 恢复与用户数据目录](ControllerForwardingTool/docs/19_SYNC_RECOVERY_AND_USER_DATA.md)。

设置页集中管理登录 Windows 后自启、启动时恢复已保存的虚拟手柄模式、启动后收起到托盘、最小化到托盘及关闭窗口后保留后台运行，点击「保存软件设置」持久化。自启通过 Startup 快捷方式实现，状态读取实际启动入口；保存时迁移当前用户的旧 Run 项，所有用户入口由安装器管理。全新便携版默认仅开启关闭窗口后后台运行；安装版附加任务默认勾选桌面快捷方式和开机自启，还可选择尝试固定任务栏或管理员启动，自启与管理员启动不能同时选择。托盘菜单可打开窗口或退出。软件设置独立保存，不提交虚拟手柄页未保存的参数。连接行为开关仍只作用于本次运行。

PS5、PS5 Edge、Xbox、NS1 Pro、NS2 Pro 五种输出各有独立线路配置：输入类型、NS2 按键映射、震动倍率、推送频率、死区及校准开关、PS5 体感方向与倍率、音频保护和端口。选择输出卡片后编辑，点击「应用并保存此线路」；启动输出也会保存当前线路。按键映射可以在输出未启动时编辑，其单独保存及恢复默认仅影响当前线路的映射。切换输出卡片保留本次未保存的草稿，退出后仅保留已保存内容。旧版共用参数自动复制到五条线路，后续修改相互独立。实体设备校准数据、陀螺仪基础校准及软件启动设置仍共用；自定义按键映射当前仍适用于 NS2 输入。

虚拟页提供震动倍率、推送频率、摇杆中心/行程校准、PS5 体感方向、音频保护、端口配置及中文诊断说明；配置可保存，日志可导出。操作及模式能力边界见 [r3 使用说明](ControllerForwardingTool/docs/09_ARENA_AND_CONTROLLER_LAB.md)。USB/IP 未安装或缺少原厂卸载器时禁用卸载。

r4 修复 LT/RT 线性行程显示和原始读数条溢出；支持百分比填充及独立扳机读数。概览改为纵向四步流程，驱动配置无需滚动查找。详见 [r4 显示与流程修复](ControllerForwardingTool/docs/10_TRIGGER_READOUT_AND_OVERVIEW.md)。

r5 按 Steam 截图调整手柄外壳和按键比例，确保十字键与右摇杆位于壳体内。体感预览改为融合角速度与重力的三轴姿态；在「手柄测试 → 校准与高级设置」提供手动静置校准、自动漂移校准、阈值调节、细微动作平滑与姿态回正。详见 [r5 体感与发布说明](ControllerForwardingTool/docs/11_MOTION_CALIBRATION_AND_PUBLISH.md)。

r6 修复 NS2 BLE 测试图 Y 轴上下颠倒，新增 Windows 手柄作为虚拟输入，可选择 Xbox → NS1、NS1 → NS2 等组合。模拟扳机在 Xbox / PS5 输出保留行程，Nintendo 输出转为数字按键；缺少的体感、背键等保持零输入，本版未增加键盘替代键。详见 [多类型输入与验证边界](ControllerForwardingTool/docs/12_MULTI_CONTROLLER_INPUT.md)、[Xbox 通信协议](ControllerForwardingTool/docs/13_XBOX_CONTROLLER_PROTOCOL.md)、[PS5 通信协议](ControllerForwardingTool/docs/14_PS5_CONTROLLER_PROTOCOL.md)。

重复启动时，同一程序目录的实例通过命名管道唤醒已有主窗口（包括托盘隐藏或最小化状态），新进程收到确认后正常退出；不同目录的副本可以分别启动。启动到托盘期间收到的唤醒请求也会保留窗口可见。通信失败或超时则继续正常启动。无硬件跨进程检查：`dotnet run --project tools/SingleInstanceChecks -c Release`。

## 构建

安装版首次安装可选择范围：默认仅为当前用户安装到 `%LOCALAPPDATA%\Programs\ControllerForwardingTool`，不请求管理员权限；选择为所有用户安装时使用 `C:\Program Files\ControllerForwardingTool` 并请求管理员权限。升级沿用已有安装范围，USB/IP 驱动安装仍需管理员权限。安装脚本、配置目录和编译方法见 [InnoSetup/README.md](ControllerForwardingTool/InnoSetup/README.md)。

```powershell
dotnet build ControllerForwardingTool.slnx
dotnet publish ControllerForwardingTool/ControllerForwardingTool.csproj -c Release -r win-x64 --self-contained false -o artifacts/Publish/ControllerForwardingTool
# 使用 Visual Studio 同款发布配置（默认自包含，输出到 D:\Temp\Publish\ControllerForwardingTool）
dotnet publish ControllerForwardingTool/ControllerForwardingTool.csproj -c Release -p:PublishProfile=ControllerForwardingTool
# 新建干净发布目录，生成运行包与配套第三方源码包（版本名不能复用）
./tools/package-release.ps1 -Version 20260926-r8
# 无需预装 .NET 的独立版本，使用新的版本名
./tools/package-release.ps1 -Version 20260926-r8-self-contained -SelfContained
```

打包脚本的发布目录为 `artifacts/Publish/<版本>/ControllerForwardingTool`，其中运行 `ControllerForwardingTool.exe`。`ControllerForwardingTool.Checks` 提供本次回连逻辑的无硬件回归检查，可用 `dotnet run --project ControllerForwardingTool.Checks -c Release` 运行；历史文档中的 `NS2ProWin11.Checks` 工程已不在当前目录，保留其旧版验证记录。

默认发布版需要 Windows 11 x64 和 .NET 10 运行时；`-SelfContained` 版本内置 .NET。请完整保留运行包中的 DLL、drivers、licenses 和 THIRD_PARTY_NOTICES.md。r5 起开发文档、README、PDB 和第三方源码压缩包不再复制进运行目录；r7 运行包名为 `ControllerForwardingTool-win-x64-*.zip`，配套源码包为 `ControllerForwardingTool-third-party-sources-*.zip`，请一起提供。来源见 [许可清单](THIRD_PARTY_NOTICES.md)。Visual Studio 发布时请选择新的空目录；旧发布目录里的历史文件不会被构建配置主动删除。

当前实现、测试证据及限制见 [实现状态](ControllerForwardingTool/docs/06_PROTOTYPE_STATUS.md)；详细操作见 [连接与虚拟 USB](ControllerForwardingTool/docs/07_CONNECTION_AND_VIRTUAL_USB.md)。参考开源工程是 [XinHeLianSheng-Pro2-Bridge](https://github.com/LeonChrome/XinHeLianSheng-Pro2-Bridge)。
