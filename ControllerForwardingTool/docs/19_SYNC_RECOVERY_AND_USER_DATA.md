# SYNC 连接恢复与 Windows 用户数据目录

> 历史记录（1.0.1 文档同步，2026-09-27）：正文保留当时的设计、功能状态与验证结果，不作为当前操作说明。当前键鼠 / 混合输入、全输入映射、NS2 USB 连接及默认配置见[当前文档目录](../../docs/README.md)和[1.0.1 更新日志](../../CHANGELOG.md)。

## 回归排查与修复

用户反馈新版长按 SYNC 后没有错误，但始终不连接。上一轮新增的广播主机地址比较和 `IsConnectable == false` 都会直接跳过自动连接，符合这种无错误的现象；尚未在用户硬件上确认是哪个字段触发。

- 移除候选自动连接与已记住地址探测中的附加元数据阻断。主机地址和可连接标志仅输出诊断；用户删除记录、关闭自动连接、已有连接和失败冷却规则仍生效。
- 新发现的设备恢复原有 60 秒整次连接预算，不再附加 25 秒发现超时。无广播的已记住地址探测恢复 8 秒发现预算，避免离线记录长时间占用连接流程。保留 Unreachable 的有限同会话重试。
- 附加广播属性读取失败时仍走基础名称/厂商字段发现，记录一次提示，不丢弃候选。服务发现诊断不再额外读取系统配对属性，避免诊断触发新的设备请求。
- 保留历史列表、删除和本地重命名。没有新增主机注册，也没有写入手柄配对信息。

## 用户数据

通过 `Environment.SpecialFolder.LocalApplicationData` 获取 Windows 用户已知目录，支持重定向用户配置文件，数据根目录为 `%LOCALAPPDATA%\ControllerForwardingTool`。不依赖 EXE 所在目录或启动快捷方式的工作目录。

| 内容 | 根目录下的位置 |
| --- | --- |
| 转发设置、校准、手柄历史与名称 | `bridge-settings.json` |
| 窗口位置 | `window-placement.json` |
| 自动运行日志 | `logs/app-*.log` |
| 新导出诊断 | `logs/diagnostics-*.txt` |
| 主进程和 VIIPER、USB/IP 后台进程的工作目录 | `runtime/` |

程序文件、DLL、资源和驱动安装器仍从安装目录只读加载。普通运行不需要提升权限。旧 `diagnostics/` 和旧产品目录内容保留，不自动删除或搬迁。

设置页可打开配置文件或日志目录。不存在的配置文件会先保存当前配置；已有文件不因打开而覆盖。配置手动编辑后需重新启动程序。配置写入采用同目录唯一临时文件再替换，避免多实例共用一个临时文件名。

运行日志从进程启动开始记录，包含程序路径、连接阶段、错误、退出和未处理异常。输入报文不按帧落盘。每条写完关闭文件，便于外部程序读取；单文件约 5 MB，最多保留 10 个生成的运行日志，手动导出不轮转删除。写入失败在设置页显示，不回退到安装目录，也不终止控制器输入。

微软依据：[Windows Known Folder IDs](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid)。

## 验证与实机边界

Release 构建通过，0 警告 / 0 错误。58 项无硬件检查通过，包含：不完整广播元数据仍允许连接、手动暂停与遗忘仍有效、已知目录不随工作目录改变、配置原子保存、运行中读取日志、轮转保留导出、重启保留旧日志、写入失败不影响输入且不回退工作目录。

```powershell
dotnet build ControllerForwardingTool.slnx -c Release --artifacts-path artifacts/sync-recovery-build -p:UsedAvaloniaProducts= -p:UseSharedCompilation=false -p:NuGetAudit=false --ignore-failed-sources
dotnet artifacts/sync-recovery-build/bin/ControllerForwardingTool.Checks/release/ControllerForwardingTool.Checks.dll
```

尚需用户设备验证 SYNC 实际连接及设置按钮打开外部编辑器/资源管理器。测试前从托盘退出旧版，再运行新版；开启自动连接并长按 SYNC。如果仍失败，设置页打开日志目录，最新 `app-*.log` 将保留具体阶段，无需手动导出才能取得日志。
