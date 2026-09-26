using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace ControllerForwardingTool.Core;

internal static class WindowsStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string UserShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppIdentity.ChineseName + ".lnk");
    internal static string CommonShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), AppIdentity.ChineseName + ".lnk");

    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return File.Exists(UserShortcut) || File.Exists(CommonShortcut) ||
            key?.GetValue(AppIdentity.EnglishName) is not null || key?.GetValue(AppIdentity.LegacyStartupName) is not null;
    }

    internal static void Save(bool enabled, Action persist)
    {
        string? path = Environment.ProcessPath;
        if (enabled && (path is null || !Path.GetFileName(path).Equals(AppIdentity.ExecutableName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("请从发布版 EXE 设置开机启动");
        if (enabled && RequiresElevation(path!))
            throw new InvalidOperationException("程序已设置为以管理员身份启动，请先在安装器中取消该选项，再启用 Startup 开机自启。");

        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        Save(enabled, persist, path ?? "", UserShortcut, CommonShortcut, key);
    }

    // Paths and legacy key are injectable so checks never change actual Windows startup.
    internal static void Save(bool enabled, Action persist, string executable, string userShortcut,
        string commonShortcut, RegistryKey? key)
    {
        bool commonEnabled = File.Exists(commonShortcut);
        if (!enabled && commonEnabled)
            throw new InvalidOperationException("安装器已启用所有用户自启。请重新运行安装器，取消“开机时自动启动”，当前用户设置不能关闭所有用户入口。");
        byte[]? previousShortcut = File.Exists(userShortcut) ? File.ReadAllBytes(userShortcut) : null;
        string[] names = [AppIdentity.EnglishName, AppIdentity.LegacyStartupName];
        var previous = names.Select(name => (Name: name,
            Value: key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames),
            Kind: key?.GetValue(name) is null ? RegistryValueKind.String : key.GetValueKind(name))).ToArray();
        try
        {
            if (enabled && !commonEnabled) CreateShortcut(executable, userShortcut);
            else File.Delete(userShortcut);
            // Migrate only this user's known legacy entries, and avoid a second shortcut
            // when the installer already created an all-users Startup entry.
            foreach (string name in names) key?.DeleteValue(name, false);
            persist();
        }
        catch (Exception failure)
        {
            try
            {
                if (previousShortcut is null) File.Delete(userShortcut);
                else File.WriteAllBytes(userShortcut, previousShortcut);
                foreach (var entry in previous)
                    if (entry.Value is null) key?.DeleteValue(entry.Name, false);
                    else key?.SetValue(entry.Name, entry.Value, entry.Kind);
            }
            catch (Exception rollback)
            {
                throw new IOException($"{failure.Message}；恢复 Windows 启动项失败，请重试保存：{rollback.Message}", failure);
            }
            throw;
        }
    }

    internal static void CreateShortcut(string executable, string shortcutPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        string temporary = Path.Combine(Path.GetDirectoryName(shortcutPath)!, $".cft-{Guid.NewGuid():N}.lnk");
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!);
            dynamic automation = shell!;
            shortcut = automation.CreateShortcut(temporary);
            dynamic link = shortcut;
            link.TargetPath = executable;
            link.WorkingDirectory = Path.GetDirectoryName(executable);
            link.Description = AppIdentity.ChineseName;
            link.Save();
            File.Move(temporary, shortcutPath, overwrite: true);
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
            File.Delete(temporary);
        }
    }

    private static bool RequiresElevation(string executable)
    {
        const string layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        using var user = Registry.CurrentUser.OpenSubKey(layers);
        using var machine = Registry.LocalMachine.OpenSubKey(layers);
        return new[] { user?.GetValue(executable) as string, machine?.GetValue(executable) as string }
            .Any(value => value?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("RUNASADMIN", StringComparer.OrdinalIgnoreCase) == true);
    }
}
