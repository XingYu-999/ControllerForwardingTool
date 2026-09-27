using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<TesterDeviceCard> BridgeInputCards { get; } = [];
    public bool HasNoBridgeInputCards => BridgeInputCards.Count == 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBridgeInputRumbleResult))]
    public partial string BridgeInputRumbleResult { get; set; } = "";
    public bool HasBridgeInputRumbleResult => BridgeInputRumbleResult.Length > 0;

    private void SyncBridgeInputCards()
    {
        if (BridgeInputCards.All(c => c.Key != "keyboard"))
            BridgeInputCards.Insert(0, new("keyboard", "键盘 / 鼠标", KeyboardMouseImage(), SelectBridgeInputCardAsync)
                { KindLabel = "默认输入", Detail = "无需手柄 · 无振动 · F8 开始 / 暂停" });
        foreach (var card in BridgeInputCards.Where(c => c.Key != "keyboard" && (c.Key == "ble"
                     ? !IsConnected : BridgeInputDevices.All(d => c.Device?.Id != d.Id))).ToArray())
            BridgeInputCards.Remove(card);
        if (IsConnected && BridgeInputCards.All(c => c.Key != "ble"))
            BridgeInputCards.Insert(0, new("ble", "NS2 Pro", ControllerImage(ControllerLayout.Switch2Pro), SelectBridgeInputCardAsync)
                { KindLabel = "实体手柄", Detail = "蓝牙直连 · 已连接" });
        foreach (var device in BridgeInputDevices)
        {
            var card = BridgeInputCards.FirstOrDefault(c => c.Device?.Id == device.Id);
            if (card is null)
            {
                card = new($"device:{device.Id}", device.Name, ControllerImage(device.Layout), SelectBridgeInputCardAsync);
                BridgeInputCards.Add(card);
            }
            card.Device = device;
            card.KindLabel = "系统手柄";
            card.Detail = $"{device.Vendor:X4}:{device.Product:X4}" + (gamepads.Read(device) is null ? " · 已断开" : "");
        }
        OnPropertyChanged(nameof(HasNoBridgeInputCards));
        UpdateBridgeInputCardSelection();
    }

    private void UpdateBridgeInputCardSelection()
    {
        foreach (var card in BridgeInputCards)
            card.IsSelected = IsKeyboardMouseInput ? card.Key == "keyboard" : IsWindowsBridgeInput
                ? card.Device is not null && card.Device.Id == SelectedBridgeGamepad?.Id
                : card.Device is not null ? card.Device.Id == SelectedNs2UsbDevice?.Id : card.Key == "ble" && !IsNs2UsbConnected;
    }

    private async Task SelectBridgeInputCardAsync(TesterDeviceCard card)
    {
        if (!CanEditMode || closing || !BridgeInputCards.Contains(card)) return;
        if (card.Key == "keyboard")
        {
            BridgeInputSelection = BridgeInputKinds[2];
            BridgeInputRumbleResult = "";
            UpdateBridgeInputCardSelection();
            return;
        }
        if (card.Device is { } device)
        {
            if (!inputGuard.Allows(device) || gamepads.Read(device) is null) return;
            SelectedBridgeGamepad = device;
            // Keep NS2 USB on its native route so its mappings and USB/BLE handover remain available.
            if (device.Layout == ControllerLayout.Switch2Pro)
            {
                SelectedNs2UsbDevice = device;
                BridgeInputSelection = BridgeInputKinds[0];
            }
            else BridgeInputSelection = BridgeInputKinds[1];
        }
        else
        {
            if (!IsConnected) return;
            BridgeInputSelection = BridgeInputKinds[0];
        }
        UpdateBridgeInputCardSelection();
        var result = await IdentifyControllerAsync(card);
        if (card.IsSelected) BridgeInputRumbleResult = result;
    }
}
