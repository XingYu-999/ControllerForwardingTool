using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System.ComponentModel;
using ControllerForwardingTool.ViewModels;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.Views;

public partial class MainWindow : Window
{
    public MainWindow() : this(new WindowPlacementStore()) { }

    internal MainWindow(WindowPlacementStore placementStore)
    {
        InitializeComponent();
        windowPlacement = placementStore;
        lastVisibleWindowState = windowPlacement.Restore(this);
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty && IsVisible && !changingVisibility &&
                WindowState is WindowState.Normal or WindowState.Maximized)
                lastVisibleWindowState = WindowState;
            if (args.Property == WindowStateProperty && WindowState == WindowState.Minimized &&
                IsVisible && !changingVisibility && DataContext is MainViewModel { ShouldMinimizeToTray: true })
                HideToTray();
        };
        DataContextChanged += (_, _) =>
        {
            if (observedModel is not null) observedModel.PropertyChanged -= OnPageChanged;
            observedModel = DataContext as MainViewModel;
            if (observedModel is not null) observedModel.PropertyChanged += OnPageChanged;
        };
        Closed += (_, _) => { if (observedModel is not null) observedModel.PropertyChanged -= OnPageChanged; };
        Activated += (_, _) => { if (DataContext is MainViewModel vm) vm.RefreshDriverStateCommand.Execute(null); };
        Closing += async (_, args) =>
        {
            if (exiting) return;
            args.Cancel = true;
            if (args.CloseReason != WindowCloseReason.WindowClosing ||
                DataContext is MainViewModel { ShouldCloseToTray: false })
                await ExitApplicationAsync();
            else HideToTray();
        };
    }
    private bool exiting;
    private bool changingVisibility;
    private readonly WindowPlacementStore windowPlacement;
    private WindowState lastVisibleWindowState;
    private MainViewModel? observedModel;

    internal void HideToTray()
    {
        if (exiting || !IsVisible) return;
        windowPlacement.Save(this, lastVisibleWindowState);
        changingVisibility = true;
        try { Hide(); }
        finally { changingVisibility = false; }
    }

    public void ShowFromTray(string? page = null)
    {
        if (exiting) return;
        if (page is not null && DataContext is MainViewModel model) model.SelectedPage = page;
        bool wasHidden = !IsVisible;
        changingVisibility = true;
        try
        {
            Show();
            if (wasHidden) windowPlacement.Apply(this);
            WindowState = lastVisibleWindowState;
            Activate();
        }
        finally { changingVisibility = false; }
    }

    public async Task ExitApplicationAsync()
    {
        if (exiting) return;
        windowPlacement.Save(this, lastVisibleWindowState);
        exiting = true;
        try { if (DataContext is MainViewModel model) await model.ShutdownAsync(); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Shutdown: {ex}"); }
        finally
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else Close();
        }
    }
    private void OnPageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.SelectedPage))
            Dispatcher.UIThread.Post(() => MainContentScroller.Offset = default, DispatcherPriority.Loaded);
    }
}
