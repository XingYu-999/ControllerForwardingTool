using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    public string UserDataDirectoryPath => AppDataPaths.DirectoryPath;
    public bool HasLogWriteError => ApplicationLog.Current.LastError is not null;
    public string LogWriteStatus => ApplicationLog.Current.LastError is { } error
        ? $"日志写入失败：{error}" : "运行日志自动保存；单文件约 5 MB，最多保留 10 个运行日志文件";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDataFilesResult))]
    public partial string DataFilesResult { get; set; } = "";
    public bool HasDataFilesResult => !string.IsNullOrEmpty(DataFilesResult);

    [RelayCommand] private void OpenUserDataDirectory()
    {
        try
        {
            Directory.CreateDirectory(UserDataDirectoryPath);
            Process.Start(new ProcessStartInfo(UserDataDirectoryPath) { UseShellExecute = true });
            DataFilesResult = "已打开用户数据目录。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        { DataFilesResult = $"无法打开用户数据目录：{ex.Message}"; AddLog("配置", DataFilesResult); }
    }
}
