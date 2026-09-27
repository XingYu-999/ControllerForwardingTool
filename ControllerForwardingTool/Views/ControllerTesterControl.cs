using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using System.Windows.Input;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;

namespace ControllerForwardingTool.Views;

/// <summary>Code-native controller diagram; no raster asset is needed for live button state.</summary>
public sealed class ControllerTesterControl : Control
{
    public static readonly StyledProperty<ICommand?> SelectButtonCommandProperty = AvaloniaProperty.Register<ControllerTesterControl, ICommand?>(nameof(SelectButtonCommand));
    public static readonly StyledProperty<ControllerButtons> SelectedButtonProperty = AvaloniaProperty.Register<ControllerTesterControl, ControllerButtons>(nameof(SelectedButton));
    public ICommand? SelectButtonCommand { get => GetValue(SelectButtonCommandProperty); set => SetValue(SelectButtonCommandProperty, value); }
    public ControllerButtons SelectedButton { get => GetValue(SelectedButtonProperty); set => SetValue(SelectedButtonProperty, value); }
    private readonly List<(Rect Bounds, ControllerButtons Button)> hitRegions = [];
    private void Selection(DrawingContext c, Rect rect, ControllerButtons button)
    {
        hitRegions.Add((rect.Inflate(4), button));
        if (button != ControllerButtons.None && SelectedButton.HasFlag(button))
            c.DrawRectangle(null, new Pen(Brush.Parse("#2563EB"), 3), rect.Inflate(5), 8, 8);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (SelectButtonCommand is not { } command || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (ButtonAt(e.GetPosition(this)) is { } button && command.CanExecute(button))
        { command.Execute(button); e.Handled = true; }
    }
    internal ControllerButtons? ButtonAt(Point point)
    {
        double scale = Math.Min(Bounds.Width / 600, Bounds.Height / 440);
        if (!double.IsFinite(scale) || scale <= 0) return null;
        point = new((point.X - (Bounds.Width - 600 * scale) / 2) / scale, (point.Y - (Bounds.Height - 440 * scale) / 2) / scale);
        foreach (var hit in hitRegions)
            if (hit.Bounds.Contains(point)) return hit.Button;
        return null;
    }
    public static readonly StyledProperty<ControllerButtons> ButtonsProperty =
        AvaloniaProperty.Register<ControllerTesterControl, ControllerButtons>(nameof(Buttons));
    public static readonly StyledProperty<bool> IsOnlineProperty =
        AvaloniaProperty.Register<ControllerTesterControl, bool>(nameof(IsOnline));
    public static readonly StyledProperty<ControllerLayout> LayoutProperty =
        AvaloniaProperty.Register<ControllerTesterControl, ControllerLayout>(nameof(Layout));
    public static readonly StyledProperty<int> BatteryPercentProperty =
        AvaloniaProperty.Register<ControllerTesterControl, int>(nameof(BatteryPercent), -1);
    public static readonly StyledProperty<double> LeftXProperty =
        AvaloniaProperty.Register<ControllerTesterControl, double>(nameof(LeftX));
    public static readonly StyledProperty<double> LeftYProperty =
        AvaloniaProperty.Register<ControllerTesterControl, double>(nameof(LeftY));
    public static readonly StyledProperty<double> RightXProperty =
        AvaloniaProperty.Register<ControllerTesterControl, double>(nameof(RightX));
    public static readonly StyledProperty<double> RightYProperty =
        AvaloniaProperty.Register<ControllerTesterControl, double>(nameof(RightY));
    public static readonly StyledProperty<double> LeftTriggerProperty =
        AvaloniaProperty.Register<ControllerTesterControl, double>(nameof(LeftTrigger));
    public static readonly StyledProperty<double> RightTriggerProperty =
        AvaloniaProperty.Register<ControllerTesterControl, double>(nameof(RightTrigger));

    static ControllerTesterControl()
    {
        AffectsRender<ControllerTesterControl>(ButtonsProperty, IsOnlineProperty, LayoutProperty,
            BatteryPercentProperty, LeftXProperty, LeftYProperty, RightXProperty, RightYProperty,
            LeftTriggerProperty, RightTriggerProperty, SelectedButtonProperty);
    }

    public ControllerButtons Buttons { get => GetValue(ButtonsProperty); set => SetValue(ButtonsProperty, value); }
    public bool IsOnline { get => GetValue(IsOnlineProperty); set => SetValue(IsOnlineProperty, value); }
    public ControllerLayout Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }
    public int BatteryPercent { get => GetValue(BatteryPercentProperty); set => SetValue(BatteryPercentProperty, value); }
    public double LeftX { get => GetValue(LeftXProperty); set => SetValue(LeftXProperty, value); }
    public double LeftY { get => GetValue(LeftYProperty); set => SetValue(LeftYProperty, value); }
    public double RightX { get => GetValue(RightXProperty); set => SetValue(RightXProperty, value); }
    public double RightY { get => GetValue(RightYProperty); set => SetValue(RightYProperty, value); }
    public double LeftTrigger { get => GetValue(LeftTriggerProperty); set => SetValue(LeftTriggerProperty, value); }
    public double RightTrigger { get => GetValue(RightTriggerProperty); set => SetValue(RightTriggerProperty, value); }


    private static readonly IBrush Ink = Brush.Parse("#394655"), Muted = Brush.Parse("#9BA5B3"), Active = Brush.Parse("#E66A33");
    private static readonly Pen Edge = new(Ink, 2.2), Soft = new(Brush.Parse("#DCE2EA"), 1);
    // Rotate one vector shape for all directions; font glyphs have unequal sizes and baselines.
    private static readonly Geometry DpadArrow = StreamGeometry.Parse("M 0,-4 L 3.5,2 L -3.5,2 Z");
    // The lower shell bridge sits below every stick ring and D-pad arm, including full travel.
    private static readonly Geometry NintendoBody = StreamGeometry.Parse("M 154,98 C 220,78 380,78 446,98 C 487,107 512,132 521,172 C 535,231 549,335 544,365 C 540,398 509,411 486,394 C 465,378 454,345 433,333 C 369,328 231,328 167,333 C 146,345 135,378 114,394 C 91,411 60,398 56,365 C 51,335 65,231 79,172 C 88,132 113,107 154,98 Z");
    private static readonly Geometry XboxBody = StreamGeometry.Parse("M 169,95 C 106,94 75,139 61,216 C 49,283 58,357 92,385 C 137,416 162,376 194,334 C 248,327 352,327 406,334 C 438,376 463,416 508,385 C 542,357 551,283 539,216 C 525,139 494,94 431,95 C 350,81 250,81 169,95 Z");
    private static readonly Geometry PsBody = StreamGeometry.Parse("M 155,98 C 113,97 86,134 76,179 L 48,350 C 40,393 71,413 102,382 L 174,331 C 226,325 374,325 426,331 L 498,382 C 529,413 560,393 552,350 L 524,179 C 514,134 487,97 445,98 C 365,84 235,84 155,98 Z");
    public override void Render(DrawingContext c)
    {
        base.Render(c);
        hitRegions.Clear();
        double scale = Math.Min(Bounds.Width / 600, Bounds.Height / 440);
        if (!double.IsFinite(scale) || scale <= 0) return;
        using (c.PushTransform(Matrix.CreateTranslation((Bounds.Width - 600 * scale) / 2, (Bounds.Height - 440 * scale) / 2)))
        using (c.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            bool ps = Layout.IsPlayStation(), nintendo = Layout.IsNintendo();
            c.DrawGeometry(Brush.Parse("#FAFCFF"), Edge, ps ? PsBody : nintendo ? NintendoBody : XboxBody);
            // Shoulder outlines sit above the body, with separate trigger windows.
            Key(c, 100, 61, 120, 17, ps ? "L1" : nintendo ? "L" : "LB", ControllerButtons.L, 7);
            Key(c, 380, 61, 120, 17, ps ? "R1" : nintendo ? "R" : "RB", ControllerButtons.R, 7);
            Trigger(c, 87, 160, ps ? "L2" : nintendo ? "ZL" : "LT", LeftTrigger, scale);
            Trigger(c, 479, 440, ps ? "R2" : nintendo ? "ZR" : "RT", RightTrigger, scale);
            c.DrawLine(Soft, new(82, 228), new(167, 333)); c.DrawLine(Soft, new(518, 228), new(433, 333));
            if (ps)
            {
                if (Layout != ControllerLayout.DualShock3)
                {
                    Key(c, 238, 123, 124, 66, "", ControllerButtons.Touchpad, 12);
                    c.DrawLine(Soft, new(249, 180), new(351, 180));
                }
                else Text(c, "DUALSHOCK 3", 300, 168, 10, Muted);
                Round(c, 213, 137, "·", ControllerButtons.Minus, 7); Round(c, 387, 137, "≡", ControllerButtons.Plus, 7);
                Round(c, 300, 237, "PS", ControllerButtons.Home, 10);
                Stick(c, 225, 273, LeftX, LeftY, ControllerButtons.LeftStick);
                Stick(c, 375, 273, RightX, RightY, ControllerButtons.RightStick);
                Dpad(c, 154, 196);
                Face(c, 449, 189, "△", "○", "×", "□", ControllerButtons.Y, ControllerButtons.B, ControllerButtons.A, ControllerButtons.X);
            }
            else
            {
                Stick(c, 165, 192, LeftX, LeftY, ControllerButtons.LeftStick);
                Stick(c, 379, 268, RightX, RightY, ControllerButtons.RightStick);
                Dpad(c, 229, 268);
                Face(c, 447, 188, nintendo ? "X" : "Y", nintendo ? "A" : "B", nintendo ? "B" : "A", nintendo ? "Y" : "X",
                    nintendo ? ControllerButtons.X : ControllerButtons.Y, nintendo ? ControllerButtons.A : ControllerButtons.B,
                    nintendo ? ControllerButtons.B : ControllerButtons.A, nintendo ? ControllerButtons.Y : ControllerButtons.X);
                Round(c, 247, 150, nintendo ? "−" : "▱", ControllerButtons.Minus, 10);
                Round(c, 351, 150, nintendo ? "+" : "≡", ControllerButtons.Plus, 10);
                if (nintendo)
                {
                    Text(c, Layout == ControllerLayout.Switch2Pro ? "SWITCH 2 PRO" : "SWITCH PRO", 300, 112, 11, Muted);
                    Key(c, 264, 192, 21, 21, "○", ControllerButtons.Capture, 5);
                    Round(c, 334, 203, "⌂", ControllerButtons.Home, 12);
                    if (Layout == ControllerLayout.Switch2Pro) Round(c, 305, 242, "C", ControllerButtons.C, 11);
                }
                else
                {
                    Round(c, 300, 135, "X", ControllerButtons.Home, 14);
                    Key(c, 290, 207, 20, 16, "", ControllerButtons.Capture, 4);
                }
            }
            if (Layout is ControllerLayout.Switch2Pro or ControllerLayout.DualSenseEdge)
            {
                Key(c, 88, 316, 48, 23, Layout == ControllerLayout.Switch2Pro ? "GL" : "L4", ControllerButtons.GL, 7);
                Key(c, 464, 316, 48, 23, Layout == ControllerLayout.Switch2Pro ? "GR" : "R4", ControllerButtons.GR, 7);
                Text(c, "背键", 112, 344, 10, Muted); Text(c, "背键", 488, 344, 10, Muted);
            }
            Text(c, IsOnline ? "●  LIVE INPUT" : "○  WAITING FOR INPUT", 300, 423, 10, IsOnline ? Active : Muted);
        }
    }
    private bool Pressed(ControllerButtons b) => IsOnline && (Buttons & b) != 0;
    private void Trigger(DrawingContext c, double x, double textX, string label, double value, double scale)
    {
        double level = IsOnline ? TriggerLevels.Normalize(value) : 0;
        var bounds = new Rect(x, 9, 34, 42);
        Selection(c, bounds, x < 300 ? ControllerButtons.ZL : ControllerButtons.ZR);
        c.DrawRectangle(Brush.Parse("#F4F6F9"), null, bounds, 6, 6);
        using (c.PushClip(new RoundedRect(bounds, 6)))
            c.DrawRectangle(Active, null, new Rect(x, 9 + 42 * (1 - level), 34, 42 * level));
        c.DrawRectangle(null, level > 0 ? new Pen(Active, 2.2) : Edge, bounds, 6, 6);
        Text(c, label, textX, 8, Math.Max(12, 10 / scale), Ink);
        Text(c, $"{level * 100:F0}%", textX, 28, Math.Max(16, 12 / scale), level > 0 ? Active : Muted);
    }
    private void Key(DrawingContext c, double x, double y, double w, double h, string label, ControllerButtons b, double radius)
    {
        Selection(c, new Rect(x, y, w, h), b);
        c.DrawRectangle(Pressed(b) ? Active : Brushes.White, Pressed(b) ? new Pen(Active, 2) : Edge, new Rect(x, y, w, h), radius, radius);
        Text(c, label, x + w / 2, y + h / 2 - 7, 11, Pressed(b) ? Brushes.White : Ink);
    }
    private void Round(DrawingContext c, double x, double y, string text, ControllerButtons b, double r = 16)
    {
        Selection(c, new Rect(x-r, y-r, r*2, r*2), b);
        c.DrawEllipse(Pressed(b) ? Active : Brushes.White, Pressed(b) ? new Pen(Active, 2) : Edge, new Point(x, y), r, r);
        Text(c, text, x, y - 8, r > 12 ? 14 : 11, Pressed(b) ? Brushes.White : Ink);
    }
    private void Face(DrawingContext c, double x, double y, string top, string right, string bottom, string left,
        ControllerButtons bt, ControllerButtons br, ControllerButtons bb, ControllerButtons bl)
    {
        Round(c, x, y - 30, top, bt); Round(c, x + 30, y, right, br);
        Round(c, x, y + 30, bottom, bb); Round(c, x - 30, y, left, bl);
    }
    private void Stick(DrawingContext c, double x, double y, double ax, double ay, ControllerButtons b)
    {
        ax = double.IsFinite(ax) ? Math.Clamp(ax,-1,1) : 0;
        ay = double.IsFinite(ay) ? Math.Clamp(ay,-1,1) : 0;
        if (!IsOnline) ax=ay=0;
        Selection(c, new Rect(x-42, y-42, 84, 84), b);
        c.DrawEllipse(Brush.Parse("#EEF2F7"), new Pen(Brush.Parse("#91A0B4"), 2), new(x,y), 42, 42);
        c.DrawEllipse(Brushes.White, Edge, new(x,y), 33, 33);
        c.DrawEllipse(Pressed(b) ? Active : Brush.Parse("#E7EDF6"), Edge, new(x+ax*8,y-ay*8), 24, 24);
        c.DrawEllipse(Pressed(b) ? Brushes.White : Math.Abs(ax)+Math.Abs(ay) > .025 ? Active : Muted, null, new(x+ax*8,y-ay*8), 3, 3);
    }
    private void Dpad(DrawingContext c, double x, double y)
    {
        var geometry = StreamGeometry.Parse($"M {x-11},{y-32} H {x+11} V {y-11} H {x+32} V {y+11} H {x+11} V {y+32} H {x-11} V {y+11} H {x-32} V {y-11} H {x-11} Z");
        c.DrawGeometry(Brush.Parse("#EEF2F7"), Edge, geometry);
        void Arm(ControllerButtons b, double dx, double dy)
        {
            Selection(c, new Rect(x+dx-9, y+dy-9, 18, 18), b);
            if (Pressed(b)) c.DrawRectangle(Active,null,new Rect(x+dx-9,y+dy-9,18,18),3,3);
            c.DrawLine(Soft,new(x,y),new(x+dx*.5,y+dy*.5));
            using (c.PushTransform(Matrix.CreateTranslation(x + dx, y + dy)))
            using (c.PushTransform(Matrix.CreateRotation(Math.Atan2(dx, -dy))))
                c.DrawGeometry(Pressed(b) ? Brushes.White : Ink, null, DpadArrow);
        }
        Arm(ControllerButtons.Up,0,-22); Arm(ControllerButtons.Down,0,22);
        Arm(ControllerButtons.Left,-22,0); Arm(ControllerButtons.Right,22,0);
    }
    private static void Text(DrawingContext c, string value, double centerX, double y, double size, IBrush color)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Microsoft YaHei UI"), size, color);
        c.DrawText(text, new Point(centerX-text.Width/2,y));
    }
}
