using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Bluetooth;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.Usb;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<GamepadDevice> Ns2UsbDevices { get; } = [];
    [ObservableProperty] public partial GamepadDevice? SelectedNs2UsbDevice { get; set; }
    [ObservableProperty] public partial bool IsNs2UsbBusy { get; set; }
    [ObservableProperty] public partial bool IsNs2UsbConnected { get; set; }
    [ObservableProperty] public partial string Ns2UsbStatus { get; set; } = "插入 USB 数据线即可发现手柄";
    [ObservableProperty] public partial string Ns2UsbRegistrationStatus { get; set; } = "注册状态未知 · 请连接 USB 手柄";
    [ObservableProperty] public partial string Ns2UsbInputText { get; set; } = "等待 USB 输入";
    private UsbRegistrationResult? usbRegistration;
    private string? ns2UsbSerial, inspectedUsbSerial;
    private string? lastNs2UsbDiagnostic;
    private GamepadDevice? ns2UsbFeedbackDevice;
    private ulong usbHandoverAddress;
    private bool usbWasConnected;
    private DateTimeOffset usbResumeUntil;
    public bool CanInspectNs2Usb => !IsNs2UsbBusy && SelectedNs2UsbDevice is { Serial.Length: > 0 } && IsNs2UsbConnected;
    public bool CanRegisterNs2Usb => CanInspectNs2Usb && usbRegistration is { Registered: false };
    public bool CanDeleteNs2Usb => CanInspectNs2Usb && usbRegistration is { Registered: true, HasOtherHosts: false };

    private void NotifyUsbActions()
    {
        OnPropertyChanged(nameof(CanInspectNs2Usb)); OnPropertyChanged(nameof(CanRegisterNs2Usb));
        OnPropertyChanged(nameof(CanDeleteNs2Usb)); OnPropertyChanged(nameof(ConnectionStatusLabel));
        OnPropertyChanged(nameof(CanConnect));
    }
    partial void OnIsNs2UsbBusyChanged(bool value) => NotifyUsbActions();
    partial void OnIsNs2UsbConnectedChanged(bool value) => NotifyUsbActions();
    partial void OnSelectedNs2UsbDeviceChanged(GamepadDevice? value)
    {
        if (IsNs2UsbBusy) return;
        if (value is not null && value.Serial != ns2UsbSerial)
        {
            ns2UsbSerial = value.Serial; inspectedUsbSerial = null; usbRegistration = null;
            usbHandoverAddress = bridgeOptions.Ns2UsbAddresses.GetValueOrDefault(value.Serial);
            Ns2UsbRegistrationStatus = "注册状态未知 · 等待读取手柄";
        }
        inputBridge?.SelectNs2Usb(value);
        Volatile.Write(ref ns2UsbFeedbackDevice, value);
        NotifyUsbActions();
    }

    private void SyncNs2Usb(IReadOnlyList<GamepadDevice> devices)
    {
        if (IsNs2UsbBusy) return;
        var available = devices.Where(d => d.Vendor == 0x057E && d.Product == 0x2069 && inputGuard.Allows(d)).ToArray();
        var chosen = available.FirstOrDefault(d => d.Serial == ns2UsbSerial);
        if (chosen is null && DateTimeOffset.Now < usbResumeUntil) return;
        if (ns2UsbSerial is null && available.Length == 1) chosen = available[0];
        if (!Ns2UsbDevices.SequenceEqual(available))
        {
            Ns2UsbDevices.Clear(); foreach (var pad in available) Ns2UsbDevices.Add(pad);
        }
        SelectedNs2UsbDevice = chosen;
        // SDL recreates instance IDs after releasing its exclusive USB command interface.
        // Restore only the same physical serial, including a running Windows-input route.
        if (chosen is not null && SelectedBridgeGamepad is { } previous && previous.Id != chosen.Id &&
            !string.IsNullOrWhiteSpace(chosen.Serial) && previous.Serial == chosen.Serial &&
            previous.Vendor == chosen.Vendor && previous.Product == chosen.Product)
            SelectedBridgeGamepad = chosen;
        var snapshot = chosen is null ? null : gamepads.Read(chosen);
        IsNs2UsbConnected = snapshot is not null;
        if (IsNs2UsbConnected && !usbWasConnected)
        {
            if (IsConnecting) connectionCancellation?.Cancel();
            if (IsConnected) _ = HandleLostConnectionAsync();
        }
        string? diagnostic = gamepads.Latest.Ns2UsbStatus;
        Ns2UsbStatus = snapshot is not null ? "NS2 Pro · USB 已连接" : available.Length > 0 ? "请选择要连接的 USB 手柄"
            : diagnostic ?? "USB 未连接 · 请插入数据线";
        if (diagnostic != lastNs2UsbDiagnostic)
        {
            if (diagnostic is not null) AddLog("USB", diagnostic);
            lastNs2UsbDiagnostic = diagnostic;
        }
        Ns2UsbInputText = snapshot is null ? "等待 USB 输入" : $"按键：{snapshot.Buttons} · 电量 {(snapshot.Battery >= 0 ? snapshot.Battery + "%" : "未知")} · 输入 {snapshot.InputRateHz?.ToString("F1") ?? "—"} Hz";
        if (usbWasConnected && !IsNs2UsbConnected)
        {
            gamepads.ClearFeedback(); inspectedUsbSerial = null; usbRegistration = null;
            Ns2UsbRegistrationStatus = "USB 已断开 · 注册状态需连接后重新读取";
            if (AutoConnect) { scanRequested = true; nextScanAt = default; BeginScan(); }
        }
        usbWasConnected = IsNs2UsbConnected;
        if (chosen is not null && snapshot is not null && inspectedUsbSerial != chosen.Serial && !string.IsNullOrWhiteSpace(chosen.Serial))
        {
            inspectedUsbSerial = chosen.Serial;
            _ = RunUsbRegistrationAsync(UsbRegistrationAction.Inspect);
        }
        NotifyUsbActions();
    }

    [RelayCommand] private Task InspectNs2UsbAsync() => RunUsbRegistrationAsync(UsbRegistrationAction.Inspect);
    [RelayCommand] private void RetryNs2Usb()
    {
        if (IsNs2UsbBusy) return;
        UpdateNs2UsbAccess();
        gamepads.RetryNs2Usb();
    }
    [RelayCommand] private Task RegisterNs2UsbAsync() => RunUsbRegistrationAsync(UsbRegistrationAction.Register);
    [RelayCommand] private Task DeleteNs2UsbRegistrationAsync() => RunUsbRegistrationAsync(UsbRegistrationAction.Delete);
    [RelayCommand] private void TestNs2Usb()
    {
        if (SelectedNs2UsbDevice is not { } device) return;
        SelectedTesterSource = TesterSources[0]; SelectedWindowsGamepad = device; SelectedPage = "手柄测试";
    }

    private async Task RunUsbRegistrationAsync(UsbRegistrationAction action)
    {
        if (!CanInspectNs2Usb || SelectedNs2UsbDevice is not { } selected || closing) return;
        if (action == UsbRegistrationAction.Register && !CanRegisterNs2Usb || action == UsbRegistrationAction.Delete && !CanDeleteNs2Usb) return;
        IsNs2UsbBusy = true; usbRegistration = null;
        Ns2UsbRegistrationStatus = action switch
        {
            UsbRegistrationAction.Register => "正在通过 USB 注册这台电脑…",
            UsbRegistrationAction.Delete => "正在删除手柄中的本机注册…",
            _ => "正在读取手柄注册信息…"
        };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var infos = await DeviceInformation.FindAllAsync(BluetoothAdapter.GetDeviceSelector()).AsTask(timeout.Token);
            List<ulong> addresses = [];
            foreach (var info in infos)
            {
                var adapter = await BluetoothAdapter.FromIdAsync(info.Id).AsTask(timeout.Token);
                if (adapter?.IsLowEnergySupported != true) continue;
                if ((await adapter.GetRadioAsync().AsTask(timeout.Token))?.State == RadioState.On) addresses.Add(adapter.BluetoothAddress);
            }
            if (addresses.Distinct().Count() != 1) throw new InvalidOperationException("请仅开启一个 BLE 蓝牙适配器，再刷新注册状态");
            ulong host = addresses[0];
            gamepads.ClearFeedback();
            var result = await gamepads.WithNs2UsbReleasedAsync(() =>
            {
                using var session = Ns2UsbSession.Open(selected.Serial, timeout.Token);
                return Ns2UsbProtocol.RunAsync(action, host, session.Exchange, timeout.Token).GetAwaiter().GetResult();
            }, timeout.Token);
            usbRegistration = result;
            Ns2UsbRegistrationStatus = result.Registered ? "已注册到这台电脑 · 已读取手柄确认" : "未注册到这台电脑 · 已读取手柄确认";
            if (result.HasOtherHosts) Ns2UsbRegistrationStatus += "；手柄保存了其他主机，本机删除不可用";
            ulong peer = result.Controller != 0 ? result.Controller : bridgeOptions.Ns2UsbAddresses.GetValueOrDefault(selected.Serial);
            var identities = bridgeOptions.BleDevices;
            var mappings = new Dictionary<string, ulong>(bridgeOptions.Ns2UsbAddresses);
            if (result.Controller != 0) mappings[selected.Serial] = result.Controller;
            if (peer != 0)
            {
                var identity = new BleDeviceIdentity(peer, BluetoothAddressType.Public, "NS2 Pro")
                { Registration = result.Registered ? new(host, DateTimeOffset.UtcNow) : null };
                identities = result.Registered ? identities.Remember(identity) : identities.Forget(identity);
                usbHandoverAddress = peer;
            }
            bridgeOptions = bridgeOptions with { Ns2UsbAddresses = mappings, BleDevices = identities };
            RefreshRememberedDevices();
            try { bridgeOptions.Save(); }
            catch (Exception ex) { Ns2UsbRegistrationStatus += $"；本地记录保存失败：{ex.Message}"; }
            if (action == UsbRegistrationAction.Delete && peer != 0 && activeBleCandidate?.Address == peer)
                await DisconnectAsync();
            AddLog("USB", Ns2UsbRegistrationStatus);
        }
        catch (OperationCanceledException) { Ns2UsbRegistrationStatus = "操作已取消或超时 · 注册结果未知，请刷新状态"; }
        catch (Exception ex) { Ns2UsbRegistrationStatus = $"注册状态未确认：{ex.Message}"; AddLog("USB", Ns2UsbRegistrationStatus); }
        finally { usbResumeUntil = DateTimeOffset.Now.AddSeconds(2); IsNs2UsbBusy = false; gamepads.RequestRefresh(); NotifyUsbActions(); }
    }
}
