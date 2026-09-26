using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ControllerForwardingTool.Input;

namespace ControllerForwardingTool.Views;

/// <summary>Small input meter with no template minimum width or animated transition.
/// Its track and fill always fit within the space assigned by the readout card.</summary>
public sealed class InputLevelBar : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<InputLevelBar, double>(nameof(Value));
    private static readonly IBrush Track = Brush.Parse("#DEE4EC"), Fill = Brush.Parse("#E66A33");
    static InputLevelBar() => AffectsRender<InputLevelBar>(ValueProperty);
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(Track, null, rect, 2, 2);
        double level = TriggerLevels.Normalize(Value);
        if (level > 0)
            context.DrawRectangle(Fill, null, new Rect(0, 0, rect.Width * level, rect.Height), 2, 2);
    }
}
