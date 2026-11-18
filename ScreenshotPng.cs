using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AutoTyper;

internal static class ScreenshotPng
{
    internal static async Task<byte[]> EncodeAsync(CapturedScreen capture, uint? maximumDimension = null)
    {
        if (capture.Width <= 0 || capture.Height <= 0
            || (long)capture.Width * capture.Height * 4 != capture.Pixels.Length)
            throw new ArgumentException("The captured image is incomplete.");
        if (maximumDimension == 0) throw new ArgumentOutOfRangeException(nameof(maximumDimension));

        using var stream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        // GDI leaves the alpha byte undefined. Ignore it to produce fully opaque screenshots.
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            (uint)capture.Width, (uint)capture.Height, 96, 96, capture.Pixels);
        if (maximumDimension is uint limit && Math.Max(capture.Width, capture.Height) > limit)
        {
            double scale = (double)limit / Math.Max(capture.Width, capture.Height);
            encoder.BitmapTransform.ScaledWidth = Math.Max(1, (uint)Math.Round(capture.Width * scale));
            encoder.BitmapTransform.ScaledHeight = Math.Max(1, (uint)Math.Round(capture.Height * scale));
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        await encoder.FlushAsync();
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(checked((uint)stream.Size));
        byte[] png = new byte[checked((int)stream.Size)];
        reader.ReadBytes(png);
        return png;
    }
}
