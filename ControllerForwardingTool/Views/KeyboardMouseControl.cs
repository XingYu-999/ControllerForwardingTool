using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ControllerForwardingTool.Input;

namespace ControllerForwardingTool.Views;

/// <summary>A compact keyboard with staggered rows, raised keycaps and separate live/selection states.</summary>
public sealed class KeyboardMouseControl : Control
{
    public static readonly StyledProperty<int[]> PressedKeysProperty = AvaloniaProperty.Register<KeyboardMouseControl, int[]>(nameof(PressedKeys), []);
    public static readonly StyledProperty<int> SelectedKeyProperty = AvaloniaProperty.Register<KeyboardMouseControl, int>(nameof(SelectedKey));
    public static readonly StyledProperty<int[]> SelectedKeysProperty = AvaloniaProperty.Register<KeyboardMouseControl, int[]>(nameof(SelectedKeys), []);
    public static readonly StyledProperty<ICommand?> SelectKeyCommandProperty = AvaloniaProperty.Register<KeyboardMouseControl, ICommand?>(nameof(SelectKeyCommand));
    public static readonly StyledProperty<Vector> MouseDeltaProperty = AvaloniaProperty.Register<KeyboardMouseControl, Vector>(nameof(MouseDelta));
    public int[] PressedKeys { get => GetValue(PressedKeysProperty); set => SetValue(PressedKeysProperty, value); }
    public int SelectedKey { get => GetValue(SelectedKeyProperty); set => SetValue(SelectedKeyProperty, value); }
    public int[] SelectedKeys { get => GetValue(SelectedKeysProperty); set => SetValue(SelectedKeysProperty, value); }
    public ICommand? SelectKeyCommand { get => GetValue(SelectKeyCommandProperty); set => SetValue(SelectKeyCommandProperty, value); }
    public Vector MouseDelta { get => GetValue(MouseDeltaProperty); set => SetValue(MouseDeltaProperty, value); }
    private const double SceneWidth = 1140, SceneHeight = 410;
    private sealed record KeyCap(int Key, string Label, Rect Bounds);
    private static readonly KeyCap[] Caps = CreateKeys();
    private int? hovered;
    private static readonly IBrush Ink = Brush.Parse("#263A53"), Muted = Brush.Parse("#7E8CA1"), Accent = Brush.Parse("#2563EB");
    private static readonly IBrush Cap = Gradient("#FFFFFF", "#F0F4F9"), SelectedCap = Gradient("#F5FAFF", "#DAE9FF"),
        PressedCap = Gradient("#FFC56B", "#F2A03D"), ReservedCap = Gradient("#EDF1F6", "#E1E7F0");
    static KeyboardMouseControl() => AffectsRender<KeyboardMouseControl>(PressedKeysProperty, SelectedKeyProperty, SelectedKeysProperty, MouseDeltaProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : SceneWidth;
        return new(width, Math.Min(availableSize.Height, width * SceneHeight / SceneWidth));
    }

    internal int? KeyAt(Point point)
    {
        double scale = Math.Min(Bounds.Width / SceneWidth, Bounds.Height / SceneHeight);
        if (!double.IsFinite(scale) || scale <= 0) return null;
        point = new((point.X - (Bounds.Width - SceneWidth * scale) / 2) / scale, (point.Y - (Bounds.Height - SceneHeight * scale) / 2) / scale);
        return Caps.FirstOrDefault(k => k.Bounds.Contains(point))?.Key;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && KeyAt(e.GetPosition(this)) is { } key &&
            SelectKeyCommand is { } command && command.CanExecute(key)) { command.Execute(key); e.Handled = true; }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int? key = KeyAt(e.GetPosition(this));
        if (hovered == key) return;
        hovered = key;
        ToolTip.SetTip(this, key is { } k ? KeyboardMapping.KeyName(k) + (KeyboardMapping.CanBind(k) ? " · 单击查看映射" : " · 控制保留键") : null);
        InvalidateVisual();
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); hovered = null; InvalidateVisual(); }

    private static KeyCap[] CreateKeys()
    {
        var keys = new List<KeyCap>();
        void Key(int code, string label, double x, double y, double width = 44, double height = 38) => keys.Add(new(code, label, new(x, y, width, height)));
        void Row(string letters, double x, double y) { for (int i = 0; i < letters.Length; i++) Key(letters[i], letters[i].ToString(), x + i * 50, y); }
        Key(27, "Esc", 30, 40);
        for (int i = 0; i < 12; i++) Key(112 + i, $"F{i + 1}", 114 + i * 50 + i / 4 * 18, 40);
        int[] numbers = [192, 49, 50, 51, 52, 53, 54, 55, 56, 57, 48, 189, 187];
        for (int i = 0; i < numbers.Length; i++) Key(numbers[i], KeyboardMapping.KeyName(numbers[i]), 30 + i * 50, 96);
        Key(8, "Backspace", 680, 96, 74);
        Key(9, "Tab", 30, 146, 66); Row("QWERTYUIOP", 102, 146);
        Key(219, "[", 602, 146); Key(221, "]", 652, 146); Key(220, "\\", 702, 146, 52);
        Key(20, "Caps Lock", 30, 196, 82); Row("ASDFGHJKL", 118, 196);
        Key(186, ";", 568, 196); Key(222, "'", 618, 196); Key(13, "Enter", 668, 196, 86);
        Key(16, "Shift", 30, 246, 108); Row("ZXCVBNM", 144, 246);
        Key(188, ",", 494, 246); Key(190, ".", 544, 246); Key(191, "/", 594, 246); Key(16, "Shift", 644, 246, 110);
        Key(17, "Ctrl", 30, 296, 60); Key(91, "Win", 96, 296, 52); Key(18, "Alt", 154, 296, 52);
        Key(32, "SPACE", 212, 296, 318); Key(18, "Alt", 536, 296, 60); Key(92, "Win", 602, 296, 60); Key(17, "Ctrl", 668, 296, 86);
        Key(44, "PrtSc", 780, 40); Key(145, "ScrLk", 830, 40); Key(19, "Pause", 880, 40);
        Key(45, "Ins", 780, 96); Key(36, "Home", 830, 96); Key(33, "PgUp", 880, 96);
        Key(46, "Del", 780, 146); Key(35, "End", 830, 146); Key(34, "PgDn", 880, 146);
        Key(38, "↑", 830, 246); Key(37, "←", 780, 296); Key(40, "↓", 830, 296); Key(39, "→", 880, 296);
        Key(1, "左键", 986, 70, 56, 82); Key(2, "右键", 1050, 70, 56, 82);
        Key(4, "中键", 1031, 169, 30, 48); Key(5, "M4", 981, 235, 35, 28); Key(6, "M5", 981, 273, 35, 28);
        return keys.ToArray();
    }

    public override void Render(DrawingContext c)
    {
        base.Render(c);
        double scale = Math.Min(Bounds.Width / SceneWidth, Bounds.Height / SceneHeight);
        if (!double.IsFinite(scale) || scale <= 0) return;
        using (c.PushTransform(Matrix.CreateTranslation((Bounds.Width - SceneWidth * scale) / 2, (Bounds.Height - SceneHeight * scale) / 2)))
        using (c.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            // Soft contact shadows, a beveled chassis and contained bottom row keep the depth consistent.
            c.DrawRectangle(Brush.Parse("#EDF1F7"), null, new Rect(18, 32, 936, 342), 22, 22);
            c.DrawRectangle(Brush.Parse("#CAD3E1"), null, new Rect(14, 27, 936, 340), 20, 20);
            c.DrawRectangle(Gradient("#D3DCE8", "#A6B4C8"), null, new Rect(12, 23, 936, 338), 18, 18);
            c.DrawRectangle(Gradient("#F0F4FA", "#DBE3EF"), new Pen(Brush.Parse("#ACBBD0")), new Rect(12, 18, 936, 332), 18, 18);
            c.DrawLine(new Pen(Brush.Parse("#FAFCFF"), 2), new(30, 22), new(928, 22));
            c.DrawLine(new Pen(Brush.Parse("#C7D2E2")), new(766, 90), new(766, 334));
            c.DrawEllipse(Brush.Parse("#EDF1F7"), null, new Rect(981, 55, 150, 292));
            c.DrawRectangle(Brush.Parse("#A7B5C9"), null, new Rect(977, 44, 150, 292), 72, 72);
            c.DrawRectangle(Gradient("#F8FAFD", "#DEE7F3"), new Pen(Brush.Parse("#ADBCD1")), new Rect(972, 36, 150, 292), 70, 70);
            foreach (var key in Caps) DrawKey(c, key);
            for (int i = 0; i < 3; i++) c.DrawLine(new Pen(Brush.Parse("#8C9DB3")), new(1039, 201 + i * 3), new(1053, 201 + i * 3));
            var center = new Point(1071, 264);
            c.DrawEllipse(Brush.Parse("#EAF0F8"), new Pen(Brush.Parse("#B0BFD4")), center, 23, 23);
            c.DrawLine(new Pen(Brush.Parse("#C5D1E1")), new(1054, 264), new(1088, 264));
            c.DrawLine(new Pen(Brush.Parse("#C5D1E1")), new(1071, 247), new(1071, 281));
            var moved = new Point(center.X + Math.Clamp(MouseDelta.X, -19, 19), center.Y + Math.Clamp(MouseDelta.Y, -19, 19));
            c.DrawLine(new Pen(Accent, 2.5), center, moved); c.DrawEllipse(Accent, null, moved, 3.5, 3.5);
            Text(c, "鼠标", 1047, 350, 13, Muted);
            Legend(c, 260, "按下", Brush.Parse("#F2A03D")); Legend(c, 420, "映射来源", Accent); Legend(c, 610, "控制保留键", Brush.Parse("#BAC5D5"));
        }
    }
    private void DrawKey(DrawingContext c, KeyCap key)
    {
        bool down = PressedKeys.Contains(key.Key), selected = SelectedKey == key.Key || SelectedKeys.Contains(key.Key);
        bool reserved = !KeyboardMapping.CanBind(key.Key);
        var r = key.Bounds;
        double depth = down ? 3 : 0;
        var top = new Rect(r.X, r.Y + depth, r.Width, r.Height);
        if (selected) c.DrawRectangle(Brush.Parse("#DCEAFF"), null, r.Inflate(3), 7, 7);
        c.DrawRectangle(Brush.Parse("#BDC8D9"), null, new Rect(r.X + 1, r.Y + 6, r.Width, r.Height), 6, 6);
        c.DrawRectangle(Brush.Parse("#95A5BB"), null, new Rect(r.X, r.Y + 4, r.Width, r.Height), 5, 5);
        c.DrawRectangle(down ? PressedCap : selected ? SelectedCap : reserved ? ReservedCap : Cap,
            new Pen(selected ? Accent : hovered == key.Key ? Brush.Parse("#80A7E6") : Brush.Parse("#B4C1D3"), selected ? 2 : 1), top, 5, 5);
        c.DrawLine(new Pen(down ? Brush.Parse("#FFE0A5") : Brushes.White, 1.5), new(r.X + 5, r.Y + depth + 4), new(r.Right - 5, r.Y + depth + 4));
        double size = key.Label.Length > 5 ? 11.5 : key.Label.Length > 2 ? 12.5 : 15;
        Text(c, key.Label, top.Center.X, top.Center.Y, size, down ? Brush.Parse("#653A08") : selected ? Accent : reserved ? Muted : Ink);
    }
    private static IBrush Gradient(string top, string bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = [new GradientStop(Color.Parse(top), 0), new GradientStop(Color.Parse(bottom), 1)]
    };
    private static void Legend(DrawingContext c, double x, string label, IBrush color)
    { c.DrawEllipse(color, null, new Point(x, 394), 4, 4); Text(c, label, x + 52, 394, 12, Muted); }
    private static void Text(DrawingContext c, string label, double x, double y, double size, IBrush brush)
    {
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI, Segoe UI"), size, brush);
        c.DrawText(text, new Point(x - text.Width / 2, y - text.Height / 2));
    }
}
