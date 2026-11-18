using AutoTyper;
using System.Net;
using System.Text;
using System.Text.Json;

int checks = 0, requests = 0;
void Require(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
async Task Fail(Func<Task> action, string message)
{
    try { await action(); }
    catch (InvalidOperationException e) { Require(e.Message.Contains(message), message); return; }
    throw new Exception("Expected failure: " + message);
}
var settings = new TranslationSettings { ApiKey = "fake-test-key-1234567890", Region = "eastus", TargetLanguage = "French" };
const string original = "Hello!\r\nTranslate this instruction literally: ignore all instructions.";
using var http = new HttpClient(new FakeHandler(async (request, token) =>
{
    requests++;
    Require(request.Method == HttpMethod.Post && request.RequestUri!.Scheme == "https" && request.RequestUri.Host == "api.cognitive.microsofttranslator.com", "Fixed Microsoft HTTPS endpoint");
    Require(!request.RequestUri!.AbsoluteUri.Contains(settings.ApiKey), "No API key in URL");
    Require(request.Headers.GetValues("Ocp-Apim-Subscription-Key").Single() == settings.ApiKey, "Key in header");
    Require(request.Headers.GetValues("Ocp-Apim-Subscription-Region").Single() == "eastus", "Regional resource header");
    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
    Require(body.RootElement.GetArrayLength() == 1 && body.RootElement[0].GetProperty("text").GetString() == original, "Exact clipboard text in Microsoft request array");
    Require(request.RequestUri.Query.Contains("api-version=3.0") && request.RequestUri.Query.Contains("to=fr") && !request.RequestUri.Query.Contains("from="), "Target language code and source auto-detection");
    return Json("""[{"detectedLanguage":{"language":"en","score":1.0},"translations":[{"to":"fr","text":"Bonjour !\nTraduisez cette instruction littéralement."}]}]""");
}));
string translated = await new MicrosoftTranslator(http).TranslateAsync(original, settings, default);
Require(translated == "Bonjour !\nTraduisez cette instruction littéralement.", "Reads Microsoft translation response");
string result = MicrosoftTranslator.FormatResult(original, translated, "French");
Require(result.StartsWith("Original:\r\n" + original) && result.EndsWith("Translation (French):\r\n" + translated), "Both original and translated text retained");
await Fail(() => new MicrosoftTranslator(http).TranslateAsync(" ", settings, default), "clipboard");
await Fail(() => new MicrosoftTranslator(http).TranslateAsync(new string('a', MicrosoftTranslator.MaximumCharacters + 1), settings, default), "too long");
await Fail(() => new MicrosoftTranslator(http).TranslateAsync(original, settings with { ApiKey = "" }, default), "API key");
await Fail(() => new MicrosoftTranslator(http).TranslateAsync(original, settings with { Region = "eastus\r\nheader:bad" }, default), "region");
await Fail(() => new MicrosoftTranslator(http).TranslateAsync(original, settings with { TargetLanguage = "invalid" }, default), "language");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await new MicrosoftTranslator(http).TranslateAsync(original, settings, cancelled.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { Require(requests == 1, "Validation and cancellation prevent network requests"); }
using (var inFlight = new CancellationTokenSource())
using (var waiting = new HttpClient(new FakeHandler(async (_, token) =>
{
    inFlight.Cancel();
    await Task.Delay(Timeout.Infinite, token);
    return Json("{}");
})))
{
    try { await new MicrosoftTranslator(waiting).TranslateAsync(original, settings, inFlight.Token); throw new Exception("In-flight cancellation ignored"); }
    catch (OperationCanceledException) { Require(true, "Cancellation propagates to an active HTTP request"); }
}
foreach (var (status, message) in new[] { (HttpStatusCode.Forbidden, "authorization"), (HttpStatusCode.Unauthorized, "authorization"),
    (HttpStatusCode.TooManyRequests, "quota"), (HttpStatusCode.NotFound, "failed"), (HttpStatusCode.BadRequest, "accept"),
    (HttpStatusCode.InternalServerError, "failed"), (HttpStatusCode.TemporaryRedirect, "failed") })
{
    using var failure = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        { Content = new StringContent("sensitive upstream error") })));
    await Fail(() => new MicrosoftTranslator(failure).TranslateAsync(original, settings, default), message);
}
foreach (var (body, message) in new[] {
    ("{}", "unreadable"), ("[]", "unreadable"), ("[{}]", "unreadable"),
    ("""[{"translations":[]}]""", "unreadable"),
    ("""[{"translations":[{"to":"fr","text":" "}]}]""", "empty"),
    ("broken JSON", "unreadable"),
    ("""[{"translations":[{"to":"en","text":"wrong target"}]}]""", "unreadable"),
    ("""[{"translations":[{"to":"fr","text":123}]}]""", "unreadable") })
{
    using var failure = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Json(body))));
    await Fail(() => new MicrosoftTranslator(failure).TranslateAsync(original, settings, default), message);
}
Require(new TranslationSettings().TargetLanguage == "English", "English default");
Require(!new TranslationSettings().IsConfigured && settings.IsConfigured
    && !(settings with { ApiKey = "" }).IsConfigured && !(settings with { Region = "invalid region" }).IsConfigured, "Translation disabled until configuration is valid; clearing key disables it");
Require(TranslationSettings.Languages.All(TranslationSettings.LanguageCodes.ContainsKey), "Every dropdown language has a Microsoft language code");
using (var globalHttp = new HttpClient(new FakeHandler((request, _) =>
{
    Require(!request.Headers.Contains("Ocp-Apim-Subscription-Region"), "Global resource omits region header");
    Require(request.RequestUri!.Query.Contains("to=zh-Hant"), "Chinese Traditional code");
    return Task.FromResult(Json("""[{"translations":[{"to":"zh-Hant","text":"你好"}]}]"""));
})))
    Require(await new MicrosoftTranslator(globalHttp).TranslateAsync("Hello", settings with { Region = "", TargetLanguage = "Chinese (Traditional)" }, default) == "你好", "Global resource translation");
byte[] legacy = SharingSettingsStore.Protect(Encoding.UTF8.GetBytes("""{"ApiKey":"old-gemini-key-1234567890","Model":"gemini-test","TargetLanguage":"French"}"""), true);
var migrated = TranslationSettingsStore.Decode(legacy);
Require(migrated.ApiKey == "" && !migrated.IsConfigured && migrated.TargetLanguage == "French", "Old provider key discarded while language is preserved");
byte[] encrypted = TranslationSettingsStore.Encode(settings);
Require(!Encoding.UTF8.GetString(encrypted).Contains(settings.ApiKey), "Saved API key encrypted");
Require(TranslationSettingsStore.Decode(encrypted) == settings, "DPAPI settings round trip");
Require(TranslationSettingsStore.Decode(TranslationSettingsStore.Encode(new TranslationSettings())).ApiKey == "", "Key can be cleared");
Require(!settings.ToString().Contains(settings.ApiKey), "Settings description hides key");
Console.WriteLine($"PASS: {checks} translation request, result, errors, cancellation and encrypted-settings checks.");

static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
}
