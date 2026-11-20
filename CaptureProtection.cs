using System.Runtime.InteropServices;

namespace AutoTyper;

internal static class CaptureProtection
{
    internal static string? Apply(IntPtr handle, bool enabled)
    {
        if (enabled && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return "Hiding windows from capture requires Windows 10 version 2004 or later.";
        uint expected = enabled ? 0x11u : 0u;
        if (!SetWindowDisplayAffinity(handle, expected))
            return $"Windows could not change screen capture protection (error {Marshal.GetLastWin32Error()}).";
        if (!GetWindowDisplayAffinity(handle, out uint actual) || actual != expected)
            return "Windows did not confirm screen capture protection. Try reopening the app.";
        return null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
}
