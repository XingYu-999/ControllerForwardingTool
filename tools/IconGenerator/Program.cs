using SkiaSharp;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "..", "..", "..", "..", ".."));
string target = Path.Combine(root, "NS2ProWin11", "Assets", "NS2ProWin11.ico");
int[] sizes = [16, 32, 256];
List<byte[]> pngs = [];

using SKSurface original = SKSurface.Create(new SKImageInfo(256, 256));
SKCanvas canvas = original.Canvas;
canvas.Clear(SKColors.Transparent);
using SKPaint blue = new() { Color = SKColor.Parse("#0958D9"), IsAntialias = true };
using SKPaint white = new() { Color = SKColors.White, IsAntialias = true };
using SKPaint arc = new() { Color = SKColors.White, IsAntialias = true,
    Style = SKPaintStyle.Stroke, StrokeWidth = 14, StrokeCap = SKStrokeCap.Round };
using SKPaint control = new() { Color = SKColor.Parse("#0958D9"), IsAntialias = true,
    Style = SKPaintStyle.Stroke, StrokeWidth = 11, StrokeCap = SKStrokeCap.Round };

canvas.DrawRoundRect(new SKRect(0, 0, 256, 256), 54, 54, blue);
using SKPath radio = SKPath.ParseSvgPathData("M71 89 C87 70 105 61 128 61 S169 70 185 89");
canvas.DrawPath(radio, arc);
canvas.DrawCircle(71, 89, 7, white);
canvas.DrawCircle(185, 89, 7, white);
using SKPath controller = SKPath.ParseSvgPathData(
    "M73 112 H183 C202 112 217 126 222 145 L230 176 C235 197 213 213 195 202 " +
    "L167 185 H89 L61 202 C43 213 21 197 26 176 L34 145 C39 126 54 112 73 112 Z");
canvas.DrawPath(controller, white);
canvas.DrawLine(78, 139, 78, 169, control);
canvas.DrawLine(63, 154, 93, 154, control);
foreach ((float x, float y) in new[] { (177f, 145f), (191f, 158f), (163f, 158f),
    (177f, 171f), (111f, 170f), (145f, 170f) })
    canvas.DrawCircle(x, y, 6, blue);
canvas.Flush();

using SKImage source = original.Snapshot();
foreach (int size in sizes)
{
    using SKSurface surface = SKSurface.Create(new SKImageInfo(size, size));
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.DrawImage(source, new SKRect(0, 0, size, size),
        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    using SKData png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
    pngs.Add(png.ToArray());
}

using FileStream file = File.Create(target);
using BinaryWriter writer = new(file);
writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)sizes.Length);
int offset = 6 + 16 * sizes.Length;
for (int index = 0; index < sizes.Length; index++)
{
    writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
    writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write((uint)pngs[index].Length);
    writer.Write((uint)offset);
    offset += pngs[index].Length;
}
foreach (byte[] png in pngs) writer.Write(png);
File.WriteAllBytes(Path.ChangeExtension(target, ".png"), pngs[^1]);
Console.WriteLine(target);
