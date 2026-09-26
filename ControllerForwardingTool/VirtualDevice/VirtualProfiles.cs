using System.Buffers.Binary;
using System.Globalization;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.VirtualDevice;

public enum VirtualControllerMode { Ns2Pro, Ns1Pro, DualSense, Xbox360, DualSenseEdge }

public sealed record VirtualProfile(VirtualControllerMode Mode, string Name, string DeviceType,
    ushort Vendor, ushort Product, int InputSize, int FeedbackSize, string Image, string Accent, string Description)
{
    public string Identity => $"{Vendor:X4}:{Product:X4}";
    public string Protocol => $"{DeviceType} · {Identity}";
    public static IReadOnlyList<VirtualProfile> All { get; } = [
        new(VirtualControllerMode.DualSense, "PS5", "dualsensehaptic", 0x054c, 0x0ce6, 33, 388, "dualsense", "#C85C47", "DualSense · 四声道 HD 震动 / 体感"),
        new(VirtualControllerMode.Ns2Pro, "NS2 PRO", "ns2pro", 0x057e, 0x2069, 28, 34, "switch2-pro", "#2563EB", "Nintendo · 原生震动 / C / GL / GR"),
        new(VirtualControllerMode.Xbox360, "XBOX", "xbox360", 0x045e, 0x028e, 20, 2, "xbox-wireless", "#3D7B40", "XInput · 双马达 / 广泛游戏兼容"),
        new(VirtualControllerMode.DualSenseEdge, "PS5 EDGE", "dualsenseedge", 0x054c, 0x0df2, 33, 6, "dualsense-edge", "#8355B5", "DualSense Edge · 背键 / 普通震动"),
        new(VirtualControllerMode.Ns1Pro, "NS1 PRO", "ns1pro", 0x057e, 0x2009, 64, 0, "switch-pro", "#0D9488", "Switch Pro · 按键 / 摇杆 / 体感 / 震动转码")
    ];
    public static VirtualProfile Get(VirtualControllerMode mode) => All.First(p => p.Mode == mode);
    public bool Matches(ViiperDevice device) => device.Type == DeviceType && ParseId(device.Vid) == Vendor && ParseId(device.Pid) == Product;
    private static ushort ParseId(string value) => ushort.TryParse(value.Replace("0x", "", StringComparison.OrdinalIgnoreCase),
        NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ? id : (ushort)0;
}

/// <summary>VIIPER's packed wire format, adapted from the upstream VirtualPadPackets.cs.
/// ControllerState names are Nintendo labels; PS/Xbox mapping follows physical positions.</summary>
public static class VirtualReportEncoder
{
    public static byte[] Encode(VirtualControllerMode mode, ControllerState state, BridgeOptions options)
    {
        if (mode == VirtualControllerMode.Ns2Pro) return Ns2VirtualReport.Encode(state);
        if (mode == VirtualControllerMode.Ns1Pro) throw new ArgumentException("NS1 uses its HID report server");
        byte[] result = new byte[VirtualProfile.Get(mode).InputSize];
        bool Has(ControllerButtons b) => (state.Buttons & b) != 0;
        uint buttons = 0;
        void Add(ControllerButtons button, uint bit) { if (Has(button)) buttons |= bit; }
        if (mode == VirtualControllerMode.Xbox360)
        {
            Add(ControllerButtons.Up, 1); Add(ControllerButtons.Down, 2); Add(ControllerButtons.Left, 4); Add(ControllerButtons.Right, 8);
            Add(ControllerButtons.Plus, 0x10); Add(ControllerButtons.Minus, 0x20);
            Add(ControllerButtons.LeftStick, 0x40); Add(ControllerButtons.RightStick, 0x80);
            Add(ControllerButtons.L, 0x100); Add(ControllerButtons.R, 0x200); Add(ControllerButtons.Home, 0x400);
            Add(ControllerButtons.B, 0x1000); Add(ControllerButtons.A, 0x2000); Add(ControllerButtons.Y, 0x4000); Add(ControllerButtons.X, 0x8000);
            BinaryPrimitives.WriteUInt32LittleEndian(result, buttons);
            result[4] = state.LeftTriggerValue;
            result[5] = state.RightTriggerValue;
            Write16(result, 6, Axis16(state.LeftX)); Write16(result, 8, Axis16(state.LeftY));
            Write16(result, 10, Axis16(state.RightX)); Write16(result, 12, Axis16(state.RightY));
            return result;
        }
        result[0] = Axis8(state.LeftX); result[1] = Axis8(state.LeftY, true);
        result[2] = Axis8(state.RightX); result[3] = Axis8(state.RightY, true);
        Add(ControllerButtons.Y, 0x10); Add(ControllerButtons.B, 0x20); Add(ControllerButtons.A, 0x40); Add(ControllerButtons.X, 0x80);
        Add(ControllerButtons.L, 0x100); Add(ControllerButtons.R, 0x200); Add(ControllerButtons.ZL, 0x400); Add(ControllerButtons.ZR, 0x800);
        Add(ControllerButtons.Minus, 0x1000); Add(ControllerButtons.Plus, 0x2000);
        Add(ControllerButtons.LeftStick, 0x4000); Add(ControllerButtons.RightStick, 0x8000);
        Add(ControllerButtons.Home, 0x10000); Add(ControllerButtons.Capture | ControllerButtons.Touchpad, 0x20000);
        if (mode == VirtualControllerMode.DualSenseEdge) { Add(ControllerButtons.GL, 0x00400000); Add(ControllerButtons.GR, 0x00800000); }
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), buttons);
        result[8] = (byte)((Has(ControllerButtons.Up) ? 1 : 0) | (Has(ControllerButtons.Down) ? 2 : 0) |
            (Has(ControllerButtons.Left) ? 4 : 0) | (Has(ControllerButtons.Right) ? 8 : 0));
        result[9] = state.LeftTriggerValue;
        result[10] = state.RightTriggerValue;
        Write16(result, 21, Clamp16(state.GyroX * options.GyroPitch * (options.InvertPitch ? -1 : 1)));
        Write16(result, 23, Clamp16(state.GyroZ * options.GyroYaw * (options.InvertYaw ? -1 : 1)));
        Write16(result, 25, Clamp16(-state.GyroY * options.GyroRoll * (options.InvertRoll ? -1 : 1)));
        Write16(result, 27, Clamp16(state.AccelX * 2)); Write16(result, 29, Clamp16(state.AccelZ * 2));
        Write16(result, 31, Clamp16(-state.AccelY * 2));
        return result;
    }
    public static short Axis16(ushort value) => Clamp16((Math.Clamp((int)value, 0, 4095) - 2048) * (value >= 2048 ? 32767.0 / 2047 : 16.0));
    private static byte Axis8(ushort value, bool invert = false)
    {
        double axis = Axis16(value) * (invert ? -1.0 : 1.0);
        return unchecked((byte)(sbyte)Math.Clamp(Math.Round(axis * (axis >= 0 ? 127.0 / 32767 : 128.0 / 32768)), -128, 127));
    }
    private static short Clamp16(double value) => (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
    private static void Write16(byte[] data, int offset, short value) => BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset), value);
}
