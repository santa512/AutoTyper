using System.Windows.Automation;
using System.Runtime.InteropServices;

namespace AutoTyper;

// Runtime IDs distinguish fields sharing one native window (for example browser inputs).
internal sealed class TypingFocusTarget
{
    private readonly IntPtr _window;
    private readonly int[] _runtimeId;
    private readonly IntPtr _inputHandle;
    private TypingFocusTarget(IntPtr window, int[] runtimeId) { _window = window; _runtimeId = runtimeId; _inputHandle = FocusHandle(); }
    internal static TypingFocusTarget Capture(IntPtr ownWindow)
    {
        IntPtr window = NativeKeyboard.GetForegroundWindow();
        if (window == IntPtr.Zero || window == ownWindow)
            throw new InvalidOperationException("Focus the destination text input during the ready delay.");
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null || !element.Current.HasKeyboardFocus || !IsTextInput(element))
                throw new InvalidOperationException("Focus an accessible text input during the ready delay.");
            int[] id = element.GetRuntimeId();
            if (id is null || id.Length == 0 || NativeKeyboard.GetForegroundWindow() != window)
                throw new InvalidOperationException("The destination focus changed. Start again with the text input focused.");
            return new(window, id);
        }
        catch (ElementNotAvailableException) { throw new InvalidOperationException("The destination input is unavailable. Focus it and start again."); }
        catch (System.Runtime.InteropServices.COMException) { throw new InvalidOperationException("Windows could not identify the destination input. Try another text editor."); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiInfo
    {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    private static IntPtr FocusHandle()
    {
        var info = new GuiInfo { Size = (uint)Marshal.SizeOf<GuiInfo>() };
        return GetGUIThreadInfo(0, ref info) ? info.Focus : IntPtr.Zero;
    }
    private static bool IsTextInput(AutomationElement element) =>
        element.Current.ControlType == ControlType.Edit || element.Current.ControlType == ControlType.Document
        || element.TryGetCurrentPattern(ValuePattern.Pattern, out _) || element.TryGetCurrentPattern(TextPattern.Pattern, out _);
    internal bool IsFocused()
    {
        if (NativeKeyboard.GetForegroundWindow() != _window || FocusHandle() != _inputHandle) return false;
        try
        {
            var focused = AutomationElement.FocusedElement;
            return focused is not null && focused.Current.HasKeyboardFocus
                && _runtimeId.SequenceEqual(focused.GetRuntimeId())
                && NativeKeyboard.GetForegroundWindow() == _window;
        }
        catch (ElementNotAvailableException) { return false; }
        catch (System.Runtime.InteropServices.COMException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
