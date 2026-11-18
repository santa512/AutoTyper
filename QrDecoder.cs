using ZXing;
using ZXing.Common;

namespace AutoTyper;

internal static class QrDecoder
{
    internal static string? Decode(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || (long)width * height * 4 != pixels.Length)
            throw new ArgumentException("Expected a complete 32-bit BGR image.");

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                PossibleFormats = new[] { BarcodeFormat.QR_CODE },
                TryHarder = true,
                TryInverted = true
            }
        };
        // BI_RGB screen captures leave the fourth byte undefined; do not treat it as alpha.
        return reader.Decode(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGR32)?.Text;
    }
}
