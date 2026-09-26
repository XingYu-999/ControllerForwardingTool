using System.Buffers.Binary;
using System.Text;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Protocol.Ns1;

namespace ControllerForwardingTool.VirtualDevice;

/// <summary>Experimental USB identity and control transfers for local USB/IP attach.</summary>
public sealed class UsbIpNs1Device
{
    public const string BusId = "1-1";
    public const ushort VendorId = 0x057E;
    public const ushort ProductId = 0x2009;
    private readonly Ns1ReportEncoder encoder = new();
    private readonly object gate = new();
    private readonly Queue<byte[]> pendingReplies = new();
    private ControllerState currentState = ControllerState.Neutral(DateTimeOffset.Now);
    private bool imuEnabled;
    private byte configuration;
    private byte idle;
    private byte playerLights;
    private long lastPublish = Environment.TickCount64;
    private long revision, readRevision = -1, lastInputRead;
    private readonly OutputWakeSignal inputChanged = new();
    public void NotifySettingsChanged() => inputChanged.Signal();


    // The USB and HID declarations are composed for this prototype. They are
    // not claimed to match the descriptor of a retail NS1 Pro controller.
    public static byte[] DeviceDescriptor { get; } =
    [0x12, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x40,
     0x7E, 0x05, 0x09, 0x20, 0x00, 0x01, 0x01, 0x02, 0x03, 0x01];

    public static byte[] ReportDescriptor { get; } = BuildReportDescriptor();

    public static byte[] ConfigurationDescriptor { get; } = BuildConfigurationDescriptor();

    public event Action<byte[]>? OutputReceived;

    public void Publish(ControllerState state)
    {
        lock (gate) { currentState = state; lastPublish = Environment.TickCount64; revision++; }
        inputChanged.Signal();
    }

    // One interrupt-IN consumer per imported connection. Command replies bypass pacing.
    public async Task<byte[]> ReadInputAsync(int maximumLength, Func<int> pushHz, CancellationToken token)
    {
        if (maximumLength <= 0) return [];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int hz = OutputRatePolicy.Resolve(pushHz(), VirtualControllerMode.Ns1Pro);
            lock (gate)
            {
                if (pendingReplies.Count > 0) return ReadInput(maximumLength);
                if (hz == 0 && (revision != readRevision || Environment.TickCount64 - lastPublish >= 250 && Environment.TickCount64 - lastInputRead >= 100))
                { readRevision = revision; lastInputRead = Environment.TickCount64; return ReadInput(maximumLength); }
            }
            if (hz > 0)
            {
                // Native high-resolution wait avoids Task.Delay(8)'s coarse timer rounding.
                using var clock = new HighResolutionPeriodicTimer(TimeSpan.FromSeconds(1.0 / hz));
                clock.WaitForNextTick(token);
                return ReadInput(maximumLength);
            }
            double remaining;
            lock (gate) remaining = Environment.TickCount64 - lastPublish >= 250
                ? Math.Clamp(100 - (Environment.TickCount64 - lastInputRead), 1, 100)
                : Math.Clamp(250 - (Environment.TickCount64 - lastPublish), 1, 250);
            await inputChanged.WaitAsync(TimeSpan.FromMilliseconds(remaining), token);
        }
    }

    public byte[] ReadInput(int maximumLength)
    {
        if (maximumLength <= 0) return [];
        lock (gate)
        {
            if (Environment.TickCount64 - lastPublish >= 250)
                currentState = ControllerState.Neutral(DateTimeOffset.Now);
            byte[] report = pendingReplies.Count > 0 ? pendingReplies.Dequeue() : encoder.Encode(currentState, imuEnabled);
            return report.AsSpan(0, Math.Min(maximumLength, report.Length)).ToArray();
        }
    }

    public byte[] HandleControl(ReadOnlySpan<byte> setup, ReadOnlySpan<byte> output)
    {
        if (setup.Length < 8) return [];
        byte type = setup[0], request = setup[1];
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(setup.Slice(2, 2));
        ushort requested = BinaryPrimitives.ReadUInt16LittleEndian(setup.Slice(6, 2));
        byte[] response = [];

        if ((type & 0x60) == 0 && request == 0x06) // GET_DESCRIPTOR
        {
            response = (value >> 8) switch
            {
                0x01 => DeviceDescriptor,
                0x02 => ConfigurationDescriptor,
                0x03 => StringDescriptor((byte)value),
                0x21 => ConfigurationDescriptor.AsSpan(18, 9).ToArray(),
                0x22 => ReportDescriptor,
                _ => []
            };
        }
        else if ((type & 0x60) == 0)
        {
            switch (request)
            {
                case 0x08: response = [configuration]; break; // GET_CONFIGURATION
                case 0x09: configuration = (byte)value; break; // SET_CONFIGURATION
                case 0x00: response = [0, 0]; break; // GET_STATUS
                case 0x0A: response = [0]; break; // GET_INTERFACE
                // SET_ADDRESS, SET_INTERFACE and CLEAR_FEATURE need no payload.
            }
        }
        else if ((type & 0x60) == 0x20) // HID class
        {
            switch (request)
            {
                case 0x01: response = ReadInput(requested); break; // GET_REPORT
                case 0x02: response = [idle]; break; // GET_IDLE
                case 0x09: // SET_REPORT
                    byte reportId = (byte)value;
                    if (output.Length > 0 && reportId != 0 && output[0] != reportId)
                    {
                        byte[] framed = new byte[output.Length + 1];
                        framed[0] = reportId;
                        output.CopyTo(framed.AsSpan(1));
                        HandleInterruptOut(framed);
                    }
                    else HandleInterruptOut(output.ToArray());
                    break;
                case 0x0A: idle = (byte)(value >> 8); break; // SET_IDLE
                case 0x03: response = [1]; break; // GET_PROTOCOL
            }
        }
        return response.AsSpan(0, Math.Min(response.Length, requested)).ToArray();
    }

    public void HandleInterruptOut(byte[] payload)
    {
        if (payload.Length == 0) return;
        OutputReceived?.Invoke(payload);
        byte[]? reply = payload[0] switch
        {
            0x80 when payload.Length >= 2 => MakeUsbReply(payload[1]),
            0x01 when payload.Length >= 11 => MakeSubcommandReply(payload),
            _ => null
        };
        if (reply is not null)
        {
            lock (gate)
            {
                if (pendingReplies.Count == 64) pendingReplies.Dequeue();
                pendingReplies.Enqueue(reply);
            }
            inputChanged.Signal();
        }
    }

    public void ResetSession()
    {
        lock (gate) { pendingReplies.Clear(); configuration = 0; idle = 0; playerLights = 0; imuEnabled = false; readRevision = -1; }
    }

    private static byte[] MakeUsbReply(byte command)
    {
        byte[] reply = new byte[64];
        reply[0] = 0x81;
        reply[1] = command;
        if (command == 0x01)
        {
            reply[3] = 0x03; // Observed Pro controller type.
            reply[4] = 0x02; // Locally administered synthetic address.
            reply[9] = 0x01;
        }
        return reply;
    }

    private byte[] MakeSubcommandReply(ReadOnlySpan<byte> request)
    {
        byte subcommand = request[10];
        byte[] reply = new byte[64];
        lock (gate)
        {
            encoder.Encode(currentState, false).AsSpan(1, 11).CopyTo(reply.AsSpan(1));
            if (subcommand == 0x40 && request.Length > 11) imuEnabled = request[11] != 0;
        }
        reply[0] = 0x21;
        reply[14] = subcommand;
        reply[13] = subcommand switch
        {
            0x02 => 0x82, // device info
            0x03 when request.Length > 11 && request[11] == 0x30 => 0x80,
            0x40 or 0x48 when request.Length > 11 => 0x80,
            0x04 or 0x08 or 0x30 or 0x38 or 0x41 => 0x80,
            0x10 when request.Length >= 16 && request[15] <= 29 => 0x90,
            0x31 => 0xB0,
            _ => 0x00 // Unsupported; do not falsely acknowledge.
        };
        if (subcommand == 0x02)
        {
            reply[15] = 0x04;
            reply[16] = 0x33;
            reply[17] = 0x03;
            reply[18] = 0x02;
            new byte[] { 0x02, 0x4E, 0x53, 0x32, 0x01, 0x01 }.CopyTo(reply, 19);
            reply[25] = 1;
            reply[26] = 1;
        }
        if (subcommand == 0x30 && request.Length > 11) playerLights = request[11];
        if (subcommand == 0x31) reply[15] = playerLights;
        if (subcommand == 0x10 && reply[13] == 0x90)
        {
            uint address = BinaryPrimitives.ReadUInt32LittleEndian(request.Slice(11, 4));
            int length = request[15];
            request.Slice(11, 5).CopyTo(reply.AsSpan(15));
            ReadSpi(address, reply.AsSpan(20, length));
        }
        return reply;
    }

    private static void ReadSpi(uint address, Span<byte> data)
    {
        // Synthetic calibration matches our full 12-bit output, not the physical NS2 calibration.
        data.Fill(0xFF);
        byte[] sticks = Convert.FromHexString("FFF77F000880000880000880000880FFF77F");
        byte[] imu = new byte[24];
        for (int i = 0; i < 3; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(imu.AsSpan(6 + i * 2), 16384);
            BinaryPrimitives.WriteInt16LittleEndian(imu.AsSpan(18 + i * 2), 13371);
        }
        CopySpiBlock(address, data, 0x6020, imu);
        CopySpiBlock(address, data, 0x603D, sticks);
        CopySpiBlock(address, data, 0x6050, [0x32, 0x32, 0x32, 0xE6, 0xE6, 0xE6, 0x32, 0x32, 0x32, 0x32, 0x32, 0x32, 1]);
        // Non-zero dead zone and range ratio; erased user calibration uses factory defaults.
        CopySpiBlock(address, data, 0x6080, Convert.FromHexString("506000000000000000000000000000000000"));
        CopySpiBlock(address, data, 0x6098, Convert.FromHexString("506000000000000000000000000000000000"));


    }

    private static void CopySpiBlock(uint address, Span<byte> destination, uint start, byte[] block)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            ulong location = (ulong)address + (uint)i;
            if (location >= start && location - start < (ulong)block.Length)
                destination[i] = block[(int)(location - start)];
        }
    }

    public static byte[] DeviceRecord()
    {
        byte[] record = new byte[312];
        WriteAscii(record, 0, 256, "/virtual/ns1-pro");
        WriteAscii(record, 256, 32, BusId);
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(288), 1); // busnum
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(292), 1); // devnum
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(296), 2); // full speed
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(300), VendorId);
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(302), ProductId);
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(304), 0x0100);
        record[309] = 1; // active configuration
        record[310] = 1; // one configuration
        record[311] = 1; // one interface
        return record;
    }

    private static byte[] BuildConfigurationDescriptor()
    {
        byte[] descriptor =
        [
            9, 2, 41, 0, 1, 1, 0, 0x80, 50,
            9, 4, 0, 0, 2, 3, 0, 0, 0,
            9, 0x21, 0x11, 0x01, 0, 1, 0x22, 0, 0,
            7, 5, 0x81, 3, 64, 0, 1,
            7, 5, 0x01, 3, 64, 0, 8
        ];
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(25, 2),
            checked((ushort)ReportDescriptor.Length));
        return descriptor;
    }

    private static byte[] BuildReportDescriptor()
    {
        List<byte> descriptor = [0x05, 0x01, 0x09, 0x05, 0xA1, 0x01, 0x06, 0x00, 0xFF];
        foreach (byte id in new byte[] { 0x30, 0x21, 0x81, 0x3F })
            descriptor.AddRange([0x85, id, 0x09, 0x01, 0x15, 0x00, 0x26, 0xFF, 0x00,
                0x75, 0x08, 0x95, 0x3F, 0x81, 0x02]);
        foreach (byte id in new byte[] { 0x01, 0x10, 0x80, 0x82 })
            descriptor.AddRange([0x85, id, 0x09, 0x02, 0x15, 0x00, 0x26, 0xFF, 0x00,
                0x75, 0x08, 0x95, 0x3F, 0x91, 0x02]);
        descriptor.Add(0xC0);
        return descriptor.ToArray();
    }

    private static byte[] StringDescriptor(byte index)
    {
        if (index == 0) return [4, 3, 0x09, 0x04];
        string? value = index switch
        {
            1 => AppIdentity.EnglishName,
            2 => "Pro Controller",
            3 => "NS2PROWIN11-LOCAL",
            _ => null
        };
        if (value is null) return [];
        byte[] utf16 = Encoding.Unicode.GetBytes(value);
        byte[] result = new byte[utf16.Length + 2];
        result[0] = (byte)result.Length;
        result[1] = 3;
        utf16.CopyTo(result.AsSpan(2));
        return result;
    }

    private static void WriteAscii(Span<byte> destination, int offset, int length, string value)
    {
        Encoding.ASCII.GetBytes(value.AsSpan(), destination.Slice(offset, length - 1));
    }
}
