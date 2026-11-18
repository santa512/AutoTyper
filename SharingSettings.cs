using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoTyper;

internal sealed class SharingSettings
{
    internal const string DefaultPublicBaseUrl = "https://pub-12e865cb46304291a8d559f120fb58f1.r2.dev/";
    public string AccountId { get; init; } = "";
    public string BucketName { get; init; } = "";
    public string AccessKeyId { get; init; } = "";
    public string SecretAccessKey { get; init; } = "";
    public string Jurisdiction { get; init; } = "default";
    public string PublicBaseUrl { get; init; } = DefaultPublicBaseUrl;

    internal void Validate()
    {
        if (!Regex.IsMatch(AccountId ?? "", "^[a-fA-F0-9]{32}$"))
            throw new InvalidOperationException("Enter your 32-character Cloudflare account ID.");
        if (!Regex.IsMatch(BucketName ?? "", "^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$"))
            throw new InvalidOperationException("Enter a valid R2 bucket name (3–63 lowercase letters, numbers or hyphens).");
        if (Jurisdiction is not ("default" or "eu" or "us" or "fedramp"))
            throw new InvalidOperationException("Choose the bucket's jurisdiction.");
        ValidateUrl(PublicBaseUrl, "Enter a valid HTTPS public bucket URL.");
        if (!Regex.IsMatch(AccessKeyId ?? "", "^[a-zA-Z0-9]{16,128}$")
            || !Regex.IsMatch(SecretAccessKey ?? "", "^[a-zA-Z0-9/+=]{32,256}$"))
            throw new InvalidOperationException("Enter the Access Key ID and Secret Access Key from your R2 API token.");
    }

    internal string EndpointHost => AccountId.ToLowerInvariant()
        + (Jurisdiction == "default" ? "" : "." + Jurisdiction) + ".r2.cloudflarestorage.com";

    private static void ValidateUrl(string value, string message)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException(message);
    }

    public override string ToString() => "Screenshot sharing settings (credentials hidden)";
}

internal static class SharingSettingsStore
{
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "AutoTyper", "sharing.json");

    internal static SharingSettings? Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            SavedSettings? saved = JsonSerializer.Deserialize<SavedSettings>(File.ReadAllText(SettingsPath));
            if (saved is null || string.IsNullOrEmpty(saved.EncryptedCredentials)) return null;
            byte[] plain = Protect(Convert.FromBase64String(saved.EncryptedCredentials), false);
            SharingSettings? settings;
            try { settings = JsonSerializer.Deserialize<SharingSettings>(plain); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain); }
            if (settings is null) return null;
            settings.Validate();
            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or FormatException or Win32Exception or InvalidOperationException)
        { return null; }
    }

    internal static void Save(SharingSettings settings)
    {
        settings.Validate();
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(settings);
        string protectedToken;
        try { protectedToken = Convert.ToBase64String(Protect(plain, true)); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain); }
        string json = JsonSerializer.Serialize(new SavedSettings(protectedToken));
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, json);
    }

    // DPAPI ties the saved configuration to the current Windows user. No credentials ship in the EXE.
    internal static byte[] Protect(byte[] data, bool encrypt)
    {
        IntPtr inputMemory = Marshal.AllocHGlobal(data.Length);
        var output = new Blob();
        try
        {
            Marshal.Copy(data, 0, inputMemory, data.Length);
            var input = new Blob { Length = data.Length, Data = inputMemory };
            bool success = encrypt
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception();
            byte[] result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[data.Length], 0, inputMemory, data.Length);
            Marshal.FreeHGlobal(inputMemory);
            if (output.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
                LocalFree(output.Data);
            }
        }
    }

    private sealed record SavedSettings(string EncryptedCredentials);
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
