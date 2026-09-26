using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace ControllerForwardingTool.VirtualDevice;

public sealed record UsbIpDriverStatus(string Installer, string Driver, bool CanInstall, bool CanUninstall, bool Ready);

public sealed class UsbIpDriverInstaller
{
    private const string InstallerName = "USBip-0.9.7.7-x64.exe";
    private const string ExpectedSha256 = "51620fa5f9f8be5932bc9d786deee557ce06d5407a99cab490dcfac71f185fea";
    private const string ExpectedSigner = "Cloudyne Systems";

    public string? FindClient() => InstallDirectories().Select(x => Path.Combine(x, "usbip.exe")).FirstOrDefault(File.Exists);

    private static IReadOnlyList<string> InstallDirectories()
    {
        List<string> directories = [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "USBip")];
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (string name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") is string display && display.StartsWith("USBip", StringComparison.OrdinalIgnoreCase)
                    && entry.GetValue("InstallLocation") is string location) directories.Add(location);
            }
        }
        return directories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<string> UninstallAsync()
    {
        string uninstaller = FindUninstaller() ?? throw new FileNotFoundException("USB/IP 未安装或缺少原厂卸载器，请先安装 / 修复");
        using Process process = Process.Start(new ProcessStartInfo(uninstaller)
        { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(uninstaller)! })
            ?? throw new InvalidOperationException("无法启动卸载向导");
        await process.WaitForExitAsync();
        return process.ExitCode switch
        {
            0 => "卸载向导已结束；已刷新驱动状态",
            1641 or 3010 => "卸载已结束；需要重启 Windows",
            var code => $"卸载未完成，返回码 {code}"
        };
    }

    public string? FindUninstaller()
    {
        // A stale kernel service is not evidence of an installed, removable package.
        return InstallDirectories().Select(x => Path.Combine(x, "unins000.exe")).FirstOrDefault(File.Exists);
    }

    public string? FindInstaller()
    {
        string relative = Path.Combine("drivers", "usbip-win2", "v0.9.7.7", InstallerName);
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, relative),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relative))
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    public UsbIpDriverStatus Inspect()
    {
        string? installer = FindInstaller();
        string installerText = installer is null ? "未找到随附安装器" : $"随附安装器：{InstallerName}";
        string service;
        bool ready = false, removable = false;
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\usbip2_ude");
            service = key is null ? "未安装 USB/IP 内核驱动" : "USB/IP 内核驱动已注册";
            string? client = FindClient();
            ready = key is not null && client is not null;
            removable = FindUninstaller() is not null;
            service += client is null ? " · 缺少 usbip.exe，请安装 / 修复" : $" · 客户端 {FileVersionInfo.GetVersionInfo(client).FileVersion}";
        }
        catch (Exception ex)
        {
            service = $"驱动状态读取失败：{ex.Message}";
        }
        return new UsbIpDriverStatus(installerText, service, installer is not null, removable, ready);
    }

    public async Task<string> InstallAsync()
    {
        string path = FindInstaller() ?? throw new FileNotFoundException("未找到随附 USB/IP 安装器");
        await using (FileStream stream = File.OpenRead(path))
        {
            string digest = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!digest.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("USB/IP 安装器 SHA-256 与仓库记录不一致，已停止安装");
        }

        // Pin the exact package hash and embedded signer identity before requesting elevation.
        // Certificate extraction alone does not validate the complete Authenticode trust chain.
#pragma warning disable SYSLIB0057
        using X509Certificate2 signature = X509CertificateLoader.LoadCertificate(
            X509Certificate.CreateFromSignedFile(path).Export(X509ContentType.Cert));
#pragma warning restore SYSLIB0057
        if (!signature.Subject.Contains(ExpectedSigner, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("USB/IP 安装器签名者与仓库记录不一致，已停止安装");

        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path)!,
            UseShellExecute = true,
            Verb = "runas"
        }) ?? throw new InvalidOperationException("Windows 未能启动 USB/IP 安装器");
        await process.WaitForExitAsync();
        return process.ExitCode switch
        {
            0 => "安装器已结束；请检查驱动状态，再验证虚拟设备挂载",
            1641 or 3010 => "安装器已结束；需要重启 Windows 后验证驱动",
            int code => $"安装器返回错误码 {code}；请查看安装向导结果"
        };
    }

    public static bool IsUserCancellation(Exception ex) =>
        ex is Win32Exception { NativeErrorCode: 1223 };
}
