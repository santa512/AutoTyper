using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoTyper;

internal sealed record SettingsBundle(TypingSettings Typing, SharingSettings? Sharing, TranslationSettings Translation, SummarySettings? Summary = null)
{
    internal void Validate()
    {
        if (Typing is null || Translation is null) throw new InvalidOperationException("The backup is missing settings.");
        Typing.Validate();
        Sharing?.Validate();
        Translation.Validate(requireKey: false);
        Summary?.Validate(requireKey: false);
    }
}

internal static class SettingsBackup
{
    internal const int MaximumFileBytes = 2 * 1024 * 1024;
    private const int Iterations = 210000;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("AutoTyper.SettingsBackup.v1");

    internal static void ValidatePassword(string password)
    {
        if (password.Length is < 8 or > 1024)
            throw new InvalidOperationException("Enter a backup password of 8–1,024 characters.");
    }

    internal static byte[] Export(SettingsBundle settings, string password)
    {
        ValidatePassword(password);
        settings.Validate();
        byte[] salt = RandomNumberGenerator.GetBytes(16), nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16];
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(settings), cipher = new byte[plain.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData);
            return JsonSerializer.SerializeToUtf8Bytes(new Envelope("AutoTyper.SettingsBackup", 1,
                Convert.ToBase64String(salt), Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(cipher)));
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    internal static SettingsBundle Import(byte[] data, string password)
    {
        ValidatePassword(password);
        if (data.Length > MaximumFileBytes) throw new InvalidOperationException("The settings backup is too large.");
        try
        {
            Envelope saved = JsonSerializer.Deserialize<Envelope>(data) ?? throw new JsonException();
            if (saved.Format != "AutoTyper.SettingsBackup" || saved.Version != 1)
                throw new InvalidOperationException("This is not a supported Auto Typer settings backup.");
            byte[] salt = Convert.FromBase64String(saved.Salt), nonce = Convert.FromBase64String(saved.Nonce),
                tag = Convert.FromBase64String(saved.Tag), cipher = Convert.FromBase64String(saved.Ciphertext);
            if (salt.Length != 16 || nonce.Length != 12 || tag.Length != 16) throw new FormatException();
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
            byte[] plain = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(key, tag.Length);
                aes.Decrypt(nonce, cipher, tag, plain, AssociatedData);
                SettingsBundle settings = JsonSerializer.Deserialize<SettingsBundle>(plain) ?? throw new JsonException();
                settings.Validate();
                return settings;
            }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
        }
        catch (CryptographicException) { throw new InvalidOperationException("Wrong backup password or damaged backup file."); }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException)
        { throw new InvalidOperationException("This settings backup is damaged or invalid."); }
    }

    private sealed record Envelope(string Format, int Version, string Salt, string Nonce, string Tag, string Ciphertext);
}
