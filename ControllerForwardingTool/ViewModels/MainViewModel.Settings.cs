using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] public partial bool LaunchAtLogin { get; set; }
    [ObservableProperty] public partial bool RestoreOutput { get; set; }
    [ObservableProperty] public partial bool StartInTray { get; set; }
    [ObservableProperty] public partial bool MinimizeToTray { get; set; }
    [ObservableProperty] public partial bool CloseToTray { get; set; } = true;
    [ObservableProperty] public partial string ApplicationSettingsResult { get; set; } = "修改后点击保存软件设置。";

    // Window behavior follows the saved snapshot, not unsaved checkbox edits.
    internal bool ShouldStartInTray => bridgeOptions.StartInTray;
    internal bool ShouldMinimizeToTray => bridgeOptions.MinimizeToTray;
    internal bool ShouldCloseToTray => bridgeOptions.CloseToTray;

    private void InitializeApplicationSettings()
    {
        LaunchAtLogin = WindowsStartupRegistration.IsEnabled();
        RestoreOutput = bridgeOptions.AutoStartOutput;
        StartInTray = bridgeOptions.StartInTray;
        MinimizeToTray = bridgeOptions.MinimizeToTray;
        CloseToTray = bridgeOptions.CloseToTray;
        ApplicationSettingsResult = "修改后点击保存软件设置。";
    }

    partial void OnLaunchAtLoginChanged(bool value) => MarkApplicationSettingsChanged();
    partial void OnRestoreOutputChanged(bool value) => MarkApplicationSettingsChanged();
    partial void OnStartInTrayChanged(bool value) => MarkApplicationSettingsChanged();
    partial void OnMinimizeToTrayChanged(bool value) => MarkApplicationSettingsChanged();
    partial void OnCloseToTrayChanged(bool value) => MarkApplicationSettingsChanged();
    private void MarkApplicationSettingsChanged() => ApplicationSettingsResult = "软件设置有未保存的修改。";

    [RelayCommand]
    private void SaveApplicationSettings() => SaveApplicationSettings(options => options.Save(), WindowsStartupRegistration.Save);

    internal void SaveApplicationSettings(Action<BridgeOptions> save, Action<bool, Action> saveWithStartup)
    {
        try
        {
            var next = bridgeOptions with
            {
                LaunchAtLogin = LaunchAtLogin, AutoStartOutput = RestoreOutput,
                StartInTray = StartInTray, MinimizeToTray = MinimizeToTray, CloseToTray = CloseToTray,
            };
            // Keep this independent of the virtual page's pending mode, port and mapping edits.
            saveWithStartup(next.LaunchAtLogin, () => save(next));
            bridgeOptions = next;
            ApplicationSettingsResult = "软件设置已保存 · 窗口行为立即生效；启动行为在下次启动时生效。";
        }
        catch (Exception ex) { ApplicationSettingsResult = $"保存软件设置失败：{ex.Message}"; }
    }
}
