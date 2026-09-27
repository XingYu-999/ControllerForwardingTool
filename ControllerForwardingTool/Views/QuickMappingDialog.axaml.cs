using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.ViewModels;

namespace ControllerForwardingTool.Views;

public partial class QuickMappingDialog : Window
{
    private readonly DispatcherTimer inputTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private MainViewModel? model;
    private bool wasActive;

    public QuickMappingDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            e.Handled = true;
            if (e.Key == Key.Escape) model?.CancelMappingCaptureCommand.Execute(null);
            else SampleInput();
            // Enter, Space and Tab are mapping sources, not dialog control shortcuts.
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
        inputTimer.Tick += (_, _) => SampleInput();
        Opened += (_, _) =>
        {
            model = DataContext as MainViewModel;
            if (model?.IsListeningForMapping != true) { Close(); return; }
            model.PropertyChanged += OnMappingChanged;
            inputTimer.Start();
        };
        Closed += (_, _) =>
        {
            inputTimer.Stop();
            if (model is not null)
            {
                model.PropertyChanged -= OnMappingChanged;
                model.CancelMappingCaptureCommand.Execute(null);
            }
        };
    }
    private void OnMappingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsListeningForMapping) && model?.IsListeningForMapping == false) Close();
    }
    private void SampleInput()
    {
        if (model is null) return;
        if (!IsActive)
        {
            if (wasActive) model.CancelMappingCaptureCommand.Execute(null);
            return;
        }
        wasActive = true;
        var keys = KeyboardMapping.ReadPressedKeys();
        // A click on Skip/Cancel is UI input. It must never bind the mouse to this step.
        if (keys.Any(k => k is 1 or 2 or 4 or 5 or 6) &&
            (DialogActions.IsPointerOver || Content is not Control { IsPointerOver: true }))
        { model.IgnoreQuickMappingClick(); return; }
        model.ObserveQuickMapping(keys);
    }
}
