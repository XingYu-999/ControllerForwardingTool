# 输出身份卡片：官方手柄产品图

1.0.1 文档同步（2026-09-27）：继续使用下列素材及原下载日期；项目版本变化见[更新日志](../../CHANGELOG.md)，权利说明见[第三方声明](../../THIRD_PARTY_NOTICES.md)。本页验证结果为素材接入时的历史记录。

2026-09-26：将五张输出身份卡片的游戏角色图替换为各厂商官网下载的手柄产品图。图片内置为 Avalonia 资源，运行时无需联网。白色展示区、等比缩放与上下留白保证手柄完整显示。

原有模式选择、选中标记、USB 身份与转发行为保持不变。XBOX 卡片使用现款 Xbox Wireless Controller 代表 Xbox 系列，虚拟设备协议仍为 Xbox 360 / XInput。

## 原始下载地址

文件位于 `Assets/Controllers/`。仅使用官网图片服务提供的 PNG 格式和 1000 像素宽度版本，未进行 AI 生成或重绘。原图保持不变；界面通过 `CroppedBitmap` 只收紧图片外围留白，统一产品展示比例。更换原图时需同步检查 `VirtualModeCard` 中的展示区域。

| 文件 | 下载地址 |
| --- | --- |
| `dualsense.png` | https://gmedia.playstation.com/is/image/SIEPDC/dualsense-controller-image-block-01-ps5-26jun20?fmt=png-alpha&wid=1000 |
| `switch2-pro.png` | https://assets.nintendo.com/image/upload/f_png,w_1000/ccb3e8ca3c296e21a8c933e8369031511589d0ef6b079cf5bb3667b09893482c/accessories/Pro%20Controller/Frame_3473717 |
| `xbox-wireless.png` | https://cdn-dynmedia-1.microsoft.com/is/image/microsoftcorp/gldn-Xbox-Cntrl-Black-b00?fmt=png-alpha&wid=1000 |
| `dualsense-edge.png` | https://gmedia.playstation.com/is/image/SIEPDC/DualSense-Edge-image-block-02-en-24aug22?fmt=png-alpha&wid=1000 |
| `switch-pro.png` | https://assets.nintendo.com/image/upload/f_png,w_1000/ncom/en_US/products/accessories/nintendo-switch/controllers/pro-controllers/nintendo-switch-pro-controller/104888-nintendo-switch-pro-controller-black-angle-1200x675 |

来源产品页和权利说明见仓库根目录 `THIRD_PARTY_NOTICES.md`。

## 验证

- `dotnet build ControllerForwardingTool/ControllerForwardingTool.csproj -c Release --no-restore`：0 警告、0 错误。
- 实际窗口检查：五张产品图均加载，手柄主体完整显示，卡片文字及标识正常。
- 点击 PS5 EDGE 后选中标记与协议说明同步更新，再切回 NS2 PRO；验证期间未启动虚拟 USB。
