using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Packs imagegen's eight already drawn poses into an animated avatar; no artwork is redrawn.
if (args.Length != 2) throw new ArgumentException("SpriteToGif input.png output.gif");
using var input = File.OpenRead(args[0]);
var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
var sheet = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
int width = sheet.PixelWidth, height = sheet.PixelHeight, stride = width * 4;
var pixels = new byte[stride * height]; sheet.CopyPixels(pixels, stride, 0);
var cells = new List<(int X, int Y, int W, int H)>();
for (int frame = 0; frame < 8; frame++)
{
    int x0 = width * (frame % 4) / 4, x1 = width * (frame % 4 + 1) / 4;
    int y0 = height * (frame / 4) / 2, y1 = height * (frame / 4 + 1) / 2;
    int minX = x1, minY = y1, maxX = x0, maxY = y0;
    for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
        if (pixels[y * stride + x * 4 + 3] >= 128) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
    if (minX >= maxX || minY >= maxY) throw new InvalidDataException("Empty sprite cell");
    cells.Add((minX, minY, maxX - minX + 1, maxY - minY + 1));
}
var colors = new List<Color> { Colors.Transparent };
colors.AddRange(new BitmapPalette(sheet, 254).Colors.Where(c => c.A >= 128));
var palette = new BitmapPalette(colors);
var cache = new Dictionary<int, byte>();
byte Index(byte b, byte g, byte r)
{
    int key = (r >> 2) << 12 | (g >> 2) << 6 | (b >> 2);
    if (cache.TryGetValue(key, out byte saved)) return saved;
    int best = int.MaxValue; byte found = 1;
    for (int i = 1; i < colors.Count; i++)
    {
        var c = colors[i]; int distance = (r - c.R) * (r - c.R) + (g - c.G) * (g - c.G) + (b - c.B) * (b - c.B);
        if (distance < best) { best = distance; found = (byte)i; }
    }
    cache[key] = found; return found;
}
const int size = 256;
double scale = Math.Min(208d / cells.Max(c => c.H), 206d / cells.Max(c => c.W));
var encoder = new GifBitmapEncoder();
foreach (var cell in cells)
{
    var indexed = new byte[size * size];
    int fw = (int)Math.Round(cell.W * scale), fh = (int)Math.Round(cell.H * scale);
    int left = (size - fw) / 2, top = 232 - fh;
    for (int y = 0; y < fh; y++) for (int x = 0; x < fw; x++)
    {
        int sx = Math.Min(cell.X + cell.W - 1, cell.X + (int)(x / scale));
        int sy = Math.Min(cell.Y + cell.H - 1, cell.Y + (int)(y / scale));
        int p = sy * stride + sx * 4;
        if (pixels[p + 3] >= 128) indexed[(top + y) * size + left + x] = Index(pixels[p], pixels[p + 1], pixels[p + 2]);
    }
    var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Indexed8, palette, indexed, size);
    var metadata = new BitmapMetadata("gif");
    metadata.SetQuery("/grctlext/Delay", (ushort)16);
    metadata.SetQuery("/grctlext/Disposal", (byte)2);
    metadata.SetQuery("/grctlext/TransparencyFlag", true);
    metadata.SetQuery("/grctlext/TransparentColorIndex", (byte)0);
    encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
}
using var encoded = new MemoryStream(); encoder.Save(encoded);
byte[] gif = encoded.ToArray();
int offset = 13 + ((gif[10] & 128) != 0 ? 3 * (1 << ((gif[10] & 7) + 1)) : 0);
// WIC's GIF encoder can discard per-frame timing/disposal metadata. Set the encoded GCE bytes
// explicitly, walking block boundaries (never search inside compressed image data).
int position = offset, controls = 0;
void SkipBlocks()
{
    while (true) { int count = gif[position++]; if (count == 0) return; position += count; }
}
while (position < gif.Length)
{
    byte tag = gif[position++];
    if (tag == 0x3b) break;
    if (tag == 0x21)
    {
        byte label = gif[position++];
        if (label == 0xf9)
        {
            if (gif[position] != 4) throw new InvalidDataException("Unexpected control extension");
            gif[position + 1] = 9; // disposal=restore background, transparent index present
            gif[position + 2] = 16; gif[position + 3] = 0; gif[position + 4] = 0;
            controls++;
        }
        SkipBlocks();
    }
    else if (tag == 0x2c)
    {
        byte packed = gif[position + 8]; position += 9;
        if ((packed & 128) != 0) position += 3 * (1 << ((packed & 7) + 1));
        position++; SkipBlocks();
    }
    else throw new InvalidDataException("Unexpected encoded GIF block");
}
if (controls != 8) throw new InvalidDataException("Missing frame control extensions");
byte[] loop = [0x21, 0xff, 11, .. "NETSCAPE2.0"u8.ToArray(), 3, 1, 0, 0, 0];
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
using (var output = File.Create(args[1])) { output.Write(gif.AsSpan(0, offset)); output.Write(loop); output.Write(gif.AsSpan(offset)); }
using var verifyInput = File.OpenRead(args[1]);
var verify = new GifBitmapDecoder(verifyInput, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
if (verify.Frames.Count != 8) throw new InvalidDataException("Frame count mismatch");
Console.WriteLine($"{size}x{size}, {verify.Frames.Count} frames, 1280 ms loop, {new FileInfo(args[1]).Length} bytes");
