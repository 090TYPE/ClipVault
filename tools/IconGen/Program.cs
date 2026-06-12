using SkiaSharp;
using Svg.Skia;

// Resolve repo root from the executable location (tools/IconGen/bin/.../).
string root = FindRepoRoot();
string svgPath = Path.Combine(root, "assets", "icon.svg");
string outDir = Path.Combine(root, "src", "ClipVault.App", "Assets");
Directory.CreateDirectory(outDir);

using var svg = new SKSvg();
if (svg.Load(svgPath) is null) { Console.Error.WriteLine("Failed to load SVG"); return 1; }

byte[] RenderPng(int size)
{
    var pic = svg!.Picture!;
    var rect = pic.CullRect;
    using var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.Transparent);
    float scale = size / Math.Max(rect.Width, rect.Height);
    canvas.Scale(scale);
    canvas.DrawPicture(pic);
    canvas.Flush();
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

void WriteIco(string path, int[] sizes)
{
    var pngs = sizes.Select(RenderPng).ToArray();
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);
    w.Write((ushort)0);              // reserved
    w.Write((ushort)1);              // type = icon
    w.Write((ushort)sizes.Length);   // count
    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        int s = sizes[i];
        w.Write((byte)(s >= 256 ? 0 : s)); // width
        w.Write((byte)(s >= 256 ? 0 : s)); // height
        w.Write((byte)0);                  // palette
        w.Write((byte)0);                  // reserved
        w.Write((ushort)1);                // color planes
        w.Write((ushort)32);               // bpp
        w.Write((uint)pngs[i].Length);     // bytes in resource
        w.Write((uint)offset);             // image offset
        offset += pngs[i].Length;
    }
    foreach (var p in pngs) w.Write(p);
}

WriteIco(Path.Combine(outDir, "app.ico"), new[] { 16, 32, 48, 256 });
WriteIco(Path.Combine(outDir, "tray.ico"), new[] { 16, 32 });
File.WriteAllBytes(Path.Combine(outDir, "icon-512.png"), RenderPng(512));

Console.WriteLine("Icons written to " + outDir);
return 0;

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir is not null && !File.Exists(Path.Combine(dir, "ClipVault.sln")))
        dir = Directory.GetParent(dir)?.FullName;
    return dir ?? Directory.GetCurrentDirectory();
}
