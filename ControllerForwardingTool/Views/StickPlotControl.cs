using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ControllerForwardingTool.Views;

public sealed class StickPlotControl : Control
{
    public static readonly StyledProperty<double> XProperty = AvaloniaProperty.Register<StickPlotControl, double>(nameof(X));
    public static readonly StyledProperty<double> YProperty = AvaloniaProperty.Register<StickPlotControl, double>(nameof(Y));
    public static readonly StyledProperty<double> FilteredXProperty = AvaloniaProperty.Register<StickPlotControl, double>(nameof(FilteredX));
    public static readonly StyledProperty<double> FilteredYProperty = AvaloniaProperty.Register<StickPlotControl, double>(nameof(FilteredY));
    public static readonly StyledProperty<double> DeadzoneProperty = AvaloniaProperty.Register<StickPlotControl, double>(nameof(Deadzone));
    public static readonly StyledProperty<double[]?> SectorsProperty = AvaloniaProperty.Register<StickPlotControl, double[]?>(nameof(Sectors));
    public double X { get => GetValue(XProperty); set => SetValue(XProperty, value); }
    public double Y { get => GetValue(YProperty); set => SetValue(YProperty, value); }
    public double FilteredX { get => GetValue(FilteredXProperty); set => SetValue(FilteredXProperty, value); }
    public double FilteredY { get => GetValue(FilteredYProperty); set => SetValue(FilteredYProperty, value); }
    public double Deadzone { get => GetValue(DeadzoneProperty); set => SetValue(DeadzoneProperty, value); }
    public double[]? Sectors { get => GetValue(SectorsProperty); set => SetValue(SectorsProperty, value); }
    static StickPlotControl() => AffectsRender<StickPlotControl>(XProperty, YProperty, FilteredXProperty, FilteredYProperty, SectorsProperty, DeadzoneProperty);
    public override void Render(DrawingContext c)
    {
        base.Render(c);
        double r = Math.Max(10, Math.Min(Bounds.Width, Bounds.Height) / 2 - 18);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var grid = new Pen(Brush.Parse("#E2E7EE"), 1);
        Point At(double x, double y) => new(center.X + x * r, center.Y - y * r);
        c.DrawEllipse(Brush.Parse("#FCFDFE"), grid, center, r, r);
        if (Sectors is { Length: > 0 } values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == 0) continue;
                double a = i * 2 * Math.PI / values.Length - Math.PI, b = a + 2 * Math.PI / values.Length;
                double length = Math.Min(values[i], 1.16);
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(center, true); path.LineTo(At(Math.Cos(a) * length, Math.Sin(a) * length));
                    path.LineTo(At(Math.Cos(b) * length, Math.Sin(b) * length)); path.EndFigure(true);
                }
                c.DrawGeometry(Brush.Parse(Math.Abs(values[i] - 1) < .08 ? "#8DCCB8" : "#E8B884"), new Pen(Brush.Parse("#FFFFFF"), .5), geometry);
            }
        }
        c.DrawEllipse(null, grid, center, r / 2, r / 2); c.DrawEllipse(null, new Pen(Brush.Parse("#99A9B8"), 1.3), center, r, r);
        c.DrawLine(grid, At(-1.15, 0), At(1.15, 0)); c.DrawLine(grid, At(0, -1.15), At(0, 1.15));
        if (Deadzone > 0) c.DrawEllipse(Brush.Parse("#22EB7538"), new Pen(Brush.Parse("#E4AA8E"), 1), center, r * Deadzone / 100, r * Deadzone / 100);
        c.DrawEllipse(Brush.Parse("#A7B0BC"), null, At(X, Y), 4, 4);
        c.DrawLine(new Pen(Brush.Parse("#DF6630"), 1.5), center, At(FilteredX, FilteredY));
        c.DrawEllipse(Brush.Parse("#E66A33"), new Pen(Brushes.White, 2), At(FilteredX, FilteredY), 6, 6);
    }
}
