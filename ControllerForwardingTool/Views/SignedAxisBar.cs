using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ControllerForwardingTool.Views;

public sealed class SignedAxisBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<SignedAxisBar,double>(nameof(Value));
    public double Value { get=>GetValue(ValueProperty); set=>SetValue(ValueProperty,value); }
    static SignedAxisBar() => AffectsRender<SignedAxisBar>(ValueProperty);
    public override void Render(DrawingContext c)
    {
        base.Render(c);
        double mid=Bounds.Width/2, y=Bounds.Height/2;
        double value=double.IsFinite(Value)?Math.Clamp(Value/5,-1,1):0;
        double x=mid+value*Math.Max(0,mid-5);
        c.DrawRectangle(Brush.Parse("#DFE7F1"),null,new Rect(0,y-3,Bounds.Width,6),3,3);
        c.DrawRectangle(Brush.Parse("#9FBEF5"),null,new Rect(Math.Min(mid,x),y-3,Math.Abs(x-mid),6),2,2);
        c.DrawLine(new Pen(Brush.Parse("#73849B"),1),new(mid,y-8),new(mid,y+8));
        c.DrawEllipse(Brush.Parse("#2563EB"),null,new(x,y),5,5);
    }
}
