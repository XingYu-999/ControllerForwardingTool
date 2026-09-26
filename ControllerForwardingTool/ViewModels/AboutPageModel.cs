using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ControllerForwardingTool.ViewModels;

public sealed record OpenSourceProject(string Name, string Description, string Repository, string Initials,
    string? IconFile = null)
{
    public Uri Url => new($"https://github.com/{Repository}");
    public string? SvgPath => IconFile?.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) == true
        ? $"avares://ControllerForwardingTool/Assets/OpenSource/{IconFile}" : null;
    public bool HasSvg => SvgPath is not null;
    public bool HasBitmap => IconFile is not null && !HasSvg;
    public bool HasNoIcon => IconFile is null;
    public Bitmap? Icon { get; } = LoadBitmap(IconFile);

    private static Bitmap? LoadBitmap(string? file)
    {
        if (file is null || file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null;
        using var stream = AssetLoader.Open(new Uri($"avares://ControllerForwardingTool/Assets/OpenSource/{file}"));
        return new Bitmap(stream);
    }
}

public partial class AboutPageModel : ObservableObject
{
    [ObservableProperty] private string linkStatus = "";

    public IReadOnlyList<OpenSourceProject> Libraries { get; } =
    [
        new("Avalonia", $"{AppIdentity.AvaloniaVersion} · 桌面界面框架", "AvaloniaUI/Avalonia", "AV", "avalonia.png"),
        new(".NET", $"{Environment.Version} · 应用运行时", "dotnet/runtime", ".N", "dotnet.png"),
        new("CommunityToolkit.Mvvm", $"{AppIdentity.GetProductVersion(typeof(ObservableObject).Assembly)} · MVVM 与命令", "CommunityToolkit/dotnet", "CT", "toolkit.png"),
        new("SDL", "3.4.16 · 手柄输入、传感器与震动", "libsdl-org/SDL", "SDL", "sdl.png"),
        new("libusb", "1.0.30 · USB 设备访问", "libusb/libusb", "USB", "libusb.png"),
        new("usbip-win2", "0.9.7.7 · 虚拟 USB 驱动与客户端", "vadimgrn/usbip-win2", "IP"),
        new("VIIPER", "虚拟输入服务 · 使用参考桥接项目的 haptic 分支", "Alia5/VIIPER", "VI", "viiper.svg"),
        new("Svg.Skia", "SVG 矢量图标渲染", "wieslawsoltes/Svg.Skia", "SVG", "svg-skia.png")
    ];

    public IReadOnlyList<OpenSourceProject> References { get; } =
    [
        new("XinHeLianSheng-Pro2-Bridge", "桥接实现、输出报文与触觉反馈参考", "LeonChrome/XinHeLianSheng-Pro2-Bridge", "BR", "bridge.png"),
        new("Switch 2 Controller Research", "蓝牙接口、命令与主机注册协议研究", "ndeadly/switch2_controller_research", "S2"),
        new("Switch2Connect", "连接与配对流程参考", "TommyWabg/Switch2Connect", "SC", "switch2connect.png"),
        new("DS4Windows · Switch 2 分支", "连接序列参考 · 原仓库当前不可访问", "Pryxo/DS4Windows-Switch-2-Pro-Controller-and-Wireless-Support", "DS")
    ];
}
