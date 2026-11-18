using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AutoTyper;

internal sealed record CapturedScreen(byte[] Pixels, int Width, int Height);

internal static class ScreenCapture
{
    // Call after the selector is destroyed and before the main window is restored.
    // Pixels remain in memory only; this does not save screenshots or contact a service.
    internal static CapturedScreen Capture(ScreenRegion region)
    {
        if (region.Width <= 0 || region.Height <= 0 || (long)region.Width * region.Height > 40_000_000)
            throw new ArgumentException("Select a smaller screen region.");

        IntPtr previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
        IntPtr screen = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, previousBitmap = IntPtr.Zero;
        try
        {
            DwmFlush(); // Wait until the selection overlay has left the composed desktop.
            screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw new Win32Exception();
            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) throw new Win32Exception();
            var info = new BitmapInfo
            {
                Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = region.Width, Height = -region.Height,
                Planes = 1, BitCount = 32
            };
            bitmap = CreateDIBSection(screen, ref info, 0, out IntPtr data, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || data == IntPtr.Zero) throw new Win32Exception();
            previousBitmap = SelectObject(memory, bitmap);
            if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1)) throw new Win32Exception();
            if (!BitBlt(memory, 0, 0, region.Width, region.Height, screen, region.X, region.Y, 0x40CC0020))
                throw new Win32Exception();
            GdiFlush();
            byte[] pixels = new byte[checked(region.Width * region.Height * 4)];
            Marshal.Copy(data, pixels, 0, pixels.Length);
            return new CapturedScreen(pixels, region.Width, region.Height);
        }
        finally
        {
            if (previousBitmap != IntPtr.Zero && previousBitmap != new IntPtr(-1)) SelectObject(memory, previousBitmap);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr data, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
