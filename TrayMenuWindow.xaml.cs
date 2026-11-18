using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;

namespace AutoTyper;

public sealed partial class TrayMenuWindow : Window
{
    private readonly List<Button> _buttons = [];
    private readonly Action<string> _invoke;
    private readonly int _logicalHeight;
    private readonly bool _isDark;
    private bool _closing;
    private bool _activated;

    internal TrayMenuWindow(IReadOnlyList<TrayCommand> commands, ElementTheme theme, Action<string> invoke)
    {
        InitializeComponent();
        _invoke = invoke;
        _isDark = theme == ElementTheme.Dark;
        MenuSurface.RequestedTheme = theme;
        _logicalHeight = 14 + commands.Count * 36 + commands.Count(c => c.SeparatorBefore) * 9;
        foreach (var command in commands)
        {
            if (command.SeparatorBefore)
                MenuRows.Children.Add(new Border { Style = (Style)MenuSurface.Resources["TraySeparatorStyle"] });
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new FontIcon { Glyph = command.Glyph, FontSize = 16, FontFamily = new FontFamily("Segoe MDL2 Assets") });
            var label = new TextBlock { Text = command.Title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var shortcut = new TextBlock { Text = command.Hotkey ?? "", FontSize = 12, Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(shortcut, 2); row.Children.Add(shortcut);
            var button = new Button { Content = row, IsEnabled = command.Enabled,
                Style = (Style)MenuSurface.Resources["TrayMenuButtonStyle"] };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, command.Title
                + (command.Hotkey is null ? "" : ", " + command.Hotkey));
            button.Click += (_, _) => { CloseMenu(); _invoke(command.Id); };
            _buttons.Add(button);
            MenuRows.Children.Add(button);
        }
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated) _activated = true;
            else if (_activated) CloseMenu();
        };
        Closed += (_, _) => _closing = true;
        MenuSurface.Loaded += (_, _) => _buttons.FirstOrDefault(button => button.IsEnabled)?.Focus(FocusState.Programmatic);
    }

    internal void ShowAt(int x, int y)
    {
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromPoint(new NativePoint { X = x, Y = y }, 2), ref monitor))
            throw new System.ComponentModel.Win32Exception();
        var work = monitor.Work;
        var area = new RectInt32(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
        // Move to the target monitor before converting logical menu dimensions to physical pixels.
        AppWindow.Move(new PointInt32(area.X, area.Y));
        IntPtr handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = Math.Max(96, GetDpiForWindow(handle)) / 96.0;
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(352 * scale), (int)Math.Ceiling(_logicalHeight * scale)));
        var size = AppWindow.Size;
        var bounds = TrayMenuPlacement.Calculate(x, y, new(area.X, area.Y, area.Width, area.Height),
            size.Width, size.Height, (int)Math.Ceiling(8 * scale));
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        int roundCorners = 2;
        DwmSetWindowAttribute(handle, 33, ref roundCorners, sizeof(int));
        int darkFrame = _isDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref darkFrame, sizeof(int));
        int noFrameBorder = unchecked((int)0xfffffffe);
        DwmSetWindowAttribute(handle, 34, ref noFrameBorder, sizeof(int));
        Activate();
        SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0043);
        SetForegroundWindow(handle);
        _buttons.FirstOrDefault(button => button.IsEnabled)?.Focus(FocusState.Programmatic);
    }

    internal void CloseMenu()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private void MenuSurface_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { e.Handled = true; CloseMenu(); return; }
        if (e.Key is not (VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End)) return;
        e.Handled = true;
        var enabled = _buttons.Where(button => button.IsEnabled).ToList();
        if (enabled.Count == 0) return;
        int current = enabled.FindIndex(button => button.FocusState != FocusState.Unfocused);
        int next = e.Key switch
        {
            VirtualKey.Home => 0, VirtualKey.End => enabled.Count - 1,
            VirtualKey.Up => (current - 1 + enabled.Count) % enabled.Count,
            _ => (current + 1) % enabled.Count
        };
        enabled[next].Focus(FocusState.Keyboard);
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref int value, int size);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
