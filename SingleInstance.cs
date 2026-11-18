using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace AutoTyper;

// The stable name intentionally does not contain the executable path or build version.
// Portable copies in other folders share the same user/session instance.
internal sealed class SingleInstance : IDisposable
{
    private const string ProductKey = "AutoTyper.Primary.87d35a8c-7e96-4db5-b4e1-02ce8dfd928d";
    private readonly Mutex _mutex;
    private readonly string _windowProperty;
    private bool _ownsMutex;
    private IntPtr _window;
    internal static uint ActivationMessage { get; } = RegisterWindowMessage(ProductKey + ".Activate");

    internal SingleInstance(string? testScope = null)
    {
        string user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string identity = ProductKey + "/" + user + "/" + Process.GetCurrentProcess().SessionId + "/" + testScope;
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        _windowProperty = ProductKey + "." + suffix;
        _mutex = new Mutex(false, @"Local\" + _windowProperty);
    }

    internal bool TryAcquire()
    {
        if (_ownsMutex) return true;
        try { _ownsMutex = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        return _ownsMutex;
    }

    internal bool EnterOrActivateExisting()
    {
        var timer = Stopwatch.StartNew();
        do
        {
            if (TryAcquire()) return true;
            if (TryActivateExistingWindow()) return false;
            // A simultaneous first launch may still be constructing its window.
            Thread.Sleep(50);
        } while (timer.Elapsed < TimeSpan.FromSeconds(15));
        // Never open a competing window if the owner is slow or unresponsive.
        return false;
    }

    internal void PublishWindow(IntPtr window)
    {
        if (!_ownsMutex) throw new InvalidOperationException("Only the primary instance can publish a window.");
        if (!SetProp(window, _windowProperty, new IntPtr(1))) throw new System.ComponentModel.Win32Exception();
        _window = window;
    }

    private bool TryActivateExistingWindow()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (GetProp(window, _windowProperty) == new IntPtr(1)) { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) return false;
        GetWindowThreadProcessId(found, out uint processId);
        AllowSetForegroundWindow(processId);
        if (SendMessageTimeout(found, ActivationMessage, IntPtr.Zero, IntPtr.Zero, 0x0003, 1000, out IntPtr result) == IntPtr.Zero
            || result != new IntPtr(1)) return false;
        // The new launch also has foreground permission from Explorer or its caller.
        SetForegroundWindow(found);
        return true;
    }

    public void Dispose()
    {
        if (_window != IntPtr.Zero) { RemoveProp(_window, _windowProperty); _window = IntPtr.Zero; }
        if (_ownsMutex) { _mutex.ReleaseMutex(); _ownsMutex = false; }
        _mutex.Dispose();
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetProp(IntPtr window, string name, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr RemoveProp(IntPtr window, string name);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
