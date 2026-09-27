using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] public partial string OverviewHeading { get; set; } = "准备你的游戏连接";
    [ObservableProperty] public partial string OverviewHint { get; set; } = "正在检查运行环境…";
    [ObservableProperty] public partial string OverviewAutoConnectionText { get; set; } = "正在检查连接状态…";
    [ObservableProperty] public partial string OverviewWindowsSummary { get; set; } = "等待 Windows 手柄枚举";

    private void UpdateOverview()
    {
        OverviewHeading = IsServerRunning ? output.Live ? "输入正在转发" : "虚拟输出已启动，等待输入"
            : IsDriverReady ? "运行环境就绪，选择你的输出身份" : "先准备 USB/IP 驱动";
        OverviewHint = IsServerRunning ? "在手柄测试中选择 Windows 输入，检查按键、摇杆和扳机，再进入游戏。"
            : IsDriverReady ? "在虚拟手柄页选择键鼠或实体手柄输入，再选择 Nintendo、Xbox 或 PlayStation 输出身份。"
            : "打开驱动配置完成安装或修复，刷新状态后即可启动虚拟手柄。普通 USB 手柄测试仍可使用。";
        OverviewAutoConnectionText = IsKeyboardMouseInput ? KeyboardMouseStatus : IsWindowsBridgeInput ? "使用所选 Windows 手柄输入；插入 USB 或连接已在系统配对的蓝牙手柄。"
            : AutoConnect ? "自动连接已开启 · 已连接过可尝试 L + R 唤醒；首次或回连失败请长按顶部 SYNC。"
            : "自动连接已暂停 · 点击开始扫描可重新启用自动搜索与连接。";
        OverviewWindowsSummary = WindowsGamepads.Count == 0 ? "尚未检测到 Windows 手柄"
            : $"Windows 已发现 {WindowsGamepads.Count} 个手柄 · 进入测试选择设备";
    }

    [RelayCommand] private void OpenWindowsTester()
    {
        SelectedTesterSource = TesterSources[0];
        SelectedPage = "手柄测试";
        RefreshGamepads();
        SelectedWindowsGamepad = WindowsGamepads.FirstOrDefault(d => inputGuard.IsOwned(d)) ?? WindowsGamepads.FirstOrDefault();
        UpdateTesterCardSelection();
    }
}
