using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;

namespace ControllerForwardingTool.Core;

internal sealed class WindowPlacementStore
{
    private const int ShowNormal = 1;
    private const int ShowMaximized = 3;
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
    private readonly string path;
    private Placement? savedPlacement;

    public WindowPlacementStore(string? path = null)
    {
        this.path = path ?? AppDataPaths.WindowPlacementPath;
    }

    public WindowState Restore(Window window)
    {
        try
        {
            Placement placement = JsonSerializer.Deserialize<Placement>(File.ReadAllText(path), JsonOptions);
            if (placement.Length == Marshal.SizeOf<Placement>() &&
                placement.NormalPosition.Right > placement.NormalPosition.Left &&
                placement.NormalPosition.Bottom > placement.NormalPosition.Top &&
                placement.ShowCommand is ShowNormal or ShowMaximized)
                savedPlacement = placement;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Missing or damaged settings use the first-launch default.
        }

        window.WindowState = savedPlacement?.ShowCommand == ShowNormal
            ? WindowState.Normal : WindowState.Maximized;
        window.Opened += RestoreOnFirstOpen;
        return window.WindowState;

        void RestoreOnFirstOpen(object? sender, EventArgs args)
        {
            window.Opened -= RestoreOnFirstOpen;
            Apply(window);
        }
    }

    public void Apply(Window window)
    {
        if (savedPlacement is not { } placement || window.TryGetPlatformHandle() is not { } handle)
            return;

        // Windows preserves the normal bounds while maximized and recovers windows
        // whose previous monitor is no longer connected.
        placement.Flags = 0;
        if (!SetWindowPlacement(handle.Handle, ref placement))
            Trace.TraceWarning("Could not restore window placement: {0}", Marshal.GetLastWin32Error());
    }

    public void Save(Window window, WindowState lastVisibleState)
    {
        // Closing to the tray already captured the placement before Hide(). Do not
        // overwrite it with the hidden window's native state when exiting the app.
        if (!window.IsVisible || window.TryGetPlatformHandle() is not { } handle)
            return;

        Placement placement = new() { Length = Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(handle.Handle, ref placement)) return;
        WindowState state = window.WindowState == WindowState.Minimized
            ? lastVisibleState : window.WindowState;
        placement.ShowCommand = state == WindowState.Maximized ? ShowMaximized : ShowNormal;
        placement.Flags = 0;
        savedPlacement = placement;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(placement, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not save window placement: {0}", ex.Message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Placement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect NormalPosition;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
}
