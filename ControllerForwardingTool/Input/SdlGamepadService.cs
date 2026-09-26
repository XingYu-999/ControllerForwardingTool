using System.Runtime.InteropServices;
using System.Numerics;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.Input;

public sealed record GamepadDevice(uint Id, string Name, ushort Vendor, ushort Product, bool Standard, ControllerLayout Layout = ControllerLayout.Generic)
{
    public string Serial { get; init; } = "";
    public bool Nintendo => Layout.IsNintendo();
    public override string ToString() => $"{Name} · {Vendor:X4}:{Product:X4}";
}

public sealed record GamepadSnapshot(ControllerButtons Buttons, double[] Axes, bool[] RawButtons,
    byte[] Hats, int Battery, bool Standard, float[]? Accel, float[]? Gyro, bool[] SupportedButtons)
{
    public TriggerLevels Triggers => Standard ? TriggerLevels.FromStandardAxes(Axes) : default;
    public double? InputRateHz { get; init; }
    public bool RateUsesSensorReports { get; init; }
}

/// <summary>SDL owns HID initialization (including Switch Pro USB/BT), mappings and hotplug.
/// All calls stay on the dedicated thread that initialized SDL; Avalonia never calls native HID functions.</summary>
public sealed class SdlGamepadService : IDisposable
{
    private readonly Dictionary<uint, (IntPtr Pad, IntPtr Joystick)> handles = [];
    private readonly Dictionary<uint, InputReportRate> inputRates = [];
    private readonly HashSet<uint> sensorReportDevices = [];
    private bool initialized;
    private IntPtr libusb;
    public string Status { get; private set; } = "未初始化";
    public event Action<uint, MotionSample>? MotionSampleReceived;

    public SdlGamepadService(bool enableNs2Usb = true)
    {
        try
        {
            // SDL loads libusb by filename. Pin the bundled DLL explicitly, including when hosted by dotnet.exe.
            libusb = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "libusb-1.0.dll"));
            Native.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            Native.SDL_SetHint("SDL_JOYSTICK_HIDAPI_SWITCH", "1");
            Native.SDL_SetHint("SDL_JOYSTICK_HIDAPI_SWITCH2", enableNs2Usb ? "1" : "0");
            // The real NS2 is owned by our BLE transport; SDL only opens its USB identity.
            initialized = Native.SDL_Init(0x00002000); // SDL_INIT_GAMEPAD (includes joystick/events)
            Status = initialized ? "SDL 3 · 自动发现 USB / 已配对蓝牙手柄" : Error();
        }
        catch (Exception ex) { Status = $"手柄运行库不可用：{ex.Message}"; }
    }

    // WinUSB's NS2 initialization interface is exclusive. Release it when leaving the tester for a game.
    public void EnableNs2Usb(bool enabled)
    {
        if (initialized) Native.SDL_SetHint("SDL_JOYSTICK_HIDAPI_SWITCH2", enabled ? "1" : "0");
    }

    public IReadOnlyList<GamepadDevice> Refresh()
    {
        if (!initialized) return [];
        Update();
        IntPtr list = Native.SDL_GetJoysticks(out int count);
        List<GamepadDevice> result = [];
        try
        {
            for (int i = 0; i < count; i++)
            {
                uint id = unchecked((uint)Marshal.ReadInt32(list, i * 4));
                bool standard = Native.SDL_IsGamepad(id);
                if (standard && handles.TryGetValue(id, out var existing) && existing.Pad == IntPtr.Zero) Close(id);
                if (!handles.ContainsKey(id))
                {
                    IntPtr pad = standard ? Native.SDL_OpenGamepad(id) : IntPtr.Zero;
                    IntPtr joystick = pad != IntPtr.Zero ? Native.SDL_GetGamepadJoystick(pad) : Native.SDL_OpenJoystick(id);
                    if (joystick == IntPtr.Zero) continue;
                    handles[id] = (pad, joystick);
                    inputRates[id] = new();
                    if (pad != IntPtr.Zero)
                        foreach (int sensor in new[] { 1, 2 })
                            if (Native.SDL_GamepadHasSensor(pad, sensor)) Native.SDL_SetGamepadSensorEnabled(pad, sensor, true);
                }
                var h = handles[id];
                ushort vendor = Native.SDL_GetJoystickVendor(h.Joystick), product = Native.SDL_GetJoystickProduct(h.Joystick);
                result.Add(new(id, Marshal.PtrToStringUTF8(Native.SDL_GetJoystickName(h.Joystick)) ?? "USB/HID 手柄",
                    vendor, product, h.Pad != IntPtr.Zero, ControllerLayouts.Identify(vendor, product, h.Pad == IntPtr.Zero ? 0 : Native.SDL_GetGamepadType(h.Pad)))
                    { Serial = Marshal.PtrToStringUTF8(Native.SDL_GetJoystickSerial(h.Joystick)) ?? "" });
            }
        }
        finally { Native.SDL_free(list); }
        foreach (uint id in handles.Keys.Except(result.Select(x => x.Id)).ToArray()) Close(id);
        return result;
    }

    public void Update()
    {
        if (!initialized) return;
        Native.SDL_PumpEvents(); Native.SDL_UpdateJoysticks();
        while (Native.SDL_PeepEvents(out var ev, 1, 2, 0x600, 0x65a) > 0)
        {
            // Axis/button events from the same report share a timestamp. Sensor
            // sub-samples also share that receipt timestamp (NS1 has three).
            // UPDATE_COMPLETE is deliberately excluded: SDL coalesces it per poll.
            bool sensorReport = ev.Type == 0x659 && ev.Sensor == 2;
            if (sensorReport || ev.Type is 0x600 or 0x601 or 0x602 or 0x603)
            {
                if (inputRates.TryGetValue(ev.Which, out var rate))
                {
                    if (sensorReport && sensorReportDevices.Add(ev.Which))
                        inputRates[ev.Which] = rate = new();
                    if (sensorReport || !sensorReportDevices.Contains(ev.Which))
                        rate.Observe(ev.Timestamp, GyroCalibration.Now);
                }
            }
            if (!sensorReport) continue;
            if (ev.Sensor != 2 || !handles.TryGetValue(ev.Which, out var h) || h.Pad == IntPtr.Zero) continue;
            float[] accel = new float[3];
            if (!Native.SDL_GetGamepadSensorData(h.Pad, 1, accel, 3)) continue;
            double age = Math.Max(0, ((double)Native.SDL_GetTicksNS() - ev.Timestamp) / 1e9);
            MotionSampleReceived?.Invoke(ev.Which, new(GyroCalibration.Now - age,
                new Vector3(accel[0], accel[1], accel[2]) / 9.80665f,
                new Vector3(ev.X, ev.Y, ev.Z) * (180f / MathF.PI)));
        }
        Native.SDL_FlushEvents(0x600, 0x8ff);
    }

    public GamepadSnapshot? Read(GamepadDevice device)
    {
        if (!handles.TryGetValue(device.Id, out var h) || !Native.SDL_JoystickConnected(h.Joystick)) return null;
        bool standard = h.Pad != IntPtr.Zero;
        int buttons = standard ? 26 : Math.Clamp(Native.SDL_GetNumJoystickButtons(h.Joystick), 0, 128);
        int axes = standard ? 6 : Math.Clamp(Native.SDL_GetNumJoystickAxes(h.Joystick), 0, 32);
        bool[] raw = new bool[buttons];
        bool[] supported = new bool[buttons];
        double[] values = new double[axes];
        for (int i = 0; i < buttons; i++)
        {
            supported[i] = !standard || Native.SDL_GamepadHasButton(h.Pad, i);
            raw[i] = standard ? Native.SDL_GetGamepadButton(h.Pad, i) : Native.SDL_GetJoystickButton(h.Joystick, i);
        }
        for (int i = 0; i < axes; i++)
        {
            short value = standard ? Native.SDL_GetGamepadAxis(h.Pad, i) : Native.SDL_GetJoystickAxis(h.Joystick, i);
            values[i] = value / (value < 0 ? 32768.0 : 32767.0);
        }
        byte[] hats = new byte[Math.Clamp(Native.SDL_GetNumJoystickHats(h.Joystick), 0, 8)];
        for (int i = 0; i < hats.Length; i++) hats[i] = Native.SDL_GetJoystickHat(h.Joystick, i);
        ControllerButtons mapped = ControllerButtons.None;
        if (standard)
        {
            ControllerButtons[] layout = [device.Nintendo ? ControllerButtons.B : ControllerButtons.A,
                device.Nintendo ? ControllerButtons.A : ControllerButtons.B,
                device.Nintendo ? ControllerButtons.Y : ControllerButtons.X,
                device.Nintendo ? ControllerButtons.X : ControllerButtons.Y,
                ControllerButtons.Minus, ControllerButtons.Home, ControllerButtons.Plus,
                ControllerButtons.LeftStick, ControllerButtons.RightStick, ControllerButtons.L, ControllerButtons.R,
                ControllerButtons.Up, ControllerButtons.Down, ControllerButtons.Left, ControllerButtons.Right,
                ControllerButtons.Capture, ControllerButtons.GR, ControllerButtons.GL];
            for (int i = 0; i < layout.Length; i++) if (raw[i]) mapped |= layout[i];
            if (device.Nintendo && device.Product == 0x2069 && raw[21]) mapped |= ControllerButtons.C;
            if (device.Layout.IsPlayStation() && raw[20]) mapped |= ControllerButtons.Touchpad;
            if (values[4] > 0.5) mapped |= ControllerButtons.ZL;
            if (values[5] > 0.5) mapped |= ControllerButtons.ZR;
        }
        // Unknown HID layouts keep raw indices; never pretend their button order is standard.
        Native.SDL_GetJoystickPowerInfo(h.Joystick, out int battery);
        return new(mapped, values, raw, hats, battery, standard, Sensor(1), Sensor(2), supported)
        {
            InputRateHz = inputRates[device.Id].Read(GyroCalibration.Now),
            RateUsesSensorReports = sensorReportDevices.Contains(device.Id)
        };

        float[]? Sensor(int type)
        {
            if (!standard || !Native.SDL_GamepadSensorEnabled(h.Pad, type)) return null;
            float[] data = new float[3];
            return Native.SDL_GetGamepadSensorData(h.Pad, type, data, 3) ? data : null;
        }
    }

    public string Rumble(GamepadDevice? device, RumbleSettings settings)
    {
        if (device is null || !handles.TryGetValue(device.Id, out var h)) return "请先选择已连接手柄";
        return Native.SDL_RumbleJoystick(h.Joystick, settings.Low, settings.High, settings.DurationMs)
            ? $"已发送 {settings.DurationMs} ms 震动 · 低频 {settings.Low / 655.35:F0}% / 高频 {settings.High / 655.35:F0}%"
            : $"设备不支持震动或发送失败：{Error()}";
    }
    public void StopRumble(GamepadDevice? device)
    {
        if (device is not null && handles.TryGetValue(device.Id, out var h)) Native.SDL_RumbleJoystick(h.Joystick, 0, 0, 0);
    }

    private static string Error() => Marshal.PtrToStringUTF8(Native.SDL_GetError()) ?? "SDL 错误";
    private void Close(uint id)
    {
        var h = handles[id];
        Native.SDL_RumbleJoystick(h.Joystick, 0, 0, 0);
        if (h.Pad != IntPtr.Zero) Native.SDL_CloseGamepad(h.Pad); else Native.SDL_CloseJoystick(h.Joystick);
        handles.Remove(id);
        inputRates.Remove(id);
        sensorReportDevices.Remove(id);
    }
    public void Dispose()
    {
        foreach (uint id in handles.Keys.ToArray()) Close(id);
        if (initialized) Native.SDL_QuitSubSystem(0x00002000);
        initialized = false;
        if (libusb != IntPtr.Zero) { NativeLibrary.Free(libusb); libusb = IntPtr.Zero; }
    }

    private static class Native
    {
        private const string Lib = "SDL3.dll";
        [StructLayout(LayoutKind.Explicit, Size = 128)]
        public struct SensorEvent
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public ulong Timestamp;
            [FieldOffset(16)] public uint Which;
            [FieldOffset(20)] public int Sensor;
            [FieldOffset(24)] public float X;
            [FieldOffset(28)] public float Y;
            [FieldOffset(32)] public float Z;
        }
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_PeepEvents(out SensorEvent ev, int count, int action, uint minimum, uint maximum);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ulong SDL_GetTicksNS();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetGamepadType(IntPtr pad);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_Init(uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_QuitSubSystem(uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetError();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_PumpEvents();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_FlushEvents(uint min, uint max);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_UpdateJoysticks();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetJoysticks(out int count);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_free(IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_IsGamepad(uint id);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_OpenGamepad(uint id);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_OpenJoystick(uint id);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetGamepadJoystick(IntPtr pad);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_CloseGamepad(IntPtr pad);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_CloseJoystick(IntPtr joystick);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetJoystickName(IntPtr joystick);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetJoystickSerial(IntPtr joystick);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_GetJoystickVendor(IntPtr joystick);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_GetJoystickProduct(IntPtr joystick);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_JoystickConnected(IntPtr joystick);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetNumJoystickButtons(IntPtr joystick);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetNumJoystickAxes(IntPtr joystick);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetNumJoystickHats(IntPtr joystick);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_GetGamepadButton(IntPtr pad, int button);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_GamepadHasButton(IntPtr pad, int button);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_GetJoystickButton(IntPtr joystick, int button);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern short SDL_GetGamepadAxis(IntPtr pad, int axis);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern short SDL_GetJoystickAxis(IntPtr joystick, int axis);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GetJoystickHat(IntPtr joystick, int hat);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetJoystickPowerInfo(IntPtr joystick, out int percent);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_GamepadHasSensor(IntPtr pad, int type);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_SetGamepadSensorEnabled(IntPtr pad, int type, [MarshalAs(UnmanagedType.I1)] bool enabled);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_GamepadSensorEnabled(IntPtr pad, int type);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_GetGamepadSensorData(IntPtr pad, int type, [Out] float[] values, int count);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_RumbleJoystick(IntPtr joystick, ushort low, ushort high, uint duration);
    }
}
