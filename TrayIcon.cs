using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace AutoTyper;

internal sealed record TrayCommand(string Id, string Title, string Glyph, string? Hotkey = null, bool Enabled = true, bool SeparatorBefore = false);

internal sealed class TrayIcon(IntPtr window, Func<IReadOnlyList<TrayCommand>> menuItems, Action<string> invoke,
    Func<ElementTheme> theme, Func<(string Single, string Double)> clickActions) : IDisposable
{
    internal const uint CallbackMessage = 0x8052;
    internal static uint TaskbarCreatedMessage { get; } = RegisterWindowMessage("TaskbarCreated");
    private IntPtr _icon;
    private bool _disposed;
    private TrayMenuWindow? _menuWindow;
    private readonly TrayClickTracker _clicks = new();
    private DispatcherTimer? _singleClickTimer;
    private bool _version4;
    private int _clickX, _clickY;
    internal bool IsVisible { get; private set; }

    internal bool Show()
    {
        if (_disposed) return false;
        if (IsVisible) return true;
        if (_icon == IntPtr.Zero)
            _icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "AutoTyper.ico"), 1, 32, 32, 0x10);
        if (_icon == IntPtr.Zero) return false;
        var data = Data();
        IsVisible = Shell_NotifyIcon(0, ref data);
        if (IsVisible) { data.Version = 4; _version4 = Shell_NotifyIcon(4, ref data); }
        return IsVisible;
    }

    internal bool ProcessMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == TaskbarCreatedMessage && message != 0)
        {
            IsVisible = false;
            Show();
            return true;
        }
        if (message != CallbackMessage) return false;
        uint notification = (uint)(lParam.ToInt64() & 0xffff);
        int x = unchecked((short)(wParam.ToInt64() & 0xffff));
        int y = unchecked((short)((wParam.ToInt64() >> 16) & 0xffff));
        if (!_version4 || (x == -1 && y == -1)) { GetCursorPos(out var point); x = point.X; y = point.Y; }
        if (notification == 0x201) _clicks.ButtonDown();
        else if (notification == 0x203)
        {
            _singleClickTimer?.Stop(); _clicks.DoubleClick();
            InvokeClick(clickActions().Double, x, y);
        }
        else if (notification == 0x401)
        {
            CancelPendingClick(); InvokeClick(clickActions().Single, x, y);
        }
        else if (notification == 0x400 || (!_version4 && notification == 0x202))
        {
            if (_clicks.Select())
            {
                _clickX = x; _clickY = y;
                if (_singleClickTimer is null)
                {
                    _singleClickTimer = new DispatcherTimer();
                    _singleClickTimer.Tick += (_, _) =>
                    {
                        _singleClickTimer.Stop();
                        if (!_disposed && _clicks.TakeSingle()) InvokeClick(clickActions().Single, _clickX, _clickY);
                    };
                }
                _singleClickTimer.Stop();
                _singleClickTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1u, GetDoubleClickTime()));
                _singleClickTimer.Start();
            }
        }
        else if (notification is 0x7b or 0x205)
        {
            CancelPendingClick();
            ShowMenu(x, y);
        }
        return true;
    }

    internal void CancelPendingClick() { _singleClickTimer?.Stop(); _clicks.Cancel(); }
    private void InvokeClick(string action, int x, int y)
    {
        if (_disposed || action == "None") return;
        if (action == "Menu") ShowMenu(x, y);
        else { _menuWindow?.CloseMenu(); invoke(action); }
    }

    private void ShowMenu(int x, int y)
    {
        _menuWindow?.CloseMenu();
        var menu = new TrayMenuWindow(menuItems(), theme(), invoke);
        _menuWindow = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_menuWindow, menu)) _menuWindow = null; };
        menu.ShowAt(x, y);
    }

    private NotifyData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyData>(), Window = window, Id = 1,
        Flags = 1 | 2 | 4 | 0x80, Callback = CallbackMessage, Icon = _icon,
        Tip = "Auto Typer — right-click for tools", Info = "", InfoTitle = ""
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPendingClick();
        _menuWindow?.CloseMenu();
        _menuWindow = null;
        var data = Data();
        Shell_NotifyIcon(2, ref data);
        IsVisible = false;
        if (_icon != IntPtr.Zero) { DestroyIcon(_icon); _icon = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
}
