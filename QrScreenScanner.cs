namespace AutoTyper;

internal static class QrScreenScanner
{
    internal static string? Scan(ScreenRegion region)
    {
        CapturedScreen capture = ScreenCapture.Capture(region);
        return QrDecoder.Decode(capture.Pixels, capture.Width, capture.Height);
    }
}
