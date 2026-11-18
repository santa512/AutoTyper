using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoTyper;

internal sealed record ShareHistoryEntry(string Url, DateTimeOffset CreatedAt)
{
    [JsonIgnore] public DateTimeOffset ExpiresAt => CreatedAt.AddDays(14);
    [JsonIgnore] public string DateLabel => $"Uploaded {CreatedAt.LocalDateTime:g} · "
        + (ExpiresAt <= DateTimeOffset.UtcNow ? $"Expired around {ExpiresAt.LocalDateTime:g}" : $"Expires around {ExpiresAt.LocalDateTime:g}");
    [JsonIgnore] public string TimelineLabel => CreatedAt.LocalDateTime.ToString("MMM d, yyyy\nHH:mm");
    [JsonIgnore] public string ExpiryLabel => ExpiresAt <= DateTimeOffset.UtcNow ? "Expired" : $"Expires {ExpiresAt.LocalDateTime:MMM d}";
}

internal static class ShareHistoryStore
{
    internal const int MaximumEntries = 200;
    private static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTyper", "share-history.dat");
    internal static List<ShareHistoryEntry> Load(string? path = null)
    {
        try
        {
            byte[] plain = SharingSettingsStore.Protect(File.ReadAllBytes(path ?? DefaultPath), false);
            try
            {
                return (JsonSerializer.Deserialize<List<ShareHistoryEntry>>(plain) ?? [])
                    .Where(e => e is not null && ValidUrl(e.Url) && e.CreatedAt.Year is >= 2020 and <= 9998)
                    .OrderByDescending(e => e.CreatedAt).Take(MaximumEntries).ToList();
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or Win32Exception or ArgumentException) { return []; }
    }
    internal static void Add(Uri url, DateTimeOffset createdAt, string? path = null)
    {
        if (!ValidUrl(url.AbsoluteUri) || createdAt.Year is < 2020 or > 9998) throw new InvalidOperationException("Invalid shared URL history entry.");
        var entries = Load(path);
        entries.RemoveAll(e => e.Url == url.AbsoluteUri);
        entries.Insert(0, new(url.AbsoluteUri, createdAt));
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(entries.Take(MaximumEntries));
        try
        {
            byte[] encrypted = SharingSettingsStore.Protect(plain, true);
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, encrypted);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private static bool ValidUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
}
