namespace AutoTyper;

// Capture one foreground application window; controls within it may change freely.
internal sealed class TypingFocusTarget
{
    private readonly IntPtr _window;
    private readonly uint _process;
    private TypingFocusTarget(IntPtr window, uint process) { _window = window; _process = process; }

    internal static TypingFocusTarget Capture(IntPtr ownWindow)
    {
        IntPtr window = NativeKeyboard.GetForegroundWindow();
        GetWindowThreadProcessId(window, out uint process);
        if (window == IntPtr.Zero || window == ownWindow || process == 0 || process == Environment.ProcessId)
            throw new InvalidOperationException("Focus another application during the ready delay.");
        return new(window, process);
    }

    internal bool IsFocused()
    {
        if (NativeKeyboard.GetForegroundWindow() != _window) return false;
        GetWindowThreadProcessId(_window, out uint process);
        return process == _process;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
