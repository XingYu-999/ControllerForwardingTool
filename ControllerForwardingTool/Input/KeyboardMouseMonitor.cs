using System.Diagnostics;
using System.Runtime.InteropServices;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.Input;

/// <summary>Samples only while a keyboard route is running. F8 captures/releases; focus loss releases.</summary>
public sealed class KeyboardMouseMonitor : IDisposable
{
    private readonly object gate = new();
    private readonly Timer? timer;
    private readonly IKeyboardMousePlatform platform;
    private readonly Action<ControllerState> publish;
    private bool enabled, captured, f8Down, disposed;
    private MouseEmulationMode mode;
    private (int X, int Y) anchor;
    private IntPtr foreground;
    private long previous;
    private readonly HashSet<int> ignoredUntilRelease = [];
    public bool IsCaptured { get { lock (gate) return captured; } }
    private IReadOnlySet<int> pressedKeys = new HashSet<int>();
    public IReadOnlySet<int> PressedKeys => Volatile.Read(ref pressedKeys);
    private ControllerState latest = ControllerState.Neutral(DateTimeOffset.MinValue);
    public ControllerState Latest => Volatile.Read(ref latest);
    private void Publish(ControllerState state) { Volatile.Write(ref latest, state); publish(state); }

    public KeyboardMouseMonitor(Action<ControllerState> publish) : this(publish, new WindowsPlatform(), true) { }
    internal KeyboardMouseMonitor(Action<ControllerState> publish, IKeyboardMousePlatform platform, bool startTimer)
    {
        this.publish = publish;
        this.platform = platform;
        if (startTimer) timer = new Timer(_ => Tick(), null, 0, 8);
    }
    public void Configure(bool enabled, MouseEmulationMode mode)
    {
        lock (gate)
        {
            if (disposed) return;
            if (this.enabled != enabled || this.mode != mode) Release();
            if (!this.enabled && enabled) f8Down = Down(0x77);
            this.enabled = enabled; this.mode = mode;
        }
    }
    /// <summary>Starts the same real forwarding path as F8, without forwarding the UI activation click/key.</summary>
    public bool TryStartCapture()
    {
        lock (gate)
        {
            if (!enabled || disposed || ReleaseKeyDown()) return false;
            if (captured) return true;
            f8Down = Down(0x77);
            return TryCapture(ignoreHeldKeys: true);
        }
    }
    public void StopCapture()
    {
        lock (gate)
        {
            if (disposed) return;
            f8Down = Down(0x77);
            Release();
        }
    }
    private bool TryCapture(bool ignoreHeldKeys)
    {
        var window = platform.Foreground;
        if (window == IntPtr.Zero || !platform.TryGetCaptureAnchor(out anchor) ||
            !platform.SetCursor(anchor.X, anchor.Y) || platform.Foreground != window) return false;
        foreground = window;
        ignoredUntilRelease.Clear();
        if (ignoreHeldKeys) ignoredUntilRelease.UnionWith(ReadKeys());
        previous = Stopwatch.GetTimestamp();
        captured = true;
        return true;
    }
    private bool ReleaseKeyDown() => Down(0x1B) || Down(0x12) || Down(0x5B) || Down(0x5C);
    private HashSet<int> ReadKeys() => Enumerable.Range(1, 254).Where(k => k is not (>= 160 and <= 165) && Down(k)).ToHashSet();
    private void Release()
    {
        captured = false;
        ignoredUntilRelease.Clear();
        Volatile.Write(ref pressedKeys, new HashSet<int>());
        Publish(ControllerState.Neutral(DateTimeOffset.Now));
    }
    internal void Tick()
    {
        lock (gate)
        {
            if (!enabled || disposed) return;
            bool f8 = Down(0x77);
            bool release = ReleaseKeyDown();
            if (release || captured && platform.Foreground != foreground) Release();
            else if (f8 && !f8Down)
            {
                if (captured) Release();
                else TryCapture(ignoreHeldKeys: false);
            }
            f8Down = f8;
            if (!captured)
            {
                Volatile.Write(ref pressedKeys, new HashSet<int>());
                Publish(ControllerState.Neutral(DateTimeOffset.Now));
                return;
            }
            if (!platform.TryGetCursor(out var cursor) || !platform.SetCursor(anchor.X, anchor.Y)) { Release(); return; }
            long now = Stopwatch.GetTimestamp();
            double seconds = (now - previous) / (double)Stopwatch.Frequency;
            previous = now;
            var keys = ReadKeys();
            ignoredUntilRelease.IntersectWith(keys);
            keys.ExceptWith(ignoredUntilRelease);
            Volatile.Write(ref pressedKeys, keys);
            Publish(KeyboardMouseMapper.Map(keys.Contains, cursor.X - anchor.X, cursor.Y - anchor.Y, seconds, mode, DateTimeOffset.Now));
        }
    }
    private bool Down(int key) => platform.Down(key);
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; enabled = false; Release(); timer?.Dispose();
        }
    }
    private sealed class WindowsPlatform : IKeyboardMousePlatform
    {
        public bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        public IntPtr Foreground => GetForegroundWindow();
        public bool TryGetCursor(out (int X, int Y) point)
        {
            bool success = GetCursorPos(out var cursor);
            point = (cursor.X, cursor.Y);
            return success;
        }
        public bool TryGetCaptureAnchor(out (int X, int Y) point)
        {
            // Start in the foreground client's center so an edge-positioned cursor can move in every direction.
            var window = Foreground;
            if (GetClientRect(window, out var rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
            {
                var center = new Point { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 };
                if (ClientToScreen(window, ref center)) { point = (center.X, center.Y); return true; }
            }
            return TryGetCursor(out point);
        }
        public bool SetCursor(int x, int y) => SetCursorPos(x, y);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
}

internal interface IKeyboardMousePlatform
{
    bool Down(int key);
    IntPtr Foreground { get; }
    bool TryGetCursor(out (int X, int Y) point);
    bool TryGetCaptureAnchor(out (int X, int Y) point);
    bool SetCursor(int x, int y);
}
