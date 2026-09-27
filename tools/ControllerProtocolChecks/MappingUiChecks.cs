using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.ViewModels;
using ControllerForwardingTool.Views;

internal static class MappingUiChecks
{
    internal static void Run(string directory)
    {
        AppBuilder.Configure<ControllerForwardingTool.App>().UsePlatformDetect().SetupWithoutStarting();
        Directory.CreateDirectory(directory);
        CheckScrollRestoration();
        foreach (bool keyboardOnly in new[] { false, true })
        { RenderEditor(directory, keyboardOnly); RenderEditor(directory, keyboardOnly, stick: true); }
        foreach (int width in new[] { 1100, 760 })
        {
            var keyboard = new KeyboardMouseControl { Height = 360, PressedKeys = ['W', 'J', 1, 5], SelectedKey = 'P', MouseDelta = new(16, -12) };
            var root = new Border { Child = keyboard, Background = Brushes.White };
            Save(root, width, Path.Combine(directory, $"keyboard-{width}.png"));
        }
        Console.WriteLine("PASS: mapping page scroll suppression/restoration; rendered keyboard and single-button dialogs without device services.");
    }
    private static void CheckScrollRestoration()
    {
        var last = new Button { Content = "Updated mapping rule", Height = 40 };
        var content = new StackPanel { Children = { new Border { Height = 1400 }, last } };
        var scroller = new ScrollViewer { Content = content, Width = 700, Height = 300 };
        // A transparent, non-activating host lets Avalonia initialize its real scroll presenter.
        var host = new Window { Content = scroller, Width = 700, Height = 300, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        host.Show();
        void Layout() { scroller.Measure(new(700, 300)); scroller.Arrange(new(0, 0, 700, 300)); }
        Layout();
        scroller.Offset = new(0, 180); Layout();
        last.BringIntoView();
        if (scroller.Offset.Y < 1000) throw new Exception($"scroll regression fixture must first reproduce scrolling to a distant rule: extent={scroller.Extent}, viewport={scroller.Viewport}, offset={scroller.Offset}, last={last.Bounds}");
        scroller.Offset = new(0, 180); Layout();
        using (new MappingScrollBookmark(scroller, content))
        {
            last.BringIntoView();
            if (scroller.Offset.Y != 180 || scroller.BringIntoViewOnFocusChange)
                throw new Exception("modal recording must suppress focus/updated-rule scroll requests");
            scroller.Offset = new(0, 900); // Simulate a queued layout/focus change at modal completion.
        }
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        if (scroller.Offset.Y != 180 || !scroller.BringIntoViewOnFocusChange)
            throw new Exception("modal completion must restore original scroll position and normal focus behavior");
        Layout(); last.BringIntoView();
        if (scroller.Offset.Y < 1000) throw new Exception("normal bring-into-view must resume after the modal closes");
        host.Close();
    }
    private static void RenderEditor(string directory, bool keyboardOnly, bool stick = false)
    {
        // Populate the actual dialog controls without instantiating a VM (which starts hardware services).
        var dialog = new MappingButtonDialog();
        var controls = dialog.GetLogicalDescendants().OfType<Control>().ToArray();
        var target = dialog.FindControl<ComboBox>("ButtonEditorTarget")!;
        ((Control)target.Parent!).IsVisible = keyboardOnly && !stick;
        target.ItemsSource = new[] { "A", "B", "X", "Y", "左摇杆 ↑" }; target.SelectedIndex = 1;
        dialog.FindControl<TextBlock>("EditorTitle")!.Text = stick ? "配置左摇杆" : keyboardOnly ? "编辑规则 · 键盘 J" : "配置 B";
        var tabs = dialog.FindControl<TabControl>("EditorTabs")!;
        dialog.FindControl<TabItem>("EditorButtonTab")!.Header = stick ? "摇杆按下" : "按键配置";
        dialog.FindControl<TabItem>("EditorStickTab")!.IsVisible = stick;
        dialog.FindControl<StackPanel>("PhysicalStickEditor")!.IsVisible = !keyboardOnly;
        dialog.FindControl<StackPanel>("KeyboardStickEditor")!.IsVisible = keyboardOnly;
        var stickSource = dialog.FindControl<ComboBox>("StickEditorSource")!;
        stickSource.ItemsSource = new[] { "源左摇杆", "源右摇杆" }; stickSource.SelectedIndex = 1;
        dialog.FindControl<ItemsControl>("StickDirectionList")!.ItemsSource = new[]
        {
            new MappingStickDirectionRow(StickDirection.LeftUp, "左摇杆 ↑", "W"),
            new MappingStickDirectionRow(StickDirection.LeftDown, "左摇杆 ↓", "S"),
            new MappingStickDirectionRow(StickDirection.LeftLeft, "左摇杆 ←", "请按下源按键…"),
            new MappingStickDirectionRow(StickDirection.LeftRight, "左摇杆 →", "D")
        };
        dialog.FindControl<TextBlock>("StickEditorHint")!.Text = keyboardOnly ? "请按下左摇杆 ← 的源按键；鼠标左键使用上方独立按钮。" : "已选择源右摇杆，确认后整体映射上下左右。";
        var diagram = dialog.FindControl<ControllerTesterControl>("EditorDiagram")!;
        diagram.Layout = ControllerLayout.SwitchPro; diagram.SelectedButton = stick ? ControllerButtons.LeftStick : ControllerButtons.B;
        dialog.FindControl<ItemsControl>("ButtonControllerSources")!.ItemsSource = new[] { new MappingEditorSource("A", ControllerButtons.A), new MappingEditorSource("左摇杆按下（L3）", ControllerButtons.LeftStick) };
        dialog.FindControl<ItemsControl>("ButtonKeyboardSources")!.ItemsSource = new[] { new MappingEditorSource("J", Key: 'J'), new MappingEditorSource("空格", Key: 32), new MappingEditorSource("鼠标侧键 M4", Key: 5) };
        var sourceCards = controls.OfType<Border>().Where(b => b.Classes.Contains("card")).ToArray();
        sourceCards[0].IsVisible = !keyboardOnly;
        Grid.SetColumn(sourceCards[1], keyboardOnly ? 0 : 1); Grid.SetColumnSpan(sourceCards[1], keyboardOnly ? 2 : 1);
        controls.OfType<CheckBox>().Single().IsChecked = true;
        var hint = dialog.FindControl<TextBlock>("ButtonEditorHint")!;
        hint.Text = "已加入来源。可继续按键盘或手柄按键，点击确认后提交。";
        var content = (Control)dialog.Content!;
        dialog.Content = null;
        var host = new Window { Content = content, Width = 700, SizeToContent = SizeToContent.Height,
            Opacity = 0, ShowActivated = false, ShowInTaskbar = false, FontFamily = new("Microsoft YaHei UI, Segoe UI") };
        try
        {
            host.Show(); host.UpdateLayout();
            // Offline values have no live command owner, so use inert commands for visual verification.
            foreach (var button in content.GetLogicalDescendants().OfType<Button>())
                button.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            if (tabs.SelectedIndex != 0) throw new Exception("Stick editor must default to the click configuration tab.");
            Save(content, 700, Path.Combine(directory, (keyboardOnly ? "keyboard" : "hybrid") + (stick ? "-stick-click.png" : "-button-editor.png")));
            if (stick)
            {
                tabs.SelectedIndex = 1; host.UpdateLayout();
                foreach (var button in content.GetLogicalDescendants().OfType<Button>())
                { button.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }); button.IsEnabled = true; }
                Save(content, 700, Path.Combine(directory, (keyboardOnly ? "keyboard" : "hybrid") + "-stick-axes.png"));
                if (TopLevel.GetTopLevel(dialog.FindControl<ItemsControl>("ButtonControllerSources")!) is not null)
                    throw new Exception("Stick axes and click pages must be mutually exclusive.");
            }
        }
        finally { host.Close(); dialog.Close(); }
    }
    private static void Save(Control root, int width, string path)
    {
        root.Measure(new(width, double.PositiveInfinity));
        int height = (int)Math.Ceiling(root.DesiredSize.Height);
        root.Arrange(new(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new(width, height), new(96, 96));
        bitmap.Render(root); bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
}
