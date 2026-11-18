using AutoTyper;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

var settings = new SharingSettings { AccountId = "0123456789abcdef0123456789abcdef", BucketName = "screenshots", AccessKeyId = new string('a',32), SecretAccessKey = new string('s',64) };
byte[] png = [137,80,78,71,13,10,26,10,0];
using (var signed = new HttpRequestMessage(HttpMethod.Put, $"https://{settings.EndpointHost}/screenshots/share/test.png"))
{
    ScreenshotShareClient.Sign(signed, png, settings.AccessKeyId, settings.SecretAccessKey, new DateTimeOffset(2026,11,18,12,0,0,TimeSpan.Zero));
    Require(signed.Headers.GetValues("Authorization").Single().EndsWith("Signature=2a56490e2c43fe5821d8e3f68fdcc75a5b93e1dc770d1acd80081b25a767da53"), "Signature matches independent Node crypto fixture");
}
int sent = 0;
string? uploadedKey = null;
using var http = new HttpClient(new FakeHandler(async (r,t) =>
{
    sent++;
    Require(r.Method == HttpMethod.Put && r.RequestUri!.Host == settings.EndpointHost, "PUT to exact R2 account endpoint");
    Require(Regex.IsMatch(r.RequestUri!.AbsolutePath, @"^/screenshots/share/[0-9a-f]{32}\.png$"), "Unique screenshot object path");
    uploadedKey = r.RequestUri.AbsolutePath["/screenshots/".Length..];
    Require(r.Headers.GetValues("Authorization").Single().StartsWith("AWS4-HMAC-SHA256 Credential=" + settings.AccessKeyId), "Signed authorization header");
    Require(r.Headers.GetValues("x-amz-content-sha256").Single() == Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(), "Payload integrity hash");
    Require(r.Content!.Headers.ContentType!.MediaType == "image/png" && (await r.Content.ReadAsByteArrayAsync(t)).SequenceEqual(png), "PNG type and exact bytes");
    Require(!r.RequestUri.AbsoluteUri.Contains(settings.SecretAccessKey), "Credentials absent from URL");
    return new HttpResponseMessage(HttpStatusCode.OK);
}));
Uri link = await new ScreenshotShareClient(http).UploadAsync(png,settings,default);
Require(link.AbsoluteUri == SharingSettings.DefaultPublicBaseUrl + uploadedKey, "Public URL uses uploaded key");
await Fail(() => new ScreenshotShareClient(http).UploadAsync([1,2,3], settings, default));
await Fail(() => new ScreenshotShareClient(http).UploadAsync(new byte[ScreenshotShareClient.MaximumPngBytes+1], settings, default));
Require(sent==1, "Invalid captures never upload");
foreach (var status in new[] { HttpStatusCode.Forbidden,HttpStatusCode.NotFound,HttpStatusCode.TooManyRequests,HttpStatusCode.InternalServerError,HttpStatusCode.TemporaryRedirect,HttpStatusCode.NoContent })
{
    using var bad = new HttpClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(status))));
    await Fail(()=>new ScreenshotShareClient(bad).UploadAsync(png,settings,default));
}
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await new ScreenshotShareClient(http).UploadAsync(png,settings,cancelled.Token); throw new Exception("Cancellation ignored"); }
catch(OperationCanceledException) { Require(sent==1,"Cancellation prevents upload"); }
foreach (var bad in new[] {
    new SharingSettings { AccountId="../evil" },
    new SharingSettings { AccountId=settings.AccountId,BucketName="../evil",AccessKeyId=settings.AccessKeyId,SecretAccessKey=settings.SecretAccessKey },
    new SharingSettings { AccountId=settings.AccountId,BucketName=settings.BucketName,AccessKeyId=settings.AccessKeyId,SecretAccessKey=settings.SecretAccessKey,PublicBaseUrl="https://example.com/?secret=x" } })
    await Fail(()=>new ScreenshotShareClient(http).UploadAsync(png,bad,default));
byte[] plain = Encoding.UTF8.GetBytes(settings.SecretAccessKey);
byte[] encrypted = SharingSettingsStore.Protect(plain,true);
Require(!encrypted.SequenceEqual(plain) && SharingSettingsStore.Protect(encrypted,false).SequenceEqual(plain),"Windows user encryption round trip");
Require(!settings.ToString().Contains(settings.SecretAccessKey),"Settings description hides secrets");
Require(new TypingSettings().DelaySeconds==3, "Default typing delay is three seconds");
Require(new TypingSettings().GetHotkeys().Count==6 && new TypingSettings().TranslateHotkey.Label=="Ctrl+Shift+T", "Six global actions with default translation shortcut");
var legacy = System.Text.Json.JsonSerializer.Deserialize<TypingSettings>("{\"Pace\":1.3,\"DelaySeconds\":4}")!;
Require(legacy.Pace==1.3 && legacy.DelaySeconds==4 && legacy.ScreenshotHotkey.Key==0x53,"Existing preferences gain new tool shortcuts without losing values");
await Fail(()=>Task.Run(() => (new TypingSettings { ShareHotkey=new TypingSettings().ScreenshotHotkey }).Validate()));
await Fail(()=>Task.Run(() => (new TypingSettings { TranslateHotkey=new TypingSettings().TextExtractHotkey }).Validate()));
await Fail(()=>Task.Run(() => (new TypingSettings { ShareHotkey=new(0x4006,0x7b,"Ctrl+Shift+F12") }).Validate()));
var registered = new Dictionary<int,HotkeyGesture>();
uint? blockedKey=null;
var registry=new GlobalHotkeySet((id,mods,key)=>
{
    if(key==blockedKey || registered.ContainsKey(id) || registered.Values.Any(h=>h.Modifiers==mods && h.Key==key)) return false;
    registered[id]=new(mods,key,""); return true;
}, id=>registered.Remove(id));
var firstBindings=new[] {new HotkeyRegistration(100,"Typing",new(0x4006,0x20,"Ctrl+Shift+Space")),new HotkeyRegistration(101,"Screenshot",new(0x4006,0x53,"Ctrl+Shift+S"))};
Require(registry.TryApply(firstBindings) && registered.Count==2,"Global registration applies all bindings");
blockedKey=0x49;
Require(!registry.TryApply([firstBindings[0],new(101,"Share",new(0x4006,0x49,"Ctrl+Shift+I"))]) && registry.FailedAction=="Share"
    && registered.Count==2 && registered[101].Key==0x53,"Failed hotkey registration restores previous shortcuts");
Require(registry.Release() && registered.Count==0,"Settings suspend all global shortcuts for recording");
var missing=registry.RegisterAvailable([firstBindings[0],new(101,"Share",new(0x4006,0x49,"Ctrl+Shift+I"))]);
Require(missing.SequenceEqual(new[]{"Share"}) && registry.IsRegistered(100) && !registry.IsRegistered(101),"Startup preserves available shortcuts and reports conflicts");
registry.Release();

string testDir = Path.Combine(Path.GetTempPath(), "AutoTyper-sharing-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDir);
string typingPath = Path.Combine(testDir,"typing.json"), historyPath = Path.Combine(testDir,"history.dat");
try
{
    var options = new TypingSettings { Pace=1.7, Rhythm=0.8, MistakeChance=0.03, DelaySeconds=4, MinimizeToTray=true, HotkeyModifiers=0x4003,HotkeyKey=0x51,HotkeyLabel="Ctrl+Alt+Q", TranslateHotkey=new(0x4003,0x54,"Ctrl+Alt+T") };
    TypingSettingsStore.Save(options,typingPath);
    Require(TypingSettingsStore.Load(typingPath)==options,"Typing settings persist across reloads");
    await Fail(()=>Task.Run(()=>TypingSettingsStore.Save(options with { DelaySeconds=double.NaN },typingPath)));
    File.WriteAllText(typingPath,"{\"HotkeyModifiers\":16390,\"HotkeyKey\":83,\"HotkeyLabel\":\"Ctrl+Shift+S\",\"Pace\":1.4}");
    var migrated=TypingSettingsStore.Load(typingPath);
    Require(migrated.Pace==1.4 && migrated.HotkeyKey==83 && migrated.ScreenshotHotkey.Modifiers==0x4007,"Legacy shortcut collisions migrate without losing typing preferences");
    string oldOptions = System.Text.Json.JsonSerializer.Serialize(options with { ShareHotkey = new(0x4006,0x54,"Ctrl+Shift+T") });
    var oldNode = System.Text.Json.Nodes.JsonNode.Parse(oldOptions)!.AsObject();
    oldNode.Remove("TranslateHotkey");
    File.WriteAllText(typingPath,oldNode.ToJsonString());
    var translatedOptions = TypingSettingsStore.Load(typingPath);
    Require(translatedOptions.Pace==options.Pace && translatedOptions.ShareHotkey.Label=="Ctrl+Shift+T"
        && translatedOptions.TranslateHotkey.Label=="Ctrl+Alt+Shift+T", "New translation shortcut avoids existing bindings without resetting preferences");
    TypingSettingsStore.Save(translatedOptions,typingPath);
    Require(TypingSettingsStore.Load(typingPath)==translatedOptions,"Migrated translation binding persists");
    File.WriteAllText(typingPath,"invalid-json");
    Require(TypingSettingsStore.Load(typingPath)==new TypingSettings(),"Corrupt typing settings recover to defaults");
    DateTimeOffset now = DateTimeOffset.UtcNow;
    var first = new Uri("https://example.com/share/"+new string('a',32)+".png");
    var second = new Uri("https://example.com/share/"+new string('b',32)+".png");
    ShareHistoryStore.Add(first,now.AddDays(-15),historyPath);
    ShareHistoryStore.Add(second,now,historyPath);
    var history=ShareHistoryStore.Load(historyPath);
    Require(history.Count==2 && history[0].Url==second.AbsoluteUri && history[1].Url==first.AbsoluteUri,"History persists newest first including older shares");
    Require(history[0].ExpiresAt==now.AddDays(14) && history[1].DateLabel.Contains("Expired"),"History uses 14-day retention and marks expired entries");
    Require(!Encoding.UTF8.GetString(File.ReadAllBytes(historyPath)).Contains(second.AbsoluteUri),"History URLs encrypted on disk");
    ShareHistoryStore.Add(second,now,historyPath);
    Require(ShareHistoryStore.Load(historyPath).Count==2,"History does not duplicate the same URL");
    var many = Enumerable.Range(0,205).Select(i=>new ShareHistoryEntry($"https://example.com/share/{i:x32}.png",now.AddMinutes(-i))).ToList();
    File.WriteAllBytes(historyPath,SharingSettingsStore.Protect(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(many),true));
    Require(ShareHistoryStore.Load(historyPath).Count==ShareHistoryStore.MaximumEntries,"History bounded to 200 most recent links");
    File.WriteAllBytes(historyPath,[1,2,3]);
    Require(ShareHistoryStore.Load(historyPath).Count==0,"Corrupt encrypted history does not prevent startup");
}
finally
{
    File.Delete(typingPath); File.Delete(historyPath); Directory.Delete(testDir);
}
var backup = new SettingsBundle(new TypingSettings { Pace=1.6, TranslateHotkey=new(0x4003,0x54,"Ctrl+Alt+T") },
    settings, new TranslationSettings { ApiKey="test-translator-key-123456", Region="eastus", TargetLanguage="Japanese" });
const string backupPassword = "test backup password";
byte[] backupData = SettingsBackup.Export(backup, backupPassword);
var restored = SettingsBackup.Import(backupData, backupPassword);
Require(restored.Typing==backup.Typing && restored.Translation==backup.Translation
    && restored.Sharing!.AccessKeyId==settings.AccessKeyId && restored.Sharing.SecretAccessKey==settings.SecretAccessKey
    && restored.Sharing.BucketName==settings.BucketName, "Portable backup round trip preserves all API keys, preferences and hotkeys");
Require(!Encoding.UTF8.GetString(backupData).Contains(settings.SecretAccessKey)
    && !Encoding.UTF8.GetString(backupData).Contains(backup.Translation.ApiKey), "Export does not expose API keys as plaintext");
Require(!backupData.SequenceEqual(SettingsBackup.Export(backup,backupPassword)), "Fresh export uses random salt and nonce");
await Fail(()=>Task.Run(()=>SettingsBackup.Import(backupData,"wrong password")));
await Fail(()=>Task.Run(()=>SettingsBackup.Export(backup,"short")));
await Fail(()=>Task.Run(()=>SettingsBackup.Import([1,2,3],backupPassword)));
await Fail(()=>Task.Run(()=>SettingsBackup.Import(new byte[SettingsBackup.MaximumFileBytes+1],backupPassword)));
var damaged = System.Text.Json.Nodes.JsonNode.Parse(Encoding.UTF8.GetString(backupData))!.AsObject();
byte[] cipher = Convert.FromBase64String(damaged["Ciphertext"]!.GetValue<string>());
cipher[0] ^= 1;
damaged["Ciphertext"] = Convert.ToBase64String(cipher);
await Fail(()=>Task.Run(()=>SettingsBackup.Import(Encoding.UTF8.GetBytes(damaged.ToJsonString()),backupPassword)));
var emptyBackup = new SettingsBundle(new TypingSettings(),null,new TranslationSettings());
Require(SettingsBackup.Import(SettingsBackup.Export(emptyBackup,backupPassword),backupPassword)==emptyBackup,
    "Unconfigured APIs round trip for clearing settings during import");
Console.WriteLine("All sharing, history, settings, hotkey and encrypted backup checks passed. No external uploads made.");
static void Require(bool value,string label) { if(!value) throw new Exception(label); Console.WriteLine("PASS: "+label); }
static async Task Fail(Func<Task> action) { try { await action(); } catch(InvalidOperationException) { Console.WriteLine("PASS: Invalid settings/capture or failed upload rejected"); return; } throw new Exception("Expected failure"); }
internal sealed class FakeHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token); }
