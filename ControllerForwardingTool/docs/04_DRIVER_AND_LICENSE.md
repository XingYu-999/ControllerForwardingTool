# 驱动、运行库与许可（2026-09-26）

1.0.1 文档同步（2026-09-27）：随附组件版本沿用下表；当前发布和数据保留规则见[构建与验证](../../docs/04_BUILD_AND_VALIDATION.md)，版本变化见[更新日志](../../CHANGELOG.md)。下文开发验证保留原始日期背景。

## 运行所需组件

| 组件 | 用途 | 分发位置 |
| --- | --- | --- |
| usbip-win2 0.9.7.7 x64 | Windows 虚拟 USB 内核驱动与 attach/detach 客户端 | `drivers/usbip-win2/v0.9.7.7/` |
| VIIPER haptic v0.8.0 参考构建 | NS2 Pro、Xbox 360、PS5 / Edge 虚拟 USB 用户态后端；不是内核驱动 | `drivers/viiper/` |
| SDL 3.4.16 定制 x64 构建 | NS1、NS2 USB、Xbox、PlayStation、通用 HID 检测 | 程序同目录 `SDL3.dll` |
| libusb 1.0.30 MinGW64 | SDL 的 NS2 USB 初始化及本应用手动注册管理接口 | 程序同目录 `libusb-1.0.dll` |

虚拟 NS1 的 USB/IP 服务由本项目 C# 实现，不启动 VIIPER。BLE 输入使用 Windows Runtime API，不要求安装额外蓝牙驱动。

## 驱动状态与操作

「虚拟手柄」页分别显示随附安装器、`usbip2_ude` 服务注册、usbip.exe 客户端版本。**已注册不等于当前驱动运行正常**；实际 attach 成功与 Windows/游戏识别另外验证。

安装/修复入口检查随附 EXE 的 SHA-256 和嵌入证书主体 `Cloudyne Systems`，随后请求 Windows UAC 并打开原厂向导。证书提取本身不是完整 Authenticode 信任链验证；文件来源由固定哈希约束。安装器 SHA-256：

```text
51620fa5f9f8be5932bc9d786deee557ce06d5407a99cab490dcfac71f185fea
```

卸载入口先停止本应用的虚拟设备，再打开安装目录的 `unins000.exe`。向导和 UAC 由用户操作，结果和重启要求显示在页面。卸载驱动会影响其他依赖 USB/IP 的软件。程序不会静默安装、更改签名策略或卸载其他软件。

本次开发验证使用电脑已有驱动，没有执行安装器或卸载器。

## 来源、许可证与源码

- usbip-win2：BSD-2-Clause，保留随附 `LICENSE.txt`。安装器来自参考桥接仓库，来源项目为 [usbip-win2](https://github.com/vadimgrn/usbip-win2)。
- VIIPER：GPL-3.0，二进制取自参考仓库 `source/final-three-in-one/tools/viiper/haptic-v0.8.0/viiper-haptic.exe`。随附许可证以及该仓库 `haptic-src` 源码快照；未宣称发布二进制已实现逐字节可复现构建。
- `ViiperProtocolClient.cs`：从桥接项目适配，保留来源说明和 Apache-2.0 文本。
- SDL：zlib 许可。应用附带的 DLL 是从 SDL 3.4.16 源码编译的定制版本，开启 `SDL_HIDAPI_LIBUSB`，不能替换为缺少此功能的通用 DLL 后仍预期 NS2 USB 可用。源码归档和 `tools/build-sdl.ps1` 随仓库保留。
- libusb：LGPL-2.1-or-later，使用可替换的动态 DLL，并随包提供许可证和对应源码归档。

完整清单、哈希和构建说明见解决方案根目录 [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md)。当前 `dotnet publish` 保留驱动包、运行库和许可；第三方源码归档由 `tools/package-release.ps1` 生成配套源码 ZIP，不放入运行目录。对外分发时提供运行包和对应源码包。

当前实现复用了开源后端和协议客户端，因此早期方案中的“全部独立实现”不再描述本次实现范围。项目原有代码的许可证选择与第三方依赖的许可分别处理。
