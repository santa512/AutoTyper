using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoTyper;

internal sealed record SummarySettings
{
    public string ApiKey { get; init; } = "";
    public string SecondaryApiKey { get; init; } = "";
    internal bool IsConfigured { get { try { Validate();return true; } catch(InvalidOperationException) { return false; } } }
    internal void Validate(bool requireKey=true)
    {
        if(ApiKey is null||SecondaryApiKey is null
            ||(requireKey&&ApiKey.Length==0&&SecondaryApiKey.Length==0)
            ||new[]{ApiKey,SecondaryApiKey}.Any(key=>key.Length>0&&!Regex.IsMatch(key,"^[A-Za-z0-9_-]{20,256}$")))
            throw new InvalidOperationException("Enter a valid Groq API key in Settings → AI summary.");
    }
    public override string ToString()=>"Groq summary settings (API key hidden)";
}

internal static class SummarySettingsStore
{
    private static string SettingsPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AutoTyper","summary.dat");
    internal static SummarySettings Load()
    {
        try { return Decode(File.ReadAllBytes(SettingsPath)); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or Win32Exception or InvalidOperationException) { return new(); }
    }
    internal static void Save(SummarySettings settings)
    {
        byte[] encrypted=Encode(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temporary=SettingsPath+".tmp";
        try { File.WriteAllBytes(temporary,encrypted);File.Move(temporary,SettingsPath,true); }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static byte[] Encode(SummarySettings settings)
    {
        settings.Validate(false);byte[] plain=JsonSerializer.SerializeToUtf8Bytes(settings);
        try { return SharingSettingsStore.Protect(plain,true); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    internal static SummarySettings Decode(byte[] encrypted)
    {
        byte[] plain=SharingSettingsStore.Protect(encrypted,false);
        try
        {
            var settings=JsonSerializer.Deserialize<SummarySettings>(plain)??throw new InvalidOperationException("Summary settings are empty.");
            settings.Validate(false);return settings;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
