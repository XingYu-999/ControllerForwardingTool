using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using ControllerForwardingTool.Protocol.Ns2;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Bluetooth;

public sealed class Ns2BleTransport : IDisposable
{
    private static readonly Guid Fd2Uuid = new("ab7de9be-89fe-49ad-828f-118f09df7fd2");
    private static readonly Guid AckUuid = new("c765a961-d9d8-4d36-a20a-5315b111836a");
    private static readonly Guid CommandUuid = new("649d4ac9-8eb7-4e6c-af44-1ea54fe5f005");
    private static readonly Guid BatteryLevelUuid = new("00002a19-0000-1000-8000-00805f9b34fb");
    private static readonly Guid ProInputUuid = new("7492866c-ec3e-4619-8258-32755ffcc0f8");
    private GattCharacteristic? proInput;
    private CancellationTokenSource? batteryLifetime;
    private int batteryMillivolts, batteryPercent = -1;
    private Ns2PowerLevel? powerLevel;
    public string BatteryDescription
    {
        get
        {
            int percent = Volatile.Read(ref batteryPercent), mv = Volatile.Read(ref batteryMillivolts);
            var power = Volatile.Read(ref powerLevel);
            string text = percent >= 0 ? $"电量 {percent}%" : power?.Description ?? "";
            if (mv > 0) text += (text.Length > 0 ? " · " : "电池电压 ") + $"{mv / 1000.0:F2} V";
            return text.Length > 0 ? text : "电量未知";
        }
    }
    private static readonly Guid RumbleUuid = new("cc483f51-9258-427d-a939-630c31f72b05");
    private readonly List<GattDeviceService> services = [];
    private BluetoothLEAdvertisementWatcher? watcher;
    private BluetoothLEDevice? device;
    private GattSession? session;
    private BluetoothLEPreferredConnectionParametersRequest? connectionParametersRequest;
    private bool observingConnectionParameters;
    public string ConnectionTiming { get; private set; } = "蓝牙未连接";
    private TaskCompletionSource? firstFrame;
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private GattCharacteristic? fd2;
    private GattCharacteristic? ack;
    private GattCharacteristic? command;
    private GattCharacteristic? batteryLevel;
    private GattCharacteristic? rumble;
    private CancellationTokenSource? rumbleLifetime;
    private Task? rumbleWriter;
    private Pro2OutputPacket? pendingRumble;
    private double rumbleGain = OutputRouteOptions.DefaultRumbleGain;
    private long rumbleWrites, rumbleFailures;
    public bool CanRumble => rumble is not null && rumbleLifetime is { IsCancellationRequested: false } && rumbleWriter is { IsCompleted: false };
    public long RumbleWrites => Interlocked.Read(ref rumbleWrites);
    public long RumbleFailures => Interlocked.Read(ref rumbleFailures);
    public double RumbleGain { get => Volatile.Read(ref rumbleGain); set => Volatile.Write(ref rumbleGain, double.IsFinite(value) ? Math.Clamp(value, 0, 3) : OutputRouteOptions.DefaultRumbleGain); }
    public void QueueRumble(Pro2OutputPacket packet)
    {
        if (CanRumble) Interlocked.Exchange(ref pendingRumble, packet with { Report = packet.Report.ToArray() });
    }
    private TaskCompletionSource<byte[]>? pendingAck;
    private Ns2PendingReply? pairingReply;
    public BleHostRegistration? Registration { get; private set; }
    private bool disposed;
    private BleDeviceIdentity[] rememberedDevices = [];
    private long advertisementsSeen, candidatesSeen, rememberedAdvertisementsSeen;
    private long localAdapterAddress;
    private int metadataWarningLogged;
    private readonly object advertisementGate = new();
    private readonly Dictionary<(ulong, BluetoothAddressType), BleCandidate> advertisementCache = [];
    public ulong LocalAddress => (ulong)Volatile.Read(ref localAdapterAddress);

    public void RememberDevices(IEnumerable<BleDeviceIdentity> identities) =>
        Volatile.Write(ref rememberedDevices, identities.Where(x => x.IsValid).ToArray());

    public void ReportScanDiagnostics() => Diagnostic?.Invoke(
        $"本轮扫描：广播 {Interlocked.Read(ref advertisementsSeen)}，候选 {Interlocked.Read(ref candidatesSeen)}，" +
        $"已记住设备广播 {Interlocked.Read(ref rememberedAdvertisementsSeen)}（0 不代表手柄未发射，可能未被适配器接收或地址已变化）");

    public event Action<BleCandidate>? CandidateSeen;
    public event Action<byte[], DateTimeOffset>? FrameReceived;
    public event Action<string>? StageChanged;
    public event Action<string>? Diagnostic;
    public event Action? DeviceDisconnected;
    public event Action<int?>? BatteryChanged;
    public event Action? ScanStopped;

    public async Task<string> CheckAdapterAsync()
    {
        BluetoothAdapter? adapter = await BluetoothAdapter.GetDefaultAsync();
        Volatile.Write(ref localAdapterAddress, (long)(adapter?.BluetoothAddress ?? 0));
        if (adapter is null) return "未发现蓝牙适配器";
        if (!adapter.IsLowEnergySupported) return "适配器不支持 BLE";
        Radio? radio = await adapter.GetRadioAsync();
        return radio?.State switch
        {
            RadioState.On => "蓝牙可用",
            RadioState.Off => "蓝牙已关闭",
            _ => "蓝牙状态未知或访问受限"
        };
    }

    public void StartScan()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        StopScan();
        Interlocked.Exchange(ref advertisementsSeen, 0);
        Interlocked.Exchange(ref candidatesSeen, 0);
        Interlocked.Exchange(ref rememberedAdvertisementsSeen, 0);
        Interlocked.Exchange(ref metadataWarningLogged, 0);
        lock (advertisementGate) advertisementCache.Clear();
        watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        watcher.Received += OnAdvertisement;
        watcher.Stopped += OnScanStopped;
        watcher.Start();
    }

    public void StopScan()
    {
        if (watcher is null) return;
        watcher.Received -= OnAdvertisement;
        watcher.Stopped -= OnScanStopped;
        watcher.Stop();
        watcher = null;
    }

    public async Task ConnectAsync(BleCandidate candidate, CancellationToken cancellationToken,
        TimeSpan? discoveryTimeout = null, bool registerHostOnSync = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await connectionGate.WaitAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        cancellationToken = timeout.Token;
        try
        {
            StopScan();
            CloseConnection();
            // Capture intent while the advertisement is fresh, before GATT initialization.
            bool registerHost = registerHostOnSync && BleDeviceHistory.IsFreshPairing(candidate, DateTimeOffset.Now);
            StageChanged?.Invoke("正在连接蓝牙");
            cancellationToken.ThrowIfCancellationRequested();
            // A remembered device may be asleep or have changed its random address.
            // Bound only discovery; keep the full initialization budget once it responds.
            using var discoveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (discoveryTimeout is { } budget) discoveryCancellation.CancelAfter(budget);
            var discoveryToken = discoveryCancellation.Token;
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(candidate.Address,
                candidate.AddressType).AsTask(discoveryToken) ?? throw new InvalidOperationException("Windows 无法打开此 BLE 设备");
            device.ConnectionStatusChanged += OnConnectionStatusChanged;
            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(discoveryToken);
            if (session?.CanMaintainConnection == true) session.MaintainConnection = true;
            await Task.Delay(500, discoveryToken);

            StageChanged?.Invoke("正在检查 GATT 特征");
            GattDeviceServicesResult discovery = await GattDiscoveryRetry.RunAsync(
                token => device!.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(token),
                result => result.Status,
                result => { foreach (var service in result.Services) service.Dispose(); },
                (attempt, result) =>
                {
                    // Diagnostics must not trigger extra device/property requests during discovery.
                    Diagnostic?.Invoke($"GATT 服务发现 {attempt}/3：{result.Status}；链路={device?.ConnectionStatus}；会话={session?.SessionStatus}");
                    if (result.Status == GattCommunicationStatus.Unreachable && attempt < 3)
                        StageChanged?.Invoke($"手柄尚未响应，重新建立连接会话 {attempt + 1}/3");
                }, discoveryToken, async token =>
                {
                    // A failed Windows session can keep returning Unreachable. Release
                    // all its references before reopening the same address, as a manual
                    // second connection did successfully in the captured diagnostic log.
                    CloseConnection();
                    await Task.Delay(800, token);
                    device = await BluetoothLEDevice.FromBluetoothAddressAsync(candidate.Address,
                        candidate.AddressType).AsTask(token) ?? throw new InvalidOperationException("Windows 无法重新打开此 BLE 设备");
                    device.ConnectionStatusChanged += OnConnectionStatusChanged;
                    session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(token);
                    if (session?.CanMaintainConnection == true) session.MaintainConnection = true;
                    await Task.Delay(500, token);
                });
            if (discovery.Status != GattCommunicationStatus.Success)
            {
                foreach (var service in discovery.Services) service.Dispose();
                throw new InvalidOperationException(discovery.Status == GattCommunicationStatus.Unreachable
                    ? "GATT 无法访问（Unreachable）；已释放失败会话。请保持 SYNC 配对灯闪烁后重试；候选列表中的旧广播不代表当前可连接"
                    : $"GATT 服务枚举失败：{discovery.Status}");
            }
            discoveryCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            Diagnostic?.Invoke("GATT 服务已响应，开始验证特征和初始化输入");
            RequestLowLatencyConnection();

            foreach (GattDeviceService service in discovery.Services)
            {
                services.Add(service);
            }
            foreach (GattDeviceService service in services)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GattCharacteristicsResult result =
                    await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
                if (result.Status != GattCommunicationStatus.Success)
                {
                    continue;
                }
                foreach (GattCharacteristic characteristic in result.Characteristics)
                {
                    if (characteristic.Uuid == Fd2Uuid) fd2 = characteristic;
                    else if (characteristic.Uuid == AckUuid) ack = characteristic;
                    else if (characteristic.Uuid == CommandUuid) command = characteristic;
                    else if (characteristic.Uuid == BatteryLevelUuid) batteryLevel = characteristic;
                    else if (characteristic.Uuid == ProInputUuid) proInput = characteristic;
                    else if (characteristic.Uuid == RumbleUuid) rumble = characteristic;
                }
            }
            if (fd2 is null || ack is null || command is null)
                throw new InvalidOperationException("未找到必要的 FD2、ACK 或命令特征；候选设备未经确认");

            if (batteryLevel is not null)
            {
                try
                {
                    GattReadResult battery = await batteryLevel.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
                    if (battery.Status == GattCommunicationStatus.Success)
                        ReportBattery(ReadBytes(battery.Value));
                    if ((batteryLevel.CharacteristicProperties & GattCharacteristicProperties.Notify) != 0)
                    {
                        batteryLevel.ValueChanged += OnBatteryLevel;
                        await batteryLevel.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(cancellationToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { Diagnostic?.Invoke($"电量特征读取失败：{ex.Message}"); }
            }

            ack.ValueChanged += OnAck;
            GattCommunicationStatus ackStatus = await SubscribeAsync(ack, cancellationToken);
            if (ackStatus != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"ACK 订阅失败：{ackStatus}");

            StageChanged?.Invoke("正在尝试实验初始化序列");
            for (int index = 0; index < Ns2InitProfile.ObservedSequence.Count; index++)
            {
                byte[] packet = Ns2InitProfile.ObservedSequence[index];
                cancellationToken.ThrowIfCancellationRequested();
                pendingAck = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                GattWriteOption option =
                    (command.CharacteristicProperties & GattCharacteristicProperties.WriteWithoutResponse) != 0
                        ? GattWriteOption.WriteWithoutResponse
                        : GattWriteOption.WriteWithResponse;
                GattCommunicationStatus write = GattCommunicationStatus.Unreachable;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    write = await command.WriteValueAsync(ToBuffer(packet), option).AsTask(cancellationToken);
                    if (write == GattCommunicationStatus.Success) break;
                    await Task.Delay(150 * (attempt + 1), cancellationToken);
                }
                Diagnostic?.Invoke($"初始化 {index + 1}/{Ns2InitProfile.ObservedSequence.Count} " +
                    $"profile={Ns2InitProfile.Version} tx={Convert.ToHexString(packet)} write={write}");
                if (write != GattCommunicationStatus.Success)
                    throw new InvalidOperationException($"初始化命令写入失败：{write}");
                try
                {
                    byte[] observedAck = await pendingAck.Task.WaitAsync(
                        TimeSpan.FromMilliseconds(1500), cancellationToken);
                    Diagnostic?.Invoke($"初始化 {index + 1} ACK={Convert.ToHexString(observedAck)}（内容尚未校验）");
                }
                catch (TimeoutException)
                {
                    Diagnostic?.Invoke($"初始化 {index + 1} ACK 超时");
                    StageChanged?.Invoke("初始化 ACK 超时，继续尝试下一命令");
                }
                finally { pendingAck = null; }
            }

            firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fd2.ValueChanged += OnFd2;
            GattCommunicationStatus notify = await SubscribeAsync(fd2, cancellationToken);
            if (notify != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"FD2 订阅失败：{notify}");
            if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                throw new InvalidOperationException("GATT 初始化期间蓝牙连接已断开");
            StageChanged?.Invoke("GATT 已连接，等待有效 FD2 输入");
            await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(6), cancellationToken);
            if (registerHost)
            {
                StageChanged?.Invoke("正在确认注册使用的蓝牙适配器");
                ulong hostAddress = await ResolveConnectionHostAsync(candidate.Address, cancellationToken);
                Volatile.Write(ref localAdapterAddress, (long)hostAddress);
                Registration = await Ns2PairingProtocol.RegisterAsync(hostAddress, candidate.Address,
                    ExchangePairingAsync, message => { StageChanged?.Invoke(message); Diagnostic?.Invoke(message); }, cancellationToken);
                Diagnostic?.Invoke($"主机注册已确认：手柄 {candidate.MaskedAddress}，本机 ••:{hostAddress & 0xFFFF:X4}；地址、挑战及提交回复均通过校验");
            }
            else Diagnostic?.Invoke(registerHostOnSync
                ? "本次未收到新的 SYNC 配对广播，跳过主机注册；普通回连不重复写入注册信息"
                : "注册到手柄开关已关闭：只连接输入，不写入主机注册；已有注册仍可回连");
            ReportConnectionTiming(device);
            // Optional low-frequency reads do not delay connection, change input reports,
            // or consume a second stream of input notifications.
            if (proInput is { } powerSource)
            {
                batteryLifetime = new();
                _ = ReadPowerLoopAsync(powerSource, batteryLifetime.Token);
            }
            if (rumble is { } target)
            {
                rumbleLifetime = new();
                var rumbleToken = rumbleLifetime.Token;
                rumbleWriter = Task.Run(() => WriteRumbleLoopAsync(target, rumbleToken));
            }
            StageChanged?.Invoke(Registration is not null ? "主机注册已确认，输入正常" : "已收到有效 FD2 输入");
        }
        catch
        {
            CloseConnection();
            throw;
        }
        finally { connectionGate.Release(); }
    }

    private void RequestLowLatencyConnection()
    {
        var current = device!;
        try
        {
            current.ConnectionParametersChanged += OnConnectionParametersChanged;
            observingConnectionParameters = true;
            // Retain the request for the lifetime of this connection; releasing it
            // restores Windows' default preferences. Failure must not break input.
            connectionParametersRequest = current.RequestPreferredConnectionParameters(
                BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
            Diagnostic?.Invoke($"已请求 BLE ThroughputOptimized：{connectionParametersRequest.Status}；以实际协商间隔为准");
        }
        catch (Exception ex)
        {
            Diagnostic?.Invoke($"BLE 较短连接间隔请求未生效，继续使用系统参数：{ex.Message}");
        }
        ReportConnectionTiming(current);
    }

    private void OnConnectionParametersChanged(BluetoothLEDevice sender, object args) => ReportConnectionTiming(sender);

    private void ReportConnectionTiming(BluetoothLEDevice current)
    {
        if (current != device) return;
        try
        {
            var parameters = current.GetConnectionParameters();
            string timing = parameters.ConnectionInterval == 0 ? "BLE 连接间隔尚未确定" :
                $"BLE 连接间隔 {parameters.ConnectionInterval * 1.25:F2} ms · 从机延迟 {parameters.ConnectionLatency}";
            if (current != device || ConnectionTiming == timing) return;
            ConnectionTiming = timing;
            Diagnostic?.Invoke(timing);
        }
        catch (Exception ex)
        {
            if (current != device) return;
            ConnectionTiming = "BLE 连接间隔暂不可读";
            Diagnostic?.Invoke($"读取 BLE 连接间隔失败：{ex.Message}");
        }
    }

    private async Task<ulong> ResolveConnectionHostAsync(ulong peer, CancellationToken token)
    {
        var adapters = await DeviceInformation.FindAllAsync(BluetoothAdapter.GetDeviceSelector()).AsTask(token);
        List<ulong> addresses = [];
        foreach (var info in adapters)
        {
            var adapter = await BluetoothAdapter.FromIdAsync(info.Id).AsTask(token);
            if (adapter is null || !adapter.IsLowEnergySupported) continue;
            var radio = await adapter.GetRadioAsync().AsTask(token);
            if (radio?.State == RadioState.On) addresses.Add(adapter.BluetoothAddress);
        }
        return BleHostAddress.Resolve(device!.BluetoothDeviceId.Id, peer, addresses);
    }

    private async Task<byte[]> ExchangePairingAsync(byte[] packet, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var pending = new Ns2PendingReply(0x15, packet[3]);
        Interlocked.Exchange(ref pairingReply, pending);
        try
        {
            var option = (command!.CharacteristicProperties & GattCharacteristicProperties.WriteWithoutResponse) != 0
                ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;
            var status = await command.WriteValueAsync(ToBuffer(packet), option).AsTask(timeout.Token);
            if (status != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"主机注册 15/{packet[3]:X2} 写入失败：{status}");
            var response = await pending.Task.WaitAsync(timeout.Token);
            // Pairing payloads contain key material. Log only the step and length.
            Diagnostic?.Invoke($"主机注册 15/{packet[3]:X2} 收到匹配回复，长度 {response.Length}");
            return response;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException(packet[3] == 3
                ? "主机注册提交回复超时，无法确认手柄是否已保存；请长按 SYNC 后重试"
                : $"主机注册 15/{packet[3]:X2} 回复超时，未继续提交注册");
        }
        finally
        {
            Interlocked.CompareExchange(ref pairingReply, null, pending);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(packet);
        }
    }

    public async Task DisconnectAsync()
    {
        await connectionGate.WaitAsync();
        try { CloseConnection(); }
        finally { connectionGate.Release(); }
    }

    // Called only by the user's explicit Delete action, never by discovery retries.
    public async Task ForgetSystemPairingAsync(BleDeviceIdentity identity, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await connectionGate.WaitAsync(timeout.Token);
        try
        {
            using var target = await BluetoothLEDevice.FromBluetoothAddressAsync(identity.Address,
                identity.AddressType).AsTask(timeout.Token);
            if (target is null) throw new InvalidOperationException("Windows 无法读取此手柄的系统配对状态");
            if (!target.DeviceInformation.Pairing.IsPaired) return;
            var result = await target.DeviceInformation.Pairing.UnpairAsync().AsTask(timeout.Token);
            if (result.Status is not (DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired))
                throw new InvalidOperationException($"Windows 取消配对失败：{result.Status}");
        }
        finally { connectionGate.Release(); }
    }

    private void CloseConnection()
    {
        if (observingConnectionParameters && device is not null)
        {
            try { device.ConnectionParametersChanged -= OnConnectionParametersChanged; }
            catch (Exception ex) { Diagnostic?.Invoke($"解除 BLE 参数监听失败，继续断开：{ex.Message}"); }
            observingConnectionParameters = false;
        }
        try { Interlocked.Exchange(ref connectionParametersRequest, null)?.Dispose(); }
        catch (Exception ex) { Diagnostic?.Invoke($"释放 BLE 参数请求失败，继续断开：{ex.Message}"); }
        ConnectionTiming = "蓝牙未连接";
        Registration = null;
        Interlocked.Exchange(ref pairingReply, null)?.Cancel();
        rumbleLifetime?.Cancel(); rumbleLifetime?.Dispose(); rumbleLifetime = null;
        rumbleWriter = null;
        Interlocked.Exchange(ref pendingRumble, null);
        pendingAck?.TrySetCanceled();
        pendingAck = null;
        firstFrame?.TrySetCanceled();
        firstFrame = null;
        if (batteryLevel is not null) batteryLevel.ValueChanged -= OnBatteryLevel;
        if (fd2 is not null)
        {
            fd2.ValueChanged -= OnFd2;
        }
        if (ack is not null)
        {
            ack.ValueChanged -= OnAck;
        }
        batteryLifetime?.Cancel(); batteryLifetime?.Dispose(); batteryLifetime = null;
        proInput = null;
        Volatile.Write(ref powerLevel, null);
        Volatile.Write(ref batteryMillivolts, 0);
        Volatile.Write(ref batteryPercent, -1);
        fd2 = ack = command = batteryLevel = rumble = null;
        foreach (GattDeviceService service in services) service.Dispose();
        services.Clear();
        if (session is not null)
        {
            if (session.CanMaintainConnection) session.MaintainConnection = false;
            session.Dispose();
            session = null;
        }
        if (device is not null)
        {
            device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            device.Dispose();
            device = null;
        }
        BatteryChanged?.Invoke(null);
    }

    private async Task WriteRumbleLoopAsync(GattCharacteristic target, CancellationToken token)
    {
        byte sequence = 0;
        var playback = new BleRumblePlayback();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(12));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                var packet = playback.Next(Interlocked.Exchange(ref pendingRumble, null), clock.Elapsed);
                if (packet is null) continue;
                byte[] data = new byte[Pro2BleRumblePacketEncoder.BlePacketSize];
                if (!Pro2BleRumblePacketEncoder.TryEncodeRaw02(packet.Report, sequence++, data, out _, out _, packet.GainOverride ?? RumbleGain)) continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var option = (target.CharacteristicProperties & GattCharacteristicProperties.WriteWithoutResponse) != 0
                    ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;
                var status = await target.WriteValueAsync(ToBuffer(data), option).AsTask(timeout.Token);
                if (status == GattCommunicationStatus.Success) Interlocked.Increment(ref rumbleWrites);
                else { Interlocked.Increment(ref rumbleFailures); Diagnostic?.Invoke($"BLE 震动写入失败：{status}"); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Interlocked.Increment(ref rumbleFailures); Diagnostic?.Invoke($"BLE 震动通道已停止：{ex.Message}"); }
    }

    private void OnAdvertisement(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (sender != watcher) return;
        Interlocked.Increment(ref advertisementsSeen);
        string name = args.Advertisement.LocalName ?? string.Empty;
        var known = Volatile.Read(ref rememberedDevices)
            .FirstOrDefault(x => x.Matches(args.BluetoothAddress, args.BluetoothAddressType));
        bool rememberedMatch = known?.Matches(args.BluetoothAddress, args.BluetoothAddressType) == true;
        if (rememberedMatch) Interlocked.Increment(ref rememberedAdvertisementsSeen);
        bool manufacturerMatch = args.Advertisement.ManufacturerData.Any(x => x.CompanyId == 0x0553);
        Ns2Advertisement? details = null;
        bool? connectable = null;
        bool scanResponse = false;
        try
        {
            details = args.Advertisement.ManufacturerData.Where(x => x.CompanyId == 0x0553)
                .Select(x => Ns2Advertisement.Parse(ReadBytes(x.Data))).FirstOrDefault(x => x is not null);
            connectable = args.IsConnectable;
            scanResponse = args.IsScanResponse;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            if (Interlocked.Exchange(ref metadataWarningLogged, 1) == 0)
                Diagnostic?.Invoke($"附加广播信息不可用，继续按基础广播连接：{ex.Message}");
        }
        BleCandidate candidate;
        lock (advertisementGate)
        {
            var key = (args.BluetoothAddress, args.BluetoothAddressType);
            advertisementCache.TryGetValue(key, out var previous);
            if (previous is not null && DateTimeOffset.Now - previous.LastSeen > TimeSpan.FromSeconds(2)) previous = null;
            if (previous is null && !BleAdvertisementFilter.Accepts(args.BluetoothAddress, args.BluetoothAddressType,
                    name, manufacturerMatch, known)) return;
            if (string.IsNullOrWhiteSpace(name)) name = previous?.Name ?? known?.Name ?? string.Empty;
            candidate = new BleCandidate(args.BluetoothAddress, args.BluetoothAddressType,
                name, args.RawSignalStrengthInDBm, DateTimeOffset.Now)
            {
                Advertisement = details ?? previous?.Advertisement,
                // Receiving another scan response must not make old pairing metadata fresh.
                AdvertisementSeenAt = details is not null ? DateTimeOffset.Now : previous?.AdvertisementSeenAt,
                // A scan response adds data; it does not revoke the connectability of its advertisement.
                IsConnectable = scanResponse ? previous?.IsConnectable : connectable
            };
            if (advertisementCache.Count >= 256) advertisementCache.Clear();
            advertisementCache[key] = candidate;
        }
        Interlocked.Increment(ref candidatesSeen);
        CandidateSeen?.Invoke(candidate);
    }

    private void OnScanStopped(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (sender != watcher) return;
        StageChanged?.Invoke($"扫描停止：{args.Error}");
        ScanStopped?.Invoke();
    }

    private void OnAck(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (sender != ack) return;
        byte[] bytes = ReadBytes(args.CharacteristicValue);
        if (Volatile.Read(ref pairingReply) is { } pending) pending.Accept(bytes);
        else pendingAck?.TrySetResult(bytes);
    }

    private void OnFd2(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (sender != fd2) return;
        byte[] data = ReadBytes(args.CharacteristicValue);
        DateTimeOffset now = DateTimeOffset.Now;
        if (new Fd2Decoder().TryDecode(data, now, out _))
        {
            Volatile.Write(ref batteryMillivolts, Ns2Battery.ReadMillivolts(data) ?? 0);
            firstFrame?.TrySetResult();
        }
        FrameReceived?.Invoke(data, now);
    }

    private static async Task<GattCommunicationStatus> SubscribeAsync(GattCharacteristic characteristic,
        CancellationToken token)
    {
        GattCommunicationStatus status = GattCommunicationStatus.Unreachable;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(token);
            if (status == GattCommunicationStatus.Success) return status;
            await Task.Delay(350 * (attempt + 1), token);
        }
        return status;
    }

    private void OnBatteryLevel(GattCharacteristic sender, GattValueChangedEventArgs args)
    { if (sender == batteryLevel) ReportBattery(ReadBytes(args.CharacteristicValue)); }

    private void ReportBattery(byte[] value)
    {
        if (value.Length > 0 && value[0] <= 100)
        { Volatile.Write(ref batteryPercent, value[0]); BatteryChanged?.Invoke(value[0]); }
    }

    private async Task ReadPowerLoopAsync(GattCharacteristic target, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && target == proInput)
            {
                Ns2PowerLevel? reading = null;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    var result = await target.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                    if (result.Status == GattCommunicationStatus.Success)
                        reading = Ns2Battery.ReadPowerLevel(ReadBytes(result.Value));
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                { Diagnostic?.Invoke($"可选电量读取未成功，使用 FD2 电压：{ex.Message}"); }
                if (token.IsCancellationRequested || target != proInput) return;
                Volatile.Write(ref powerLevel, reading);
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Diagnostic?.Invoke($"电量读取已停止：{ex.Message}"); }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender == device && sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            DeviceDisconnected?.Invoke();
    }

    private static byte[] ReadBytes(IBuffer buffer)
    {
        byte[] bytes = new byte[buffer.Length];
        using DataReader reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static IBuffer ToBuffer(byte[] bytes)
    {
        using DataWriter writer = new();
        writer.WriteBytes(bytes);
        return writer.DetachBuffer();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopScan();
        _ = DisconnectAsync();
    }
}
