using System.IO;
using System.Text.Json;

namespace AutoTyper;

internal sealed record AudioSettings
{
    public string MicrophoneId { get; init; } = "";
    public string OutputId { get; init; } = "";
    public double Depth { get; init; } = 1;
    public double Body { get; init; } = 0.45;
    public double Volume { get; init; } = 0.35;
    internal void Validate()
    {
        if (MicrophoneId is null || OutputId is null || MicrophoneId.Length>2048 || OutputId.Length>2048
            || !double.IsFinite(Depth) || Depth<0 || Depth>3 || !double.IsFinite(Body) || Body<0 || Body>1
            || !double.IsFinite(Volume) || Volume<0 || Volume>1)
            throw new InvalidOperationException("Choose valid microphone preview settings.");
    }
}

internal static class AudioSettingsStore
{
    private static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AutoTyper","audio.json");
    internal static AudioSettings Load()
    {
        try { var settings=JsonSerializer.Deserialize<AudioSettings>(File.ReadAllText(PathName))??new(); settings.Validate(); return settings; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return new(); }
    }
    internal static void Save(AudioSettings settings)
    {
        settings.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName,JsonSerializer.Serialize(settings));
    }
}
