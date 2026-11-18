using Microsoft.Win32;

namespace AutoTyper;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "AutoTyper";

    internal static string BuildCommand(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || executable.Any(c => c == '"' || char.IsControl(c)))
            throw new InvalidOperationException("Startup requires the installed or portable Auto Typer EXE.");
        return $"\"{executable}\" --startup";
    }

    internal static bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value); }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
        }
    }

    internal static void Apply(bool enabled, string? executable = null, Action<string?>? write = null)
    {
        string? command = enabled ? BuildCommand(executable ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not find the Auto Typer executable for startup.")) : null;
        if (write is not null) { write(command); return; }
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (command is null) key.DeleteValue(ValueName, throwOnMissingValue: false);
        else key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    internal static void RemoveIfMatchingExecutable()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } executable) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string value && string.Equals(value, BuildCommand(executable), StringComparison.OrdinalIgnoreCase))
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
