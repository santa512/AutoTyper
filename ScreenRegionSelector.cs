using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AutoTyper;

internal readonly record struct ScreenRegion(int X, int Y, int Width, int Height);

// A selection surface only: no screen pixels are captured or exported.
// This window shares the WinUI thread's message pump and uses physical screen coordinates.
internal sealed class ScreenRegionSelector : IDisposable
{
    private readonly string _className = "AutoTyper.Region." + Guid.NewGuid().ToString("N");
    private readonly string _tool;
    private readonly WindowProc _procedure;
    private readonly TaskCompletionSource<ScreenRegion?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IntPtr _module = GetModuleHandle(null);
    private IntPtr _window;
    private bool _registered, _dragging, _finished;
    private int _left, _top, _width, _height;
    private Point _start, _end;

    internal ScreenRegionSelector(string tool)
    {
        _tool = tool;
        _procedure = ProcessMessage;
    }

    internal Task<ScreenRegion?> SelectAsync()
    {
        IntPtr previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Procedure = _procedure,
                Instance = _module,
                Cursor = LoadCursor(IntPtr.Zero, new IntPtr(32515)),
                ClassName = _className
            };
            if (RegisterClassEx(ref windowClass) == 0) throw new Win32Exception();
            _registered = true;
            _left = GetSystemMetrics(76);
            _top = GetSystemMetrics(77);
            _width = GetSystemMetrics(78);
            _height = GetSystemMetrics(79);
            _window = CreateWindowEx(0x000C0008, _className, "Auto Typer — Select region", 0x80000000,
                _left, _top, _width, _height, IntPtr.Zero, IntPtr.Zero, _module, IntPtr.Zero);
            if (_window == IntPtr.Zero) throw new Win32Exception();
            if (!SetLayeredWindowAttributes(_window, 0, 150, 2)) throw new Win32Exception();
            ShowWindow(_window, 5);
            SetForegroundWindow(_window);
            SetFocus(_window);
            return _completion.Task;
        }
        finally
        {
            if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    internal void Cancel() => Finish(null);

    private void Finish(ScreenRegion? region)
    {
        if (_finished) return;
        _finished = true;
        _dragging = false;
        // Complete before WM_NCDESTROY so normal teardown cannot replace a valid selection.
        _completion.TrySetResult(region);
        if (GetCapture() == _window) ReleaseCapture();
        if (_window != IntPtr.Zero) DestroyWindow(_window);
    }

    private Point MousePosition(IntPtr lParam)
    {
        // Use the position when this event occurred, even when input is queued quickly.
        long packed = lParam.ToInt64();
        return new Point { X = Math.Clamp((short)(packed & 0xFFFF), 0, _width),
            Y = Math.Clamp((short)((packed >> 16) & 0xFFFF), 0, _height) };
    }

    private Rect Selection => new()
    {
        Left = Math.Min(_start.X, _end.X), Top = Math.Min(_start.Y, _end.Y),
        Right = Math.Max(_start.X, _end.X), Bottom = Math.Max(_start.Y, _end.Y)
    };

    private IntPtr ProcessMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        // Never unwind a managed exception through a native window procedure.
        try
        {
            switch (message)
            {
                case 0x14: return new IntPtr(1); // WM_ERASEBKGND
                case 0x0F: Paint(window); return IntPtr.Zero;
                case 0x201: // WM_LBUTTONDOWN
                    _start = _end = MousePosition(lParam);
                    _dragging = true;
                    SetCapture(window);
                    InvalidateRect(window, IntPtr.Zero, false);
                    return IntPtr.Zero;
                case 0x200 when _dragging:
                    _end = MousePosition(lParam);
                    InvalidateRect(window, IntPtr.Zero, false);
                    return IntPtr.Zero;
                case 0x202 when _dragging:
                    _end = MousePosition(lParam);
                    Rect selected = Selection;
                    _dragging = false;
                    if (selected.Right - selected.Left >= 3 && selected.Bottom - selected.Top >= 3)
                        Finish(new ScreenRegion(_left + selected.Left, _top + selected.Top,
                            selected.Right - selected.Left, selected.Bottom - selected.Top));
                    else
                    {
                        ReleaseCapture();
                        InvalidateRect(window, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case 0x100 when wParam.ToInt32() == 0x1B: // Escape
                case 0x204: // Right-click
                case 0x10: // Close
                case 0x7E: // Display layout changed
                case 0x2E0: // DPI changed
                    Cancel(); return IntPtr.Zero;
                case 0x8 when !_finished: // Focus lost (e.g. Alt+Tab)
                case 0x215 when _dragging: // Capture lost
                    Cancel(); return IntPtr.Zero;
                case 0x82: // WM_NCDESTROY
                    _window = IntPtr.Zero;
                    _completion.TrySetResult(null);
                    break;
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            Cancel();
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private void Paint(IntPtr window)
    {
        IntPtr dc = BeginPaint(window, out PaintStruct paint);
        try
        {
            var background = new Rect { Right = _width, Bottom = _height };
            FillRect(dc, ref background, GetStockObject(4)); // BLACK_BRUSH
            SetBkMode(dc, 1);
            SetTextColor(dc, 0x00FFFFFF);
            uint dpi = GetDpiForWindow(window);
            int padding = (int)(24 * dpi / 96);
            int lineHeight = (int)(36 * dpi / 96);
            IntPtr font = CreateFont(-(int)(18 * dpi / 96), 0, 0, 0, 500, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
            IntPtr oldFont = SelectObject(dc, font);
            try
            {
                // Anchor instructions to the monitor under the cursor, including secondary monitors.
                GetCursorPos(out Point cursor);
                var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                GetMonitorInfo(MonitorFromPoint(cursor, 2), ref monitor);
                var hint = new Rect { Left = monitor.Work.Left - _left + padding,
                    Top = monitor.Work.Top - _top + padding, Right = monitor.Work.Right - _left - padding };
                hint.Bottom = hint.Top + lineHeight;
                string instruction = $"{_tool} · Drag to select a region · Esc or right-click to cancel";
                DrawText(dc, instruction, -1, ref hint, 0x20);
                if (_dragging)
                {
                    Rect rectangle = Selection;
                    IntPtr pen = CreatePen(0, Math.Max(2, (int)(2 * dpi / 96)), 0x0000D7FF);
                    IntPtr oldPen = SelectObject(dc, pen);
                    IntPtr oldBrush = SelectObject(dc, GetStockObject(5)); // NULL_BRUSH
                    Rectangle(dc, rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
                    SelectObject(dc, oldBrush);
                    SelectObject(dc, oldPen);
                    DeleteObject(pen);
                    string size = $"{rectangle.Right - rectangle.Left} × {rectangle.Bottom - rectangle.Top} px";
                    var label = new Rect { Left = Math.Min(rectangle.Left + padding, _width - 200),
                        Top = Math.Clamp(rectangle.Bottom + 8, padding + lineHeight, _height - lineHeight),
                        Right = _width - padding };
                    label.Bottom = label.Top + lineHeight;
                    DrawText(dc, size, -1, ref label, 0x20);
                }
            }
            finally { SelectObject(dc, oldFont); DeleteObject(font); }
        }
        finally { EndPaint(window, ref paint); }
    }

    public void Dispose()
    {
        Cancel();
        if (_registered) { UnregisterClass(_className, _module); _registered = false; }
        GC.KeepAlive(_procedure);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public WindowProc Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct PaintStruct
    {
        public IntPtr Dc;
        public int Erase;
        public Rect Paint;
        public int Restore, IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int metric);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr window, IntPtr rect, bool erase);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr window, ref PaintStruct paint);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref Rect rect, IntPtr brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawText(IntPtr dc, string text, int length, ref Rect rect, uint format);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int index);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] private static extern bool Rectangle(IntPtr dc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeout, uint charset, uint output, uint clip, uint quality, uint pitch, string face);
}
