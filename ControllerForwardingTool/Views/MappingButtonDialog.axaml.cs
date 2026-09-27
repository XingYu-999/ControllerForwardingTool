using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.ViewModels;

namespace ControllerForwardingTool.Views;

public partial class MappingButtonDialog : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private MainViewModel? model;
    private bool wasActive;
    public MappingButtonDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            e.Handled = true;
            if (e.Key == Key.Escape) model?.CancelMappingCaptureCommand.Execute(null);
            else Sample();
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
        timer.Tick += (_, _) => Sample();
        Opened += (_, _) =>
        {
            model = DataContext as MainViewModel;
            if (model?.IsListeningForMapping != true) { Close(); return; }
            model.PropertyChanged += Changed;
            timer.Start();
        };
        Closed += (_, _) =>
        {
            timer.Stop();
            if (model is null) return;
            model.PropertyChanged -= Changed;
            model.CancelMappingCaptureCommand.Execute(null);
        };
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsListeningForMapping) && model?.IsListeningForMapping == false) Close();
    }
    private void Sample()
    {
        if (!IsActive) { if (wasActive) model?.CancelMappingCaptureCommand.Execute(null); return; }
        wasActive = true;
        model?.ObserveMappingEditor(KeyboardMapping.ReadPressedKeys());
    }
}
