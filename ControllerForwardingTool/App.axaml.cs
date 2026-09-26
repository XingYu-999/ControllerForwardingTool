using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using ControllerForwardingTool.ViewModels;
using ControllerForwardingTool.Views;

namespace ControllerForwardingTool;

public partial class App : Application
{
    private TrayIcon? trayIcon;
    private static int activationRequested;
    private static bool startupVisibilityApplied;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var model = new MainViewModel();
            var window = new MainWindow
            {
                DataContext = model,
            };
            desktop.MainWindow = window;
            CreateTrayIcon(window, model);
            window.Opened += OnFirstOpen;
            void OnFirstOpen(object? sender, EventArgs args)
            {
                window.Opened -= OnFirstOpen;
                // Run after the initial Show and placement restoration. An activation
                // received during startup takes precedence over starting in the tray.
                Dispatcher.UIThread.Post(() =>
                {
                    startupVisibilityApplied = true;
                    if (Volatile.Read(ref activationRequested) != 0) ProcessPendingActivation();
                    else if (model.ShouldStartInTray) window.HideToTray();
                });
            }
            desktop.Exit += (_, _) => trayIcon?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal static void RequestMainWindowActivation()
    {
        Interlocked.Exchange(ref activationRequested, 1);
        if (Current is not null) Dispatcher.UIThread.Post(ProcessPendingActivation);
    }

    private static void ProcessPendingActivation()
    {
        if (!startupVisibilityApplied ||
            Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is not MainWindow window) return;

        if (Interlocked.Exchange(ref activationRequested, 0) != 0) window.ShowFromTray();
    }

    private void CreateTrayIcon(MainWindow window, MainViewModel model)
    {
        using var iconStream = AssetLoader.Open(new Uri("avares://ControllerForwardingTool/Assets/ControllerForwardingTool.ico"));
        var menu = new NativeMenu();
        var openItem = new NativeMenuItem("打开主窗口");
        openItem.Click += (_, _) => window.ShowFromTray();
        menu.Items.Add(openItem);

        var statusItem = new NativeMenuItem(model.ConnectionStatusLabel) { IsEnabled = false };
        menu.Items.Add(statusItem);
        menu.Items.Add(new NativeMenuItemSeparator());

        var logsItem = new NativeMenuItem("查看日志");
        logsItem.Click += (_, _) => window.ShowFromTray("日志");
        menu.Items.Add(logsItem);
        var settingsItem = new NativeMenuItem("设置");
        settingsItem.Click += (_, _) => window.ShowFromTray("设置");
        menu.Items.Add(settingsItem);
        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += async (_, _) => await window.ExitApplicationAsync();
        menu.Items.Add(exitItem);

        trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = AppIdentity.ChineseName,
            Menu = menu,
        };
        trayIcon.Clicked += (_, _) => window.ShowFromTray();
        TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ConnectionStatusLabel))
                statusItem.Header = model.ConnectionStatusLabel;
        };
    }
}
