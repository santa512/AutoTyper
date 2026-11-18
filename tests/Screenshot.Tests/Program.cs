using AutoTyper;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

// Top-down BGRX from GDI: red/green in the first row, blue/white in the second.
// Alpha is deliberately zero to verify exported screenshots remain opaque.
byte[] pixels = [0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0];
byte[] png = await ScreenshotPng.EncodeAsync(new CapturedScreen(pixels, 2, 2));
await WithDecoder(png, async decoder =>
{
    Require(decoder.PixelWidth == 2 && decoder.PixelHeight == 2, "Full-resolution dimensions");
    byte[] decoded = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
        new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
    for (int i = 0; i < decoded.Length; i++)
        if (decoded[i] != (i % 4 == 3 ? (byte)255 : pixels[i]))
            throw new Exception($"Pixel channel {i} changed: color, row orientation, or alpha is incorrect.");
    Console.WriteLine("PASS: PNG preserves colors, top-down orientation, and opaque alpha");
});
foreach ((int width, int height, uint expectedWidth, uint expectedHeight) in new[]
    { (1280, 640, 480u, 240u), (640, 1280, 240u, 480u), (2, 2, 2u, 2u) })
{
    byte[] preview = await ScreenshotPng.EncodeAsync(new CapturedScreen(new byte[width * height * 4], width, height), 480);
    await WithDecoder(preview, decoder =>
    {
        Require(decoder.PixelWidth == expectedWidth && decoder.PixelHeight == expectedHeight,
            $"Preview {width}x{height} keeps aspect ratio without upscaling");
        return Task.CompletedTask;
    });
}
try
{
    await ScreenshotPng.EncodeAsync(new CapturedScreen(new byte[1], 2, 2));
    throw new Exception("Invalid image was accepted.");
}
catch (ArgumentException) { Console.WriteLine("PASS: Incomplete capture rejected"); }
Console.WriteLine("All screenshot checks passed.");

static async Task WithDecoder(byte[] png, Func<BitmapDecoder, Task> test)
{
    using var stream = new InMemoryRandomAccessStream();
    using (var writer = new DataWriter(stream))
    {
        writer.WriteBytes(png);
        await writer.StoreAsync();
        writer.DetachStream();
    }
    stream.Seek(0);
    await test(await BitmapDecoder.CreateAsync(stream));
}
static void Require(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    Console.WriteLine($"PASS: {label}");
}
