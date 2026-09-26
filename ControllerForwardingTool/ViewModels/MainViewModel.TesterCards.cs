using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<TesterDeviceCard> TesterSourceCards { get; } = [];
    public ObservableCollection<TesterDeviceCard> TesterVirtualCards { get; } = [];
    public VirtualControllerMode TesterPreviewMode { get; private set; } = VirtualControllerMode.Ns1Pro;
    public bool IsMappedTester => SelectedTesterSource == TesterSources[2];
    public bool HasTesterDevices => TesterSourceCards.Count > 0;
    public bool HasNoTesterDevices => !HasTesterDevices;

    private void SyncTesterCards(IReadOnlyList<GamepadDevice> devices)
    {
        IImage Icon(VirtualControllerMode mode) => ModeCards.First(x => x.Profile.Mode == mode).Image;
        foreach (var card in TesterSourceCards.Where(c => c.Key == "ble" ? !IsConnected : devices.All(d => c.Device?.Id != d.Id)).ToArray())
            TesterSourceCards.Remove(card);
        if (IsConnected && TesterSourceCards.All(c => c.Key != "ble"))
            TesterSourceCards.Insert(0, new("ble", "NS2 Pro", Icon(VirtualControllerMode.Ns2Pro), SelectTesterCard)
                { KindLabel = "实体手柄", Detail = "蓝牙直连 · 已连接" });
        foreach (var device in devices)
        {
            var card = TesterSourceCards.FirstOrDefault(c => c.Device?.Id == device.Id);
            if (card is null)
            {
                var mode = device.Layout switch {
                    ControllerLayout.Switch2Pro => VirtualControllerMode.Ns2Pro, ControllerLayout.SwitchPro => VirtualControllerMode.Ns1Pro,
                    ControllerLayout.DualSenseEdge => VirtualControllerMode.DualSenseEdge,
                    ControllerLayout.DualSense or ControllerLayout.DualShock or ControllerLayout.DualShock3 => VirtualControllerMode.DualSense,
                    _ => VirtualControllerMode.Xbox360 };
                card = new($"device:{device.Id}", device.Name, Icon(mode), SelectTesterCard) { Device = device };
                TesterSourceCards.Add(card);
            }
            card.Device = device;
            card.KindLabel = inputGuard.IsOwned(device) ? "模拟手柄 · 本应用" : "系统手柄";
            card.Detail = $"{device.Vendor:X4}:{device.Product:X4} · #{device.Id}";
        }
        foreach (var profile in VirtualProfile.All)
        {
            var card = TesterVirtualCards.FirstOrDefault(c => c.Mode == profile.Mode);
            if (card is null)
            {
                card = new($"virtual:{profile.Mode}", profile.Name, Icon(profile.Mode), SelectTesterCard) { Mode = profile.Mode };
                TesterVirtualCards.Add(card);
            }
            card.KindLabel = "映射参考";
            card.Detail = "使用上次保存的映射";
        }
        OnPropertyChanged(nameof(HasTesterDevices));
        OnPropertyChanged(nameof(HasNoTesterDevices));
        UpdateTesterCardSelection();
    }

    private void SelectTesterCard(TesterDeviceCard card)
    {
        TesterSkin = "自动识别";
        if (card.Device is { } device)
        { SelectedWindowsGamepad = device; SelectedTesterSource = TesterSources[0]; }
        else if (card.Mode is { } mode)
        {
            TesterPreviewMode = mode;
            SelectedTesterSource = TesterSources[2];
            ResetTesterRate(); ResetStickTrace();
        }
        else SelectedTesterSource = TesterSources[1];
        UpdateTesterCardSelection();
    }
    private void UpdateTesterCardSelection()
    {
        foreach (var card in TesterSourceCards.Concat(TesterVirtualCards))
            card.IsSelected = IsWindowsTester ? card.Device is not null && card.Device.Id == SelectedWindowsGamepad?.Id
                : IsMappedTester ? card.Mode == TesterPreviewMode : card.Key == "ble";
    }

    private void UpdateMappedTester()
    {
        var state = inputBridge.Latest; // The bridge always applies the last saved mapping, never the editor draft.
        if (DateTimeOffset.Now - state.ReceivedAt >= TimeSpan.FromMilliseconds(250))
        { ClearTester("映射预览等待输入 · 请在虚拟手柄页选择并连接手柄源"); return; }
        TesterIsOnline = true; TesterLayout = MappingPreview.Layout(TesterPreviewMode);
        TesterButtons = MappingPreview.OutputButtons(state.Buttons, TesterPreviewMode);
        TesterLeftX = StickCoordinates.Unit(state.LeftX); TesterLeftY = StickCoordinates.Unit(state.LeftY);
        TesterRightX = StickCoordinates.Unit(state.RightX); TesterRightY = StickCoordinates.Unit(state.RightY);
        TesterLeftTrigger = state.LeftTriggerValue / 255.0; TesterRightTrigger = state.RightTriggerValue / 255.0;
        TesterBatteryPercent = -1;
        TesterSummary = VirtualProfile.Get(TesterPreviewMode).Name + " · 已保存映射预览";
        TesterDeviceInfo = "此处显示上次应用并保存的映射参考；实际接收测试请在上方选择已连接的手柄。";
        TesterRateText = "映射预览 · 非接收速率";
        TesterRateHint = "输入来自虚拟手柄页选择的手柄源；参考视图不表示 Windows 实际接收，也不会启动模拟器。";
        var buttons = Enum.GetValues<ControllerButtons>().Where(b => b != ControllerButtons.None).ToArray();
        SetValues(TesterButtonValues, buttons.Select(b => (TesterButtons & b) != 0 ? 1.0 : 0.0).ToArray(), "B", buttons.Select(b => b.ToString()).ToArray());
        SetValues(TesterAxisValues, [TesterLeftX, TesterLeftY, TesterRightX, TesterRightY], "轴 ");
        UpdateMotion(CurrentCalibration, TesterPreviewMode != VirtualControllerMode.Xbox360 &&
            (!IsWindowsBridgeInput || SelectedBridgeGamepad is { } pad && gamepads.Read(pad) is { Gyro: not null, Accel: not null }));
    }
}

public partial class TesterDeviceCard : ObservableObject
{
    public string Key { get; }
    public string Name { get; }
    public IImage Image { get; }
    public VirtualControllerMode? Mode { get; init; }
    public GamepadDevice? Device { get; set; }
    public IRelayCommand SelectCommand { get; }
    [ObservableProperty] public partial string Detail { get; set; } = "";
    [ObservableProperty] public partial string KindLabel { get; set; } = "";
    [ObservableProperty] public partial bool IsSelected { get; set; }
    public TesterDeviceCard(string key, string name, IImage image, Action<TesterDeviceCard> select)
    { Key = key; Name = name; Image = image; SelectCommand = new RelayCommand(() => select(this)); }
}
