using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ControllerForwardingTool.Usb;

// Short-lived command interface ownership. Called only while SDL has released Switch 2 USB.
internal sealed class Ns2UsbSession : IDisposable
{
    private IntPtr context, handle;
    private byte inputEndpoint, outputEndpoint;
    private bool claimed;
    private readonly CancellationToken token;
    private Ns2UsbSession(CancellationToken token) => this.token = token;

    internal static Ns2UsbSession Open(string serial, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(serial) || serial.StartsWith("NS2PROWIN11-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("无法唯一识别实体手柄，请重新插入 USB 后再试");
        var session = new Ns2UsbSession(token);
        try
        {
            Check(Native.libusb_init(out session.context));
            nint count = Native.libusb_get_device_list(session.context, out var list);
            if (count < 0) throw new IOException("无法枚举 USB 设备");
            try
            {
                for (int i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var device = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                    if (Native.libusb_get_device_descriptor(device, out var descriptor) < 0 || descriptor.Vendor != 0x057E || descriptor.Product != 0x2069) continue;
                    if (Native.libusb_open(device, out session.handle) < 0) continue;
                    try
                    {
                        session.FindEndpoints(device);
                        Check(Native.libusb_claim_interface(session.handle, 1)); session.claimed = true;
                        var bytes = Ns2UsbProtocol.ReadMemory(session.Exchange, 0x13002, 16);
                        string actual = Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ', '\u00ff');
                        if (actual == serial) return session;
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
                    session.CloseHandle();
                }
            }
            finally { Native.libusb_free_device_list(list, 1); }
            throw new IOException("无法打开所选手柄的 USB 管理接口；请关闭占用手柄的软件，检查 USB 连接后重试");
        }
        catch { session.Dispose(); throw; }
    }

    private void FindEndpoints(IntPtr device)
    {
        Check(Native.libusb_get_config_descriptor(device, 0, out var configPointer));
        try
        {
            var config = Marshal.PtrToStructure<ConfigDescriptor>(configPointer);
            for (int i = 0; i < config.InterfaceCount; i++)
            {
                var iface = Marshal.PtrToStructure<Interface>(config.Interfaces + i * Marshal.SizeOf<Interface>());
                for (int j = 0; j < iface.Count; j++)
                {
                    var alt = Marshal.PtrToStructure<InterfaceDescriptor>(iface.Alternatives + j * Marshal.SizeOf<InterfaceDescriptor>());
                    if (alt.Number != 1 || alt.Alternative != 0) continue;
                    byte input = 0, output = 0;
                    for (int k = 0; k < alt.EndpointCount; k++)
                    {
                        var ep = Marshal.PtrToStructure<EndpointDescriptor>(alt.Endpoints + k * Marshal.SizeOf<EndpointDescriptor>());
                        if ((ep.Attributes & 3) != 2) continue;
                        if ((ep.Address & 0x80) != 0) input = ep.Address; else output = ep.Address;
                    }
                    if (input != 0 && output != 0) { inputEndpoint = input; outputEndpoint = output; return; }
                }
            }
            throw new IOException("未找到 NS2 Pro USB 命令端点");
        }
        finally { Native.libusb_free_config_descriptor(configPointer); }
    }

    internal byte[] Exchange(byte[] packet, int minimumLength)
    {
        token.ThrowIfCancellationRequested();
        // Drain stale command replies before sending; never retry a mutation after an ambiguous timeout.
        byte[] chunk = new byte[64];
        for (int i = 0; i < 8; i++)
        {
            int rc = Native.libusb_bulk_transfer(handle, inputEndpoint, chunk, chunk.Length, out _, 5);
            if (rc == -7) break;
            Check(rc);
        }
        Check(Native.libusb_bulk_transfer(handle, outputEndpoint, packet, packet.Length, out int written, 1000));
        if (written != packet.Length) throw new IOException("USB 命令未完整写入，操作结果未知");
        List<byte> response = [];
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(3))
        {
            token.ThrowIfCancellationRequested();
            int rc = Native.libusb_bulk_transfer(handle, inputEndpoint, chunk, chunk.Length, out int received, 100);
            if (rc == -7) continue;
            Check(rc);
            if (received == 0) continue;
            response.AddRange(chunk.AsSpan(0, received).ToArray());
            if (response.Count >= minimumLength) return response.ToArray();
        }
        throw new TimeoutException("USB 回复超时；写入结果尚未确认，请刷新注册状态");
    }

    private static void Check(int result) { if (result < 0) throw new IOException($"USB 接口操作失败（{result}）"); }
    private void CloseHandle()
    {
        if (handle == IntPtr.Zero) return;
        if (claimed) Native.libusb_release_interface(handle, 1);
        claimed = false; Native.libusb_close(handle); handle = IntPtr.Zero;
    }
    public void Dispose() { CloseHandle(); if (context != IntPtr.Zero) { Native.libusb_exit(context); context = IntPtr.Zero; } }

    [StructLayout(LayoutKind.Sequential)] private struct DeviceDescriptor
    {
        public byte Length, Type; public ushort Usb; public byte Class, Subclass, Protocol, PacketSize;
        public ushort Vendor, Product, Version; public byte Manufacturer, ProductString, Serial, Configurations;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ConfigDescriptor
    {
        public byte Length, Type; public ushort TotalLength; public byte InterfaceCount, Value, String, Attributes, Power;
        public IntPtr Interfaces, Extra; public int ExtraLength;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Interface { public IntPtr Alternatives; public int Count; }
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceDescriptor
    {
        public byte Length, Type, Number, Alternative, EndpointCount, Class, Subclass, Protocol, String;
        public IntPtr Endpoints, Extra; public int ExtraLength;
    }
    [StructLayout(LayoutKind.Sequential)] private struct EndpointDescriptor
    {
        public byte Length, Type, Address, Attributes; public ushort PacketSize; public byte Interval, Refresh, SyncAddress;
        public IntPtr Extra; public int ExtraLength;
    }
    private static class Native
    {
        private const string Library = "libusb-1.0.dll";
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern int libusb_init(out IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern void libusb_exit(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern nint libusb_get_device_list(IntPtr context, out IntPtr list);
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern void libusb_free_device_list(IntPtr list, int unref);
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern int libusb_get_device_descriptor(IntPtr device, out DeviceDescriptor descriptor);
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern int libusb_get_config_descriptor(IntPtr device, byte index, out IntPtr descriptor);
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern void libusb_free_config_descriptor(IntPtr descriptor);
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern int libusb_open(IntPtr device, out IntPtr handle);
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern void libusb_close(IntPtr handle);
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern int libusb_claim_interface(IntPtr handle, int number);
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern int libusb_release_interface(IntPtr handle, int number);
        [DllImport(Library, CallingConvention = CallingConvention.Winapi)] internal static extern int libusb_bulk_transfer(IntPtr handle, byte endpoint, [In, Out] byte[] data, int length, out int transferred, uint timeout);
    }
}
