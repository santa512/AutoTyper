using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoTyper;

internal sealed record TranslationSettings
{
    internal const string MicrosoftProvider = "MicrosoftTranslator";
    internal static readonly string[] Languages = ["English", "Arabic", "Bengali", "Chinese (Simplified)",
        "Chinese (Traditional)", "Dutch", "French", "German", "Hindi", "Indonesian", "Italian",
        "Japanese", "Korean", "Polish", "Portuguese", "Russian", "Spanish", "Tamil", "Thai", "Turkish",
        "Ukrainian", "Urdu", "Vietnamese"];
    public string ApiKey { get; init; } = "";
    public string Provider { get; init; } = MicrosoftProvider;
    public string Region { get; init; } = "";
    public string TargetLanguage { get; init; } = "English";
    internal static readonly IReadOnlyDictionary<string, string> LanguageCodes = new Dictionary<string, string>
    {
        ["English"] = "en", ["Arabic"] = "ar", ["Bengali"] = "bn", ["Chinese (Simplified)"] = "zh-Hans",
        ["Chinese (Traditional)"] = "zh-Hant", ["Dutch"] = "nl", ["French"] = "fr", ["German"] = "de",
        ["Hindi"] = "hi", ["Indonesian"] = "id", ["Italian"] = "it", ["Japanese"] = "ja", ["Korean"] = "ko",
        ["Polish"] = "pl", ["Portuguese"] = "pt", ["Russian"] = "ru", ["Spanish"] = "es", ["Tamil"] = "ta",
        ["Thai"] = "th", ["Turkish"] = "tr", ["Ukrainian"] = "uk", ["Urdu"] = "ur", ["Vietnamese"] = "vi"
    };
    internal bool IsConfigured
    {
        get { try { Validate(); return true; } catch (InvalidOperationException) { return false; } }
    }

    internal void Validate(bool requireKey = true)
    {
        if ((requireKey || !string.IsNullOrEmpty(ApiKey)) && !Regex.IsMatch(ApiKey ?? "", "^[A-Za-z0-9_-]{20,256}$"))
            throw new InvalidOperationException("Enter a valid Microsoft Translator API key in Translation settings.");
        if (ApiKey is null || Provider != MicrosoftProvider)
            throw new InvalidOperationException("Configure Microsoft Translator in Translation settings.");
        if (Region is null || (Region.Length > 0 && !Regex.IsMatch(Region, "^[a-z0-9-]{2,64}$")))
            throw new InvalidOperationException("Enter the Azure resource region, for example eastus, or leave it blank for a global Translator resource.");
        if (!Languages.Contains(TargetLanguage))
            throw new InvalidOperationException("Choose a translation language.");
    }

    public override string ToString() => "Translation settings (API key hidden)";
}

internal static class TranslationSettingsStore
{
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "AutoTyper", "translation.dat");

    internal static TranslationSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            return Decode(File.ReadAllBytes(SettingsPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
            or Win32Exception or InvalidOperationException) { return new(); }
    }

    internal static void Save(TranslationSettings settings)
    {
        byte[] encrypted = Encode(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temporary = SettingsPath + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static byte[] Encode(TranslationSettings settings)
    {
        settings.Validate(requireKey: false);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(settings);
        try { return SharingSettingsStore.Protect(plain, true); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal static TranslationSettings Decode(byte[] encrypted)
    {
        byte[] plain = SharingSettingsStore.Protect(encrypted, false);
        try
        {
            // Preserve the language, but never send an old Gemini key to a different provider.
            using var json = JsonDocument.Parse(plain);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            if (!json.RootElement.TryGetProperty("Provider", out _))
            {
                string? language = json.RootElement.TryGetProperty("TargetLanguage", out var value)
                    && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                return new TranslationSettings { TargetLanguage = LanguagesOrDefault(language) };
            }
            var settings = JsonSerializer.Deserialize<TranslationSettings>(plain)
                ?? throw new InvalidOperationException("Translation settings are empty.");
            settings.Validate(requireKey: false);
            return settings;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static string LanguagesOrDefault(string? language) =>
        TranslationSettings.Languages.Contains(language) ? language! : "English";
}
