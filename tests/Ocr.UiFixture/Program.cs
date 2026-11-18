using System.Runtime.InteropServices;

// A visible, disposable fixture. It only paints known English text for desktop OCR tests.
internal static class Program
{
    private static readonly Proc Callback = WindowProc;
    [STAThread]
    private static void Main()
    {
        IntPtr module = GetModuleHandle(null);
        var cls = new WindowClass { Procedure = Callback, Instance = module,
            Background = new IntPtr(6), ClassName = "AutoTyperOcrFixture" };
        RegisterClass(ref cls);
        IntPtr window = CreateWindowEx(0, cls.ClassName, "Auto Typer English OCR Test", 0x00CF0000,
            180, 180, 740, 220, IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);
        ShowWindow(window, 5);
        while (GetMessage(out Message message, IntPtr.Zero, 0, 0) > 0)
        { TranslateMessage(ref message); DispatchMessage(ref message); }
    }
    private static IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == 1)
        {
            IntPtr label = CreateWindowEx(0, "STATIC", "PaddleOCR reads English screen text.\r\nOrder ID: AT-2026-0142\r\nEmail: hello@example.com", 0x50000000,
                20, 20, 690, 130, window, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            IntPtr font = GetStockObject(17);
            SendMessage(label, 0x30, font, new IntPtr(1));
        }
        if (message == 2) { PostQuitMessage(0); return IntPtr.Zero; }
        return DefWindowProc(window, message, wParam, lParam);
    }
    private delegate IntPtr Proc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    { public uint Style; public Proc Procedure; public int ClassExtra, WindowExtra; public IntPtr Instance, Icon, Cursor, Background; public string? Menu; public string ClassName; }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr module, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int show);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int id);
}
