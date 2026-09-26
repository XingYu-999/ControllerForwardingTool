using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ControllerForwardingTool.Views;

/// <summary>Three-axis controller-shaped cuboid. Quaternion rotation, perspective, back-face
/// culling and depth ordering keep the front and rear from painting over one another.</summary>
public sealed class MotionCubeControl : Control
{
    public static readonly StyledProperty<Quaternion> OrientationProperty =
        AvaloniaProperty.Register<MotionCubeControl, Quaternion>(nameof(Orientation), Quaternion.Identity);
    public static readonly StyledProperty<bool> HasMotionProperty =
        AvaloniaProperty.Register<MotionCubeControl, bool>(nameof(HasMotion));
    static MotionCubeControl() => AffectsRender<MotionCubeControl>(OrientationProperty, HasMotionProperty);
    public Quaternion Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public bool HasMotion { get => GetValue(HasMotionProperty); set => SetValue(HasMotionProperty, value); }

    private static readonly Vector3[] Vertices = [new(-1.6f,-.24f,-1),new(1.6f,-.24f,-1),new(1.6f,.24f,-1),new(-1.6f,.24f,-1),
        new(-1.6f,-.24f,1),new(1.6f,-.24f,1),new(1.6f,.24f,1),new(-1.6f,.24f,1)];
    private static readonly int[][] Faces = [[3,7,6,2],[0,1,5,4],[4,5,6,7],[0,3,2,1],[0,4,7,3],[1,2,6,5]];
    private static readonly IBrush[] Colors = [Brush.Parse("#8DB5F7"),Brush.Parse("#365C95"),Brush.Parse("#598BD6"),
        Brush.Parse("#749DDA"),Brush.Parse("#BCD2F3"),Brush.Parse("#ABC5ED")];
    private static readonly Matrix4x4 Camera = Matrix4x4.CreateLookAt(new(3,3.7f,6), Vector3.Zero, Vector3.UnitY);

    public override void Render(DrawingContext c)
    {
        base.Render(c);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        double size = Math.Min(Bounds.Width, Bounds.Height) * 1.5;
        var q = HasMotion && float.IsFinite(Orientation.LengthSquared()) && Orientation.LengthSquared() > .5f
            ? Quaternion.Normalize(Orientation) : Quaternion.Identity;
        Vector3 Transform(Vector3 v) => Vector3.Transform(Vector3.Transform(v, q), Camera);
        Point Project(Vector3 v) => new(Bounds.Width / 2 + v.X * size / -v.Z, Bounds.Height / 2 - v.Y * size / -v.Z);
        Vector3[] cameraVertices = Vertices.Select(Transform).ToArray();
        // A fixed floor makes rotation and depth easier to read.
        var grid = new Pen(Brush.Parse("#E2E9F3"), 1);
        for (int i = -2; i <= 2; i++)
        {
            c.DrawLine(grid, Project(Vector3.Transform(new Vector3(i,-.8f,-2),Camera)), Project(Vector3.Transform(new Vector3(i,-.8f,2),Camera)));
            c.DrawLine(grid, Project(Vector3.Transform(new Vector3(-2,-.8f,i),Camera)), Project(Vector3.Transform(new Vector3(2,-.8f,i),Camera)));
        }
        foreach (int faceIndex in Enumerable.Range(0, Faces.Length).OrderBy(i => Faces[i].Average(v => cameraVertices[v].Z)))
        {
            var face = Faces[faceIndex];
            var a = cameraVertices[face[0]]; var b = cameraVertices[face[1]]; var d = cameraVertices[face[2]];
            if (Vector3.Dot(Vector3.Cross(b-a,d-a), -a) <= 0) continue;
            Polygon(face.Select(i=>Project(cameraVertices[i])).ToArray(), HasMotion ? Colors[faceIndex] : Brush.Parse("#CED6E1"));
            if (faceIndex == 0)
            {
                // Front marker and two stick rings are fixed to the top surface of the model.
                Polygon(new[] {new Vector3(0,.245f,-.85f),new Vector3(-.22f,.245f,-.48f),new Vector3(.22f,.245f,-.48f)}.Select(v=>Project(Transform(v))).ToArray(),Brushes.White);
                foreach (float x in new[] {-.85f,.85f})
                    Polygon(Enumerable.Range(0,24).Select(i=>Project(Transform(new Vector3(x+.22f*MathF.Cos(i*MathF.Tau/24),.245f,.28f+.22f*MathF.Sin(i*MathF.Tau/24))))).ToArray(),Brush.Parse("#EAF2FF"));
            }
        }
        void Polygon(Point[] points, IBrush fill)
        {
            StreamGeometry geometry = new();
            using (var path=geometry.Open()) { path.BeginFigure(points[0],true); foreach(var point in points.Skip(1)) path.LineTo(point); path.EndFigure(true); }
            c.DrawGeometry(fill,new Pen(Brush.Parse("#49688E"),1),geometry);
        }
    }
}
