using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ControllerForwardingTool;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Views;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // No desktop lifetime or view model: no Bluetooth scanning, drivers, or tray icon.
        AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
        using var done = new CancellationTokenSource();
        int result = 0;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await CheckWindows(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
            finally { done.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(done.Token);
        return result;
    }

    private static async Task CheckWindows()
    {
        string directory = Path.Combine(Path.GetTempPath(), "forwarding-window-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "placement.json");
        var windows = new List<MainWindow>();
        try
        {
            MainWindow first = await Open();
            CheckState(first, WindowState.Maximized, "First launch");
            await first.ExitApplicationAsync();

            foreach (WindowState state in new[] { WindowState.Normal, WindowState.Maximized })
            {
                MainWindow window = await Open();
                window.WindowState = WindowState.Normal;
                await Settle();
                window.Position = new PixelPoint(100, 80);
                window.Width = 1100;
                window.Height = 700;
                await Settle();
                PixelPoint normalPosition = window.Position;
                Size normalSize = window.Bounds.Size;
                window.WindowState = state;
                await Settle();

                window.Close();
                await Settle();
                Check(!window.IsVisible, "Close hides to tray");
                window.ShowFromTray();
                await Settle();
                CheckState(window, state, "Tray reopen after close");

                window.WindowState = WindowState.Minimized;
                await Settle();
                window.ShowFromTray();
                await Settle();
                CheckState(window, state, "Tray reopen after minimize");

                // Exit while hidden must retain the placement captured by Close().
                window.Close();
                await window.ExitApplicationAsync();
                MainWindow reopened = await Open();
                CheckState(reopened, state, "Restart after hidden exit");
                reopened.WindowState = WindowState.Normal;
                await Settle();
                Check(Math.Abs(reopened.Position.X - normalPosition.X) < 3 &&
                      Math.Abs(reopened.Position.Y - normalPosition.Y) < 3 &&
                      Math.Abs(reopened.Bounds.Width - normalSize.Width) < 3 &&
                      Math.Abs(reopened.Bounds.Height - normalSize.Height) < 3,
                    "Normal position and size survive restart");

                reopened.WindowState = state;
                await Settle();
                reopened.WindowState = WindowState.Minimized;
                await Settle();
                await reopened.ExitApplicationAsync();
                MainWindow afterMinimizedExit = await Open();
                CheckState(afterMinimizedExit, state, "Restart after minimized exit");
                await afterMinimizedExit.ExitApplicationAsync();
            }

            File.WriteAllText(path, "invalid json");
            MainWindow invalid = await Open();
            CheckState(invalid, WindowState.Maximized, "Damaged settings fallback");
            await invalid.ExitApplicationAsync();
        }
        finally
        {
            foreach (MainWindow window in windows) await window.ExitApplicationAsync();
            Directory.Delete(directory, recursive: true);
        }

        async Task<MainWindow> Open()
        {
            var window = new MainWindow(new WindowPlacementStore(path));
            windows.Add(window);
            window.Show();
            await Settle();
            return window;
        }
    }

    private static Task Settle() => Task.Delay(200);

    private static void CheckState(Window window, WindowState expected, string message)
    {
        Check(window.IsVisible && window.WindowState == expected &&
              IsZoomed(window.TryGetPlatformHandle()!.Handle) == (expected == WindowState.Maximized),
            $"{message}: {expected} (Avalonia and native Windows state)");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);
}
