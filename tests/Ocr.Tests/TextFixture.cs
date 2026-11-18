using System.Runtime.InteropServices;
using AutoTyper;

// Render synthetic screenshot-like text into an offscreen bitmap. No desktop input or capture.
internal static class TextFixture
{
    internal static CapturedScreen Render(string text, string fontName, int size, uint foreground, uint background, int lineGap = 9)
    {
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        IntPtr font = CreateFont(-size, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 4, 0, fontName);
        IntPtr oldFont = SelectObject(dc, font);
        IntPtr bitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero, brush = IntPtr.Zero;
        try
        {
            string[] lines = text.Split('\n');
            int width = 0;
            foreach (string line in lines)
            {
                GetTextExtentPoint32(dc, line, line.Length, out Size extent);
                width = Math.Max(width, extent.Width);
            }
            width += 32;
            int height = lines.Length * (size + lineGap) + 24;
            var info = new BitmapInfo { Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(dc, ref info, 0, out IntPtr data, IntPtr.Zero, 0);
            oldBitmap = SelectObject(dc, bitmap);
            brush = CreateSolidBrush(background);
            var area = new Rect { Right = width, Bottom = height };
            FillRect(dc, ref area, brush);
            SetBkMode(dc, 1); SetTextColor(dc, foreground);
            for (int i = 0; i < lines.Length; i++) TextOut(dc, 16, 12 + i * (size + lineGap), lines[i], lines[i].Length);
            GdiFlush();
            byte[] pixels = new byte[width * height * 4];
            Marshal.Copy(data, pixels, 0, pixels.Length);
            return new CapturedScreen(pixels, width, height);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero) SelectObject(dc, oldBitmap);
            SelectObject(dc, oldFont);
            if (brush != IntPtr.Zero) DeleteObject(brush);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            DeleteObject(font); DeleteDC(dc);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int X, Y; public uint ColorsUsed, ColorsImportant;
    }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeout, uint charset, uint output, uint clip, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool GetTextExtentPoint32(IntPtr dc, string text, int length, out Size extent);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr data, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref Rect rect, IntPtr brush);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool TextOut(IntPtr dc, int x, int y, string text, int count);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
