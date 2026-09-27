using Avalonia.Controls;
using Avalonia;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.ViewModels;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ControllerForwardingTool.Core;
namespace ControllerForwardingTool.Views;
public partial class MappingPage : UserControl
{
    private readonly DispatcherTimer inputTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Point? previousMouse;
    private Window? mappingDialog;
    public MappingPage()
    {
        InitializeComponent();
        OutputMappingDiagram.AddHandler(PointerPressedEvent, OnOutputPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        inputTimer.Tick += (_, _) => SampleInput();
        AttachedToVisualTree += (_, _) => inputTimer.Start();
        DetachedFromVisualTree += (_, _) =>
        { inputTimer.Stop(); previousMouse = null; (DataContext as MainViewModel)?.LeaveMappingPage(); };
    }
    private void OnQuickMappingClick(object? sender, RoutedEventArgs e) => ShowMappingDialog(sender as Control, fullWizard: true);
    private void OnRuleDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: MappingRuleGroup rule } control) return;
        e.Handled = true;
        ShowMappingDialog(control, rule: rule);
    }
    private void OnOutputPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            OutputMappingDiagram.ButtonAt(e.GetPosition(OutputMappingDiagram)) is not { } button ||
            DataContext is not MainViewModel vm) return;
        e.Handled = true;
        ShowMappingDialog(OutputMappingDiagram, target: button);
    }
    private async void ShowMappingDialog(Control? trigger, bool fullWizard = false, MappingRuleGroup? rule = null, ControllerButtons? target = null)
    {
        if (mappingDialog is not null || DataContext is not MainViewModel vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        var scroller = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        using var bookmark = new MappingScrollBookmark(scroller, this);
        trigger?.Focus(NavigationMethod.Pointer);
        if (target is { } button) vm.SelectOutputMappingButtonCommand.Execute(button);
        if (!(fullWizard ? vm.BeginQuickMapping() : vm.BeginMappingEditor(rule))) return;
        try
        {
            mappingDialog = fullWizard ? new QuickMappingDialog { DataContext = vm } : new MappingButtonDialog { DataContext = vm };
            await mappingDialog.ShowDialog(owner);
        }
        finally { mappingDialog = null; previousMouse = null; vm.CancelMappingCaptureCommand.Execute(null); }
    }
    private void SampleInput()
    {
        if (DataContext is not MainViewModel vm || !vm.IsMapping || vm.IsListeningForMapping) return;
        bool focused = TopLevel.GetTopLevel(this) is Window { IsActive: true };
        Vector delta = default;
        if (focused && GetCursorPos(out var position))
        {
            var current = new Point(position.X, position.Y);
            if (previousMouse is { } old) delta = current - old;
            previousMouse = current;
        }
        else previousMouse = null;
        vm.ObserveMappingKeyboard(focused ? KeyboardMapping.ReadPressedKeys() : [], delta, focused);
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint position);
}

/// <summary>Modal focus restoration and updated rule controls must not move the underlying page.</summary>
internal sealed class MappingScrollBookmark : IDisposable
{
    private readonly ScrollViewer? scroller;
    private readonly Vector offset;
    private readonly bool focusScroll;
    private readonly Control? content;
    public MappingScrollBookmark(ScrollViewer? scroller, Control? content = null)
    {
        this.scroller = scroller;
        this.content = content ?? scroller;
        if (scroller is null) return;
        offset = scroller.Offset; focusScroll = scroller.BringIntoViewOnFocusChange;
        scroller.BringIntoViewOnFocusChange = false;
        this.content!.AddHandler(Control.RequestBringIntoViewEvent, Suppress, RoutingStrategies.Bubble, handledEventsToo: true);
    }
    private void Suppress(object? sender, RequestBringIntoViewEventArgs e) => e.Handled = true;
    public void Dispose()
    {
        if (scroller is null) return;
        scroller.Offset = offset;
        Dispatcher.UIThread.Post(() =>
        {
            scroller.Offset = offset;
            scroller.BringIntoViewOnFocusChange = focusScroll;
            content!.RemoveHandler(Control.RequestBringIntoViewEvent, Suppress);
        }, DispatcherPriority.Loaded);
    }
}
