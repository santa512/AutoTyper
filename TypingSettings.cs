using System.Text.Json;

namespace AutoTyper;

internal sealed record TypingSettings
{
    public double Pace { get; init; } = 1;
    public double Rhythm { get; init; } = 0.5;
    public double MistakeChance { get; init; } = 0.01;
    public double DelaySeconds { get; init; } = 3;
    public bool MinimizeToTray { get; init; }
    public uint HotkeyModifiers { get; init; } = 0x4006;
    public uint HotkeyKey { get; init; } = 0x20;
    public string HotkeyLabel { get; init; } = "Ctrl+Shift+Space";
    public HotkeyGesture ScreenshotHotkey { get; init; } = new(0x4006, 0x53, "Ctrl+Shift+S");
    public HotkeyGesture ShareHotkey { get; init; } = new(0x4006, 0x49, "Ctrl+Shift+I");
    public HotkeyGesture QrHotkey { get; init; } = new(0x4006, 0x51, "Ctrl+Shift+Q");
    public HotkeyGesture TextExtractHotkey { get; init; } = new(0x4006, 0x45, "Ctrl+Shift+E");

    internal Dictionary<string, HotkeyGesture> GetHotkeys() => new()
    {
        ["Typing"] = new(HotkeyModifiers, HotkeyKey, HotkeyLabel), ["Screenshot"] = ScreenshotHotkey,
        ["Share"] = ShareHotkey, ["QR"] = QrHotkey, ["Text Extract"] = TextExtractHotkey
    };

    internal void Validate()
    {
        if (!double.IsFinite(Pace) || Pace < 0.5 || Pace > 2 || !double.IsFinite(Rhythm) || Rhythm < 0 || Rhythm > 1
            || !double.IsFinite(MistakeChance) || MistakeChance < 0 || MistakeChance > 0.12
            || !double.IsFinite(DelaySeconds) || DelaySeconds < 0 || DelaySeconds > 10)
            throw new InvalidOperationException("Enter valid typing settings. Delay must be between 0 and 10 seconds.");
        var hotkeys = GetHotkeys().Values.ToArray();
        foreach (var hotkey in hotkeys)
        {
            if (hotkey is null) throw new InvalidOperationException("Choose a shortcut for each action.");
            hotkey.Validate();
        }
        if (hotkeys.Select(h => (h.Modifiers & 0xf, h.Key)).Distinct().Count() != hotkeys.Length)
            throw new InvalidOperationException("Each action needs a different hotkey.");
    }
}

internal static class TypingSettingsStore
{
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTyper", "typing.json");
    internal static TypingSettings Load(string? path = null)
    {
        try
        {
            string json = File.ReadAllText(path ?? DefaultPath);
            var settings = JsonSerializer.Deserialize<TypingSettings>(json) ?? new();
            using var document = JsonDocument.Parse(json);
            // Older preferences only had the typing shortcut. Preserve it when adding new defaults.
            if (!document.RootElement.TryGetProperty(nameof(TypingSettings.ScreenshotHotkey), out _))
            {
                HotkeyGesture AvoidTypingConflict(HotkeyGesture key) => key.Key == settings.HotkeyKey
                    && (key.Modifiers & 0xf) == (settings.HotkeyModifiers & 0xf)
                        ? key with { Modifiers = 0x4007, Label = "Ctrl+Alt+Shift+" + key.Label.Split('+')[^1] } : key;
                settings = settings with
                {
                    ScreenshotHotkey = AvoidTypingConflict(settings.ScreenshotHotkey), ShareHotkey = AvoidTypingConflict(settings.ShareHotkey),
                    QrHotkey = AvoidTypingConflict(settings.QrHotkey), TextExtractHotkey = AvoidTypingConflict(settings.TextExtractHotkey)
                };
            }
            settings.Validate();
            return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return new(); }
    }
    internal static void Save(TypingSettings settings, string? path = null)
    {
        settings.Validate();
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings));
    }
}
