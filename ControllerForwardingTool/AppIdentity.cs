using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ControllerForwardingTool;

/// <summary>Product names shared by the desktop shell, diagnostics and startup registration.</summary>
public static class AppIdentity
{
    public const string ChineseName = "手柄转发工具";
    public const string EnglishName = "Controller Forwarding Tool";
    public const string ExecutableName = "ControllerForwardingTool.exe";
    public const string LegacyStartupName = "NS2ProWin11";
    public static string Version => typeof(AppIdentity).Assembly.GetName().Version!.ToString(3);
    public static string VersionLabel => $"当前版本 {Version}";
    public static string BuildTime { get; } = GetBuildTime();
    public static string AvaloniaVersion => GetProductVersion(typeof(Avalonia.Application).Assembly);
    public static string FrameworkLabel => $"Avalonia {AvaloniaVersion}  +  {RuntimeInformation.FrameworkDescription}";
    public const string SourceCodeUrl = "https://github.com/XingYu-999/ControllerForwardingTool";
    public static Uri SourceCodeUri { get; } = new(SourceCodeUrl);

    internal static string GetProductVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? assembly.GetName().Version?.ToString(3) ?? "未知";

    private static string GetBuildTime()
    {
        string? value = typeof(AppIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "BuildTimestamp")?.Value;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)
            ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
            : "未知";
    }
}
