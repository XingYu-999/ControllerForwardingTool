using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    public string ConfigurationFilePath => BridgeOptions.SettingsPath;
    public string LogDirectoryPath => AppDataPaths.LogDirectoryPath;
    public string UserDataDirectoryPath => AppDataPaths.DirectoryPath;
    public string LogWriteStatus => ApplicationLog.Current.LastError is { } error
        ? $"日志写入失败：{error}" : "运行日志自动保存；单文件约 5 MB，最多保留 10 个运行日志文件";
    [ObservableProperty] public partial string DataFilesResult { get; set; } = "手动编辑配置前请退出程序；下次启动读取修改。";

    [RelayCommand] private void OpenConfigurationFile()
    {
        try
        {
            if (!File.Exists(ConfigurationFilePath)) bridgeOptions.Save();
            try { Process.Start(new ProcessStartInfo(ConfigurationFilePath) { UseShellExecute = true }); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1155)
            {
                var editor = new ProcessStartInfo("notepad.exe") { UseShellExecute = true };
                editor.ArgumentList.Add(ConfigurationFilePath);
                Process.Start(editor);
            }
            DataFilesResult = "已打开配置文件。手动编辑前请退出程序，避免运行中的设置覆盖修改。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        { DataFilesResult = $"无法打开配置文件：{ex.Message}"; AddLog("配置", DataFilesResult); }
    }

    [RelayCommand] private void OpenLogDirectory()
    {
        try
        {
            Directory.CreateDirectory(LogDirectoryPath);
            Process.Start(new ProcessStartInfo(LogDirectoryPath) { UseShellExecute = true });
            DataFilesResult = "已打开日志目录；app-*.log 是运行日志，diagnostics-*.txt 是手动导出。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        { DataFilesResult = $"无法打开日志目录：{ex.Message}"; AddLog("日志", DataFilesResult); }
    }
}
