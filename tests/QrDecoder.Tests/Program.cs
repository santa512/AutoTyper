using AutoTyper;
using ZXing;
using ZXing.Common;

const int size = 360;
const string payload = "https://example.com/auto-typer?test=QR\nHello, 世界 👋";
var matrix = new MultiFormatWriter().encode(payload, BarcodeFormat.QR_CODE, size, size,
    new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = "UTF-8" });
byte[] pixels = new byte[size * size * 4];
for (int y = 0; y < size; y++)
for (int x = 0; x < size; x++)
{
    int offset = (y * size + x) * 4;
    byte value = matrix[x, y] ? (byte)0 : (byte)255;
    pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
    pixels[offset + 3] = 0; // GDI BI_RGB alpha is unused; decoding must ignore it.
}
Check("QR / Unicode / unused alpha", pixels, size, size, payload);
byte[] rotated = new byte[pixels.Length];
byte[] inverted = new byte[pixels.Length];
for (int y = 0; y < size; y++)
for (int x = 0; x < size; x++)
for (int channel = 0; channel < 4; channel++)
{
    rotated[((x * size) + size - 1 - y) * 4 + channel] = pixels[(y * size + x) * 4 + channel];
    inverted[(y * size + x) * 4 + channel] = channel == 3 ? (byte)0 : (byte)(255 - pixels[(y * size + x) * 4 + channel]);
}
Check("Rotated QR", rotated, size, size, payload);
Check("Inverted QR", inverted, size, size, payload);
Check("Blank region", Enumerable.Repeat((byte)255, pixels.Length).ToArray(), size, size, null);
Check("Tiny region", new byte[3 * 3 * 4], 3, 3, null);

// Exercise colored modules as well as the usual monochrome QR image.
byte[] colored = (byte[])pixels.Clone();
for (int i = 0; i < colored.Length; i += 4)
    if (colored[i] == 0) { colored[i] = 180; colored[i + 1] = 0; colored[i + 2] = 0; }
Check("Colored QR in BGRA", colored, size, size, payload);

var code128 = new MultiFormatWriter().encode("AUTOTYPER", BarcodeFormat.CODE_128, size, 100);
byte[] barcodePixels = new byte[size * 100 * 4];
for (int y = 0; y < 100; y++)
for (int x = 0; x < size; x++)
{
    int offset = (y * size + x) * 4;
    byte value = code128[x, y] ? (byte)0 : (byte)255;
    barcodePixels[offset] = barcodePixels[offset + 1] = barcodePixels[offset + 2] = value;
}
Check("Reject non-QR barcode", barcodePixels, size, 100, null);
try
{
    QrDecoder.Decode(new byte[12], size, size);
    throw new Exception("Expected invalid buffer to be rejected.");
}
catch (ArgumentException) { Console.WriteLine("PASS: Invalid buffer rejected"); }

// Generate a BMP fixture for manual screen-region scanning through the actual UI.
if (args.Length == 1)
{
    using var file = new BinaryWriter(File.Create(args[0]));
    file.Write((ushort)0x4D42); file.Write(54 + pixels.Length); file.Write(0); file.Write(54);
    file.Write(40); file.Write(size); file.Write(-size); file.Write((ushort)1); file.Write((ushort)32);
    file.Write(0); file.Write(pixels.Length); file.Write(0); file.Write(0); file.Write(0); file.Write(0);
    file.Write(pixels);
}
Console.WriteLine("All QR decoder checks passed.");

static void Check(string name, byte[] bytes, int width, int height, string? expected)
{
    string? actual = QrDecoder.Decode(bytes, width, height);
    if (actual != expected) throw new Exception($"{name}: expected {expected ?? "no result"}, got {actual ?? "no result"}");
    Console.WriteLine($"PASS: {name}");
}
