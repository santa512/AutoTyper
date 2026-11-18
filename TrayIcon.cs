using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace AutoTyper;

internal sealed record TrayCommand(string Id, string Title, string Glyph, string? Hotkey = null, bool Enabled = true, bool SeparatorBefore = false);

internal sealed class TrayIcon(IntPtr window, Func<IReadOnlyList<TrayCommand>> menuItems, Action<string> invoke, Func<ElementTheme> theme) : IDisposable
{
    internal const uint CallbackMessage = 0x8052;
    internal static uint TaskbarCreatedMessage { get; } = RegisterWindowMessage("TaskbarCreated");
    private IntPtr _icon;
    private bool _disposed;
    private TrayMenuWindow? _menuWindow;
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
        if (IsVisible) { data.Version = 4; Shell_NotifyIcon(4, ref data); }
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
        if (notification is 0x400 or 0x401 or 0x203) { _menuWindow?.CloseMenu(); invoke("Main"); }
        else if (notification is 0x7b or 0x205)
        {
            int x = unchecked((short)(wParam.ToInt64() & 0xffff));
            int y = unchecked((short)((wParam.ToInt64() >> 16) & 0xffff));
            if (x == -1 && y == -1) { GetCursorPos(out var point); x = point.X; y = point.Y; }
            ShowMenu(x, y);
        }
        return true;
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
}
