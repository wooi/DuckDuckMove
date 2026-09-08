using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

if (args.Length != 3) throw new ArgumentException("DuckIcon input.gif output.ico output.png");
using var input = File.OpenRead(args[0]);
var first = new GifBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
var images = new List<(int Size, byte[] Png)>();
foreach (int size in new[] { 16, 24, 32, 48, 64, 128, 256 })
{
    var visual = new DrawingVisual();
    using (var drawing = visual.RenderOpen())
    {
        drawing.PushTransform(new ScaleTransform(size / 256d, size / 256d));
        drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(219, 234, 254)), null, new Rect(5, 5, 246, 246), 49, 49);
        drawing.DrawImage(first, new Rect(9, 3, 238, 238));
        drawing.Pop();
    }
    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
    using var buffer = new MemoryStream(); png.Save(buffer); images.Add((size, buffer.ToArray()));
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
using (var ico = new BinaryWriter(File.Create(args[1])))
{
    ico.Write((ushort)0); ico.Write((ushort)1); ico.Write((ushort)images.Count);
    int offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        ico.Write((byte)(size == 256 ? 0 : size)); ico.Write((byte)(size == 256 ? 0 : size));
        ico.Write((byte)0); ico.Write((byte)0); ico.Write((ushort)1); ico.Write((ushort)32); ico.Write(png.Length); ico.Write(offset); offset += png.Length;
    }
    foreach (var (_, png) in images) ico.Write(png);
}
File.WriteAllBytes(args[2], images[^1].Png);
Console.WriteLine("Duck first-frame icon: 16, 24, 32, 48, 64, 128, 256 px; light-blue rounded-square background.");
