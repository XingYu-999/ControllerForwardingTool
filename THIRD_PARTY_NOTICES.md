# Third-party components

Documentation scope: Controller Forwarding Tool 1.0.1 (2026-09-27). Component versions, attribution and original license texts below remain unchanged by this application release.

Controller Forwarding Tool (手柄转发工具, formerly NS2ProWin11) licenses its original application code under the MIT License; see the accompanying `LICENSE`. Use, modification, redistribution and commercial use of that original code do not require notifying the authors or obtaining additional permission, provided the MIT copyright and permission notices are retained. Third-party code, libraries, drivers and artwork remain subject to their respective licenses; the project's MIT License does not replace those terms. Original copyright notices and license texts are retained in the paths below.

From r5, releases are distributed as two adjacent downloads. From r7, their names are `ControllerForwardingTool-win-x64-*.zip` (runtime) and `ControllerForwardingTool-third-party-sources-*.zip` (matching sources); older releases use the NS2ProWin11 prefix. Source paths in the table refer to the repository or the extracted source package. The runtime keeps license texts (`licenses/` and `drivers/`) and this notice, but does not include development documentation, debug symbols or source archives. Distributors should make both packages available together.

| Component | Origin / version | License and source |
| --- | --- | --- |
| usbip-win2 Windows driver/client | [vadimgrn/usbip-win2 v0.9.7.7](https://github.com/vadimgrn/usbip-win2/releases/tag/v0.9.7.7) | BSD-2-Clause; `drivers/usbip-win2/v0.9.7.7/LICENSE.txt` |
| VIIPER haptic service | [LeonChrome/XinHeLianSheng-Pro2-Bridge](https://github.com/LeonChrome/XinHeLianSheng-Pro2-Bridge), `source/final-three-in-one/tools/viiper/haptic-v0.8.0/viiper-haptic.exe`; based on [Alia5/VIIPER](https://github.com/Alia5/VIIPER) | GPL-3.0; `drivers/viiper/LICENSE.txt`; accompanying reference source snapshot `drivers/viiper/viiper-haptic-source.tar.gz` includes build files, `go.mod`, `go.sum` and upstream notices |
| VIIPER C# protocol client | Reference bridge `windows/v60_viiper_app/ViiperProtocolClient.cs`, adapted namespace | Apache-2.0; `drivers/viiper/BRIDGE-LICENSE.txt`; source header retains attribution |
| SDL 3.4.16 | [libsdl-org/SDL release-3.4.16](https://github.com/libsdl-org/SDL/releases/tag/release-3.4.16) | zlib; `drivers/sdl3/3.4.16/LICENSE.txt`; pristine source archive in that directory |
| libusb 1.0.30 | [libusb/libusb v1.0.30](https://github.com/libusb/libusb/releases/tag/v1.0.30), MinGW64 DLL | LGPL-2.1-or-later; `drivers/libusb/1.0.30/COPYING`; corresponding source tarball in that directory |

SDL3.dll is a **custom build**, not the official prebuilt SDL DLL. It enables HIDAPI/libusb for Switch 2 USB and disables unused audio/video/render/camera backends. It is dynamically linked to libusb. `tools/build-sdl.ps1` in the companion source package rebuilds from the unmodified SDL source, using LLVM-MinGW 20260922 (ucrt x86_64), CMake 4.4.3 and Ninja 1.13.2. Pass the LLVM toolchain `bin` directory and CMake/Ninja paths. libusb's replaceable DLL and LGPL text remain in the runtime package; its source, header and import library are in the accompanying source package. Replacing the DLL does not require recompiling the C# application.

VIIPER runs as a separate process and communicates over localhost TCP. Its binary is copied unchanged and pinned by SHA-256. The included source is the reference repository's `tools/viiper/haptic-src` snapshot; byte-for-byte reproducibility of that published binary has not been established. It can be inspected/rebuilt using its `README.md`, `justfile` and Go module files. Updating/rebuilding the backend also requires updating the pin in `VirtualControllerSession.cs` and rerunning the integration checks.

NuGet dependencies (Avalonia, CommunityToolkit.Mvvm and their transitive packages) retain their own package licenses. See the `.csproj` and NuGet package metadata for exact versions.

## About page and project icons

The About page renders the application's original SVG with [Svg.Controls.Skia.Avalonia 12.0.0 / Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) (MIT). Its transitive packages retain their own licenses in NuGet metadata.

Icons in `ControllerForwardingTool/Assets/OpenSource/` were obtained from the respective upstream repositories or official websites for Avalonia, .NET, CommunityToolkit, SDL, libusb, VIIPER, Svg.Skia, XinHeLianSheng-Pro2-Bridge and Switch2Connect. Exact asset URLs are recorded in `Assets/OpenSource/SOURCES.md`. The bridge PNG is extracted from the upstream ICO without changing its artwork. Project icons and trademarks remain the property of their owners and are used only to identify credited projects, without implying endorsement. Projects without an identified icon use plain initials.

The previously cited Pryxo DS4Windows Switch 2 repository returned HTTP 404 when checked on 2026-09-26; the About page retains that historical attribution and labels the unavailable source.

## NS2 Bluetooth and USB registration protocol references

`Bluetooth/Ns2PairingProtocol.cs` is an independent implementation of the documented wire protocol in [ndeadly/switch2_controller_research](https://github.com/ndeadly/switch2_controller_research), specifically `commands.md` and `bluetooth_interface.md`. Tests include the published AES input/output vector to check byte order. [Switch2Connect](https://github.com/TommyWabg/Switch2Connect) and [Pryxo's DS4Windows branch](https://github.com/Pryxo/DS4Windows-Switch-2-Pro-Controller-and-Wireless-Support) were consulted to compare the connection sequence; their pairing source code is not copied into this implementation.

The USB registration path uses the same pairing exchange with USB-specific framing and validates stored host addresses using the research repository's `memory_layout.md`. Transport access uses the bundled libusb DLL. These protocol references do not imply firmware or hardware validation of every registration operation.

## SHA-256 of bundled runtime files

| File | SHA-256 |
| --- | --- |
| USBip-0.9.7.7-x64.exe | `51620FA5F9F8BE5932BC9D786DEEE557CE06D5407A99CAB490DCFAC71F185FEA` |
| viiper-haptic.exe | `F153400F095817AF5056A6658A6EBD93A46F533F0BCD5E5DB3E59B0731278727` |
| SDL3.dll | `1931EDD584B82BD3B99F4EF1229A8C6707CDC37B16803ED2C2BA76C0F0B02011` |
| libusb-1.0.dll | `5BD409849825009B6FE25861A6147F76D256AAB248F07049D78387E3BFF12D94` |

## r3 source integration

`VirtualDevice/Source/` adapts `Pro2OutputPacketMapper.cs`, `Pro2BleRumblePacketEncoder.cs`, `DualSenseHapticFeedback.cs`, `AudioEndpointGuard.cs`, and `HighResolutionPeriodicTimer.cs` from the reference bridge v6.2.32 (Apache-2.0, accompanying `drivers/viiper/BRIDGE-LICENSE.txt`). `VirtualProfiles.cs` adapts the packed wire layouts from `VirtualPadPackets.cs`.

The output mode cards use official controller product images in `Assets/Controllers/`, downloaded on 2026-09-26 from Sony Interactive Entertainment, Nintendo, and Microsoft. Image ownership remains with the respective manufacturers; these images are not covered by the source code licenses. Source pages and exact download URLs are recorded in `ControllerForwardingTool/docs/15_CONTROLLER_PRODUCT_IMAGES.md`. The former `Assets/Characters/` images remain as historical source assets and are excluded from application resources.

| Card / image | Official source |
| --- | --- |
| PS5 / `dualsense.png` | [PlayStation DualSense](https://www.playstation.com/en-us/accessories/dualsense-wireless-controller/) |
| NS2 PRO / `switch2-pro.png` | [Nintendo Switch 2 accessories](https://www.nintendo.com/us/gaming-systems/switch-2/accessories/) |
| XBOX / `xbox-wireless.png` | [Microsoft Xbox Wireless Controller](https://www.microsoft.com/en-us/d/xbox-wireless-controller/8xn59crbsqgz) |
| PS5 EDGE / `dualsense-edge.png` | [PlayStation DualSense Edge](https://www.playstation.com/en-us/accessories/dualsense-edge-wireless-controller/) |
| NS1 PRO / `switch-pro.png` | [Nintendo Switch Pro Controller](https://www.nintendo.com/us/store/products/pro-controller/) |

The Xbox Wireless Controller image represents the Xbox family; the application's virtual output remains the Xbox 360 / XInput identity (`045E:028E`). Product imagery does not imply manufacturer endorsement or additional emulated device capabilities.

The tester layout references [All Controller Test](https://allcontrollertest.com/zh/tester) and the user's screenshots. The controller outlines and charts are locally drawn Avalonia controls; website code and web assets are not bundled.

The r5 controller layout and motion calibration controls reference the user's Steam screenshots. They are original Avalonia drawings and controls; Steam artwork and code are not bundled. Motion orientation uses an original System.Numerics quaternion implementation, informed by the gyro/gravity fusion approach in the locally supplied eden project (`src/hid_core/frontend/motion_input.cpp`) and [SDL's sensor coordinate documentation](https://wiki.libsdl.org/SDL3/SDL_SensorType). No eden source code was copied into the application.
