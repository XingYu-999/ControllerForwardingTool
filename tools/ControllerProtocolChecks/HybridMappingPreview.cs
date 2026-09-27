using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.ViewModels;
using ControllerForwardingTool.VirtualDevice;
using ControllerForwardingTool.Views;

internal static class HybridMappingPreview
{
    internal static void Render(string directory)
    {
        AppBuilder.Configure<ControllerForwardingTool.App>().UsePlatformDetect().SetupWithoutStarting();
        Directory.CreateDirectory(directory);
        foreach (int width in new[] { 1800, 1200, 900 })
        foreach (string input in new[] { "hybrid-off", "hybrid-on", "keyboard" })
        {
            bool keyboard = input == "keyboard", supplement = input == "hybrid-on";
            // Actual page/compiled templates with sample data; no live VM or device services.
            var page = new MappingPage();
            var controls = page.GetLogicalDescendants().OfType<Control>().ToArray();
            var grid = page.FindControl<Grid>("MappingComparisonGrid")!;
            Grid.SetColumnSpan(grid.Children[0], keyboard ? 2 : 1);
            Grid.SetColumnSpan(grid.Children[1], keyboard ? 2 : 1);
            Grid.SetColumn(grid.Children[1], keyboard ? 0 : 1);
            Grid.SetRow(grid.Children[1], keyboard ? 1 : 0);
            var pads = controls.OfType<ControllerTesterControl>().ToArray();
            pads[0].IsVisible = !keyboard; pads[0].Layout = ControllerLayout.Xbox;
            pads[0].SelectedButton = ControllerButtons.A; pads[0].Buttons = ControllerButtons.A; pads[0].IsOnline = true;
            pads[1].Layout = ControllerLayout.SwitchPro; pads[1].SelectedButton = ControllerButtons.X;
            pads[1].Buttons = ControllerButtons.X; pads[1].IsOnline = true;
            var keyboards = controls.OfType<KeyboardMouseControl>().ToArray();
            keyboards[0].IsVisible = keyboard; keyboards[0].SelectedKey = 'J'; keyboards[0].PressedKeys = ['J'];
            keyboards[1].SelectedKeys = ['P', 'J', 1]; keyboards[1].PressedKeys = ['J'];
            var primaryMode = page.FindControl<ComboBox>("PrimaryMouseMode")!;
            ((Control)primaryMode.Parent!).IsVisible = keyboard;
            primaryMode.ItemsSource = new[] { "右摇杆（默认）", "陀螺仪" }; primaryMode.SelectedIndex = 0;
            controls.OfType<TextBlock>().First(t => t.Text?.StartsWith("鼠标默认控制右摇杆") == true).IsVisible = keyboard;
            page.FindControl<TextBlock>("SourceTitle")!.Text = keyboard ? "键盘 / 鼠标" : "Xbox 360 Controller";
            page.FindControl<TextBlock>("OutputTitle")!.Text = "NS1 PRO · 当前配置测试";
            page.FindControl<TextBlock>("SelectionText")!.Text = keyboard ? "所选输出按钮的输入源（当前配置）：J" : "所选输出按钮的输入源（当前配置）：手柄 A、键盘 P";
            page.FindControl<Border>("KeyboardSupplement")!.IsVisible = !keyboard;
            page.FindControl<ToggleSwitch>("SupplementSwitch")!.IsChecked = supplement;
            page.FindControl<StackPanel>("SupplementOptions")!.IsVisible = supplement;
            page.FindControl<TextBlock>("SupplementHint")!.Text = supplement
                ? "补充已开启：已配置的键鼠按键和鼠标体感参与本页测试。实际转发按 F8 开始，Esc / F8 暂停；摇杆始终来自手柄。"
                : "默认关闭：仅使用实体手柄输入，键鼠补充绑定仍保留。开启后可测试已配置的键鼠按键和鼠标体感。";
            page.FindControl<TextBlock>("KeyboardHint")!.Text = "双击输出按键或映射卡片配置键鼠来源。仅转发已配置的补充按键；关闭补充不会删除绑定。";
            var gyroMode = page.FindControl<ComboBox>("SupplementGyroMode")!;
            gyroMode.ItemsSource = new[] { "自动（优先手柄，无体感时使用鼠标）", "实体手柄", "鼠标" }; gyroMode.SelectedIndex = 0;
            page.FindControl<TextBlock>("HybridStatus")!.Text = "配置来源：鼠标 → 陀螺仪。保存后按 F8 开启键鼠补充；摇杆仍由实体手柄控制。";
            var rows = Enum.GetValues<ControllerButtons>().Where(b => b != ControllerButtons.None)
                .Select(b => new ButtonMappingRow(b, b == ControllerButtons.B ? ControllerButtons.X : b, VirtualControllerMode.Ns1Pro)
                    { SourceLayout = ControllerLayout.Xbox, KeyboardSource = keyboard }).ToArray();
            var mapping = new Ns2ButtonMapping { Bindings = rows.ToDictionary(r => r.Source, r => r.Target.Button) };
            var rules = MainViewModel.BuildMappingRules(keyboard, VirtualControllerMode.Ns1Pro, rows, mapping,
                new Dictionary<int, ControllerButtons> { ['P'] = ControllerButtons.X, ['J'] = ControllerButtons.X, [1] = ControllerButtons.X }, new Dictionary<StickDirection, int>());
            page.FindControl<ItemsControl>("MappingRulesList")!.ItemsSource = MainViewModel.GroupMappingRules(rules, VirtualControllerMode.Ns1Pro);
            var sourceCombo = page.FindControl<ComboBox>("SourceMappingCombo")!;
            var targetCombo = page.FindControl<ComboBox>("TargetMappingCombo")!;
            sourceCombo.ItemsSource = rows; sourceCombo.SelectedIndex = Array.FindIndex(rows, r => r.Source == ControllerButtons.B);
            targetCombo.ItemsSource = rows[0].Choices; targetCombo.SelectedItem = rows[0].Choices.First(c => c.Button == ControllerButtons.X);
            controls.OfType<TextBlock>().First(t => t.Text?.Contains("未保存 ·") == true).IsVisible = true;
            var content = (Control)page.Content!; page.Content = null;
            var root = new Border { Child = content, Padding = new Thickness(20), Background = Brush.Parse("#F4F7FC") };
            var host = new Window { Content = root, Width = width, SizeToContent = SizeToContent.Height,
                Opacity = 0, ShowActivated = false, ShowInTaskbar = false, FontFamily = new("Microsoft YaHei UI, Segoe UI"), FontSize = 14 };
            try
            {
                host.Show(); host.UpdateLayout();
                foreach (var control in content.GetLogicalDescendants().OfType<Control>()) control.IsEnabled = true;
                foreach (var button in content.GetLogicalDescendants().OfType<Button>()) button.Command = new RelayCommand(() => { });
                var tabs = page.FindControl<TabControl>("MappingViews")!;
                for (int tab = 0; tab < 2; tab++)
                {
                    tabs.SelectedIndex = tab; host.UpdateLayout();
                    root.Measure(new(width, double.PositiveInfinity));
                    int height = (int)Math.Ceiling(root.DesiredSize.Height);
                    root.Arrange(new(0, 0, width, height));
                    var stateRoot = TopLevel.GetTopLevel(page.FindControl<StackPanel>("MappingStateView")!);
                    var rulesRoot = TopLevel.GetTopLevel(page.FindControl<Border>("MappingRulesView")!);
                    if (tab == 0 ? stateRoot is null || rulesRoot is not null : stateRoot is not null || rulesRoot is null)
                        throw new Exception("Mapping tabs must display only the selected state/rules block.");
                    if (tab == 0 && (keyboard || supplement))
                    {
                        var diagram = keyboards[keyboard ? 0 : 1];
                        if (diagram.Bounds.Height <= 0 || diagram.Bounds.Height > pads[1].Bounds.Height)
                            throw new Exception("Keyboard/mouse diagram must remain within the controller diagram's display height.");
                        double scale = Math.Min(diagram.Bounds.Width / 1140, diagram.Bounds.Height / 410);
                        var keyPoint = new Point((diagram.Bounds.Width - 1140 * scale) / 2 + 140 * scale,
                            (diagram.Bounds.Height - 410 * scale) / 2 + 215 * scale);
                        if (diagram.KeyAt(keyPoint) != 'A') throw new Exception("Scaled keyboard hit testing must still select the displayed key.");
                    }
                    using var bitmap = new RenderTargetBitmap(new(width, height), new(96, 96));
                    bitmap.Render(root); bitmap.Save(Path.Combine(directory, $"{input}-{(tab == 0 ? "state" : "rules")}-{width}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { host.Close(); }
        }
        Console.WriteLine("PASS: mutually exclusive mapping views, controller-sized keyboard hit testing, and off/on supplementary layouts at 1800/1200/900 px.");
    }
}
