using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace AutoTyper;

internal sealed class MicrosoftTranslator(HttpClient client)
{
    internal const int MaximumCharacters = 20000;
    internal static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(60), MaxResponseContentBufferSize = 1024 * 1024 };

    internal async Task<string> TranslateAsync(string original, TranslationSettings settings, CancellationToken token)
    {
        settings.Validate();
        if (string.IsNullOrWhiteSpace(original)) throw new InvalidOperationException("The clipboard does not contain text to translate.");
        if (original.Length > MaximumCharacters)
            throw new InvalidOperationException("Clipboard text is too long. Copy up to 20,000 characters and try again.");
        token.ThrowIfCancellationRequested();
        string code = TranslationSettings.LanguageCodes[settings.TargetLanguage];
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&textType=plain&to=" + Uri.EscapeDataString(code));
        request.Headers.Add("Ocp-Apim-Subscription-Key", settings.ApiKey);
        if (settings.Region.Length > 0) request.Headers.Add("Ocp-Apim-Subscription-Region", settings.Region);
        request.Content = JsonContent.Create(new[] { new { Text = original } });
        using HttpResponseMessage response = await client.SendAsync(request, token);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Microsoft Translator authorization failed. Check your API key, Azure resource region and subscription in Translation settings.",
                HttpStatusCode.TooManyRequests => "Microsoft Translator quota or rate limit reached. Check your quota or try again later.",
                HttpStatusCode.BadRequest => "Microsoft Translator could not accept the translation. Check your API settings and clipboard text.",
                _ => "Microsoft Translator translation failed. Try again later."
            });
        try
        {
            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 1
                || root[0].ValueKind != JsonValueKind.Object
                || !root[0].TryGetProperty("translations", out var translations)
                || translations.ValueKind != JsonValueKind.Array || translations.GetArrayLength() != 1)
                throw new JsonException();
            var translation = translations[0];
            if (translation.ValueKind != JsonValueKind.Object
                || !translation.TryGetProperty("to", out var target) || target.ValueKind != JsonValueKind.String
                || !string.Equals(target.GetString(), code, StringComparison.OrdinalIgnoreCase)
                || !translation.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                throw new JsonException();
            string result = text.GetString()!;
            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("Microsoft Translator returned an empty translation. Try again.");
            return result;
        }
        catch (JsonException)
        { throw new InvalidOperationException("Microsoft Translator returned an unreadable response. Try again."); }
    }

    internal static string FormatResult(string original, string translated, string language) =>
        $"Original:\r\n{original}\r\n\r\nTranslation ({language}):\r\n{translated}";
}
