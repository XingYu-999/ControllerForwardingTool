using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.VirtualDevice;

/// <summary>Loopback-only USB/IP 1.1.1 server. Attach requires a separate client driver.</summary>
public sealed class UsbIpPrototypeServer : IAsyncDisposable
{
    private const ushort Version = 0x0111;
    private const int HeaderLength = 48;
    private const int MaximumTransfer = 65536;
    private readonly UsbIpNs1Device device = new();
    private readonly List<TcpClient> clients = [];
    private readonly object clientsGate = new();
    private TcpListener? listener;
    private CancellationTokenSource? lifetime;
    private Task? acceptLoop;
    private readonly int port;
    private int imported;
    private readonly Func<int> pushHz;
    public UsbIpPrototypeServer(int port = 3240, Func<int>? pushHz = null)
    { this.port = port; this.pushHz = pushHz ?? (() => 0); }
    public void NotifySettingsChanged() => device.NotifySettingsChanged();

    public bool IsRunning => listener is not null;
    internal int BoundPort => ((IPEndPoint)listener!.LocalEndpoint).Port;
    public event Action<string>? Event;
    public event Action? InputReportSent;
    public event Action<byte[]>? OutputReceived
    {
        add => device.OutputReceived += value;
        remove => device.OutputReceived -= value;
    }

    public void Publish(ControllerState state) => device.Publish(state);

    public void Start()
    {
        if (IsRunning) return;
        lifetime = new CancellationTokenSource();
        var candidate = new TcpListener(IPAddress.Loopback, port);
        candidate.Start();
        listener = candidate;
        // Never inherit Avalonia's synchronization context. HID initialization can wait for
        // this server while the UI/native input thread is busy opening the device.
        CancellationToken token = lifetime.Token;
        acceptLoop = Task.Run(() => AcceptLoopAsync(candidate, token));
        Event?.Invoke($"USB/IP 服务端监听 127.0.0.1:{port}，等待客户端列举");
    }

    public async Task StopAsync()
    {
        if (listener is null) return;
        lifetime?.Cancel();
        listener.Stop();
        listener = null;
        lock (clientsGate)
        {
            foreach (TcpClient client in clients) client.Dispose();
            clients.Clear();
        }
        if (acceptLoop is not null)
        {
            try { await acceptLoop; }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
        acceptLoop = null;
        lifetime?.Dispose();
        lifetime = null;
        Event?.Invoke("USB/IP 服务端已停止");
    }

    private async Task AcceptLoopAsync(TcpListener server, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await server.AcceptTcpClientAsync(token); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            client.NoDelay = true;
            lock (clientsGate) clients.Add(client);
            _ = HandleClientAsync(client, token);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        bool ownsDevice = false;
        try
        {
            await using NetworkStream stream = client.GetStream();
            byte[] operation = new byte[8];
            await stream.ReadExactlyAsync(operation, token);
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(operation);
            ushort code = BinaryPrimitives.ReadUInt16BigEndian(operation.AsSpan(2));
            if (version != Version) return;
            switch (code)
            {
                case 0x8005: // OP_REQ_DEVLIST
                    await WriteDeviceListAsync(stream, token);
                    Event?.Invoke("客户端已读取虚拟设备列表");
                    break;
                case 0x8003: // OP_REQ_IMPORT
                    byte[] busIdBytes = new byte[32];
                    await stream.ReadExactlyAsync(busIdBytes, token);
                    string busId = Encoding.ASCII.GetString(busIdBytes).TrimEnd('\0');
                    if (busId != UsbIpNs1Device.BusId)
                    {
                        byte[] failure = OperationReply(0x0003, 1);
                        await stream.WriteAsync(failure, token);
                        break;
                    }
                    if (Interlocked.CompareExchange(ref imported, 1, 0) != 0)
                    {
                        await stream.WriteAsync(OperationReply(0x0003, 1), token);
                        break;
                    }
                    ownsDevice = true;
                    byte[] reply = new byte[8 + 312];
                    OperationReply(0x0003, 0).CopyTo(reply, 0);
                    UsbIpNs1Device.DeviceRecord().CopyTo(reply, 8);
                    await stream.WriteAsync(reply, token);
                    device.ResetSession();
                    Event?.Invoke("客户端已导入 NS1 Pro；正在处理 USB 请求");
                    await HandleTransfersAsync(stream, token);
                    break;
            }
        }
        catch (EndOfStreamException) { }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Event?.Invoke($"USB/IP 会话错误：{ex.Message}"); }
        finally
        {
            if (ownsDevice) Interlocked.Exchange(ref imported, 0);
            lock (clientsGate) clients.Remove(client);
            client.Dispose();
        }
    }

    private static async Task WriteDeviceListAsync(NetworkStream stream, CancellationToken token)
    {
        byte[] reply = new byte[8 + 4 + 312 + 4];
        OperationReply(0x0005, 0).CopyTo(reply, 0);
        BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(8), 1);
        UsbIpNs1Device.DeviceRecord().CopyTo(reply, 12);
        reply[324] = 3; // HID interface class
        await stream.WriteAsync(reply, token);
    }

    private async Task HandleTransfersAsync(NetworkStream stream, CancellationToken token)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
        token = connection.Token;
        using var writes = new SemaphoreSlim(1, 1);
        using var inputs = new SemaphoreSlim(1, 1);
        var pending = new ConcurrentDictionary<uint, (CancellationTokenSource Cancel, TaskCompletionSource Done)>();
        async Task SendAsync(byte[] reply, CancellationToken cancellation)
        {
            await writes.WaitAsync(cancellation);
            try { await stream.WriteAsync(reply, cancellation); }
            finally { writes.Release(); }
        }
        async Task CompleteInputAsync(uint sequence, int requested, CancellationTokenSource cancellation, TaskCompletionSource done)
        {
            try
            {
                await inputs.WaitAsync(cancellation.Token);
                try
                {
                    byte[] report = await device.ReadInputAsync(requested, pushHz, cancellation.Token);
                    await SendAsync(TransferReply(sequence, report.Length, report), cancellation.Token);
                    if (report.Length > 0 && report[0] == 0x30) InputReportSent?.Invoke();
                }
                finally { inputs.Release(); }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { connection.Cancel(); }
            catch (Exception ex) { Event?.Invoke($"USB/IP 输入失败：{ex.Message}"); connection.Cancel(); }
            finally { pending.TryRemove(sequence, out _); cancellation.Dispose(); done.TrySetResult(); }
        }
        byte[] header = new byte[HeaderLength];
        try
        {
        while (!token.IsCancellationRequested)
        {
            await stream.ReadExactlyAsync(header, token);
            uint command = BinaryPrimitives.ReadUInt32BigEndian(header);
            uint sequence = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
            uint direction = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12));
            uint endpoint = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16));
            if (command == 2) // USBIP_CMD_UNLINK
            {
                uint target = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(24));
                if (pending.TryGetValue(target, out var transfer))
                {
                    try { transfer.Cancel.Cancel(); } catch (ObjectDisposedException) { }
                    await transfer.Done.Task;
                }
                byte[] unlink = new byte[HeaderLength];
                BinaryPrimitives.WriteUInt32BigEndian(unlink, 4);
                BinaryPrimitives.WriteUInt32BigEndian(unlink.AsSpan(4), sequence);
                await SendAsync(unlink, token);
                continue;
            }
            if (command != 1) return;
            int requested = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(24));
            if (requested < 0 || requested > MaximumTransfer) return;
            byte[] output = [];
            if (direction == 0 && requested > 0)
            {
                output = new byte[requested];
                await stream.ReadExactlyAsync(output, token);
            }
            byte[] input = [];
            if (endpoint == 0)
                input = device.HandleControl(header.AsSpan(40, 8), output);
            else if (endpoint == 1 && direction == 1)
            {
                if (pending.Count >= 64) throw new IOException("过多未完成的 USB 输入请求");
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!pending.TryAdd(sequence, (cancellation, done)))
                { cancellation.Dispose(); throw new IOException("重复的 USB/IP 请求序号"); }
                // Keep receiving control/OUT/unlink while interrupt IN waits for a new source frame.
                _ = Task.Run(() => CompleteInputAsync(sequence, requested, cancellation, done));
                continue;
            }
            else if (endpoint == 1 && direction == 0)
                device.HandleInterruptOut(output);

            if (input.Length > requested) input = input.AsSpan(0, requested).ToArray();
            int actual = direction == 1 ? input.Length : output.Length;
            await SendAsync(TransferReply(sequence, actual, direction == 1 ? input : []), token);
        }
        }
        finally
        {
            connection.Cancel();
            await Task.WhenAll(pending.Values.Select(x => x.Done.Task));
        }
    }

    private static byte[] TransferReply(uint sequence, int actual, byte[] input)
    {
        byte[] response = new byte[HeaderLength + input.Length];
        BinaryPrimitives.WriteUInt32BigEndian(response, 3);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(4), sequence);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(24), actual);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(32), uint.MaxValue);
        input.CopyTo(response, HeaderLength);
        return response;
    }

    private static byte[] OperationReply(ushort code, uint status)
    {
        byte[] reply = new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(reply, Version);
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2), code);
        BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(4), status);
        return reply;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
