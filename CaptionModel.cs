using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace AutoTyper;

internal enum CaptionEngine { Accurate, Fast }

internal static class CaptionEngineChoice
{
    internal static readonly string[] Labels=["Accurate","Fast"];
    private static string SettingsPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AutoTyper","captions.json");
    private sealed record Saved(string Engine);
    internal static CaptionEngine Load()
    {
        try
        {
            if(!File.Exists(SettingsPath)) return CaptionEngine.Accurate;
            var saved=JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsPath));
            return Enum.TryParse(saved?.Engine,out CaptionEngine engine)&&Enum.IsDefined(engine)?engine:CaptionEngine.Accurate;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { return CaptionEngine.Accurate; }
    }
    internal static void Save(CaptionEngine engine)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath,JsonSerializer.Serialize(new Saved(engine.ToString())));
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
    }
}

internal static class CaptionModel
{
    // Fast: Vosk small English (streaming, word-by-word, no punctuation).
    internal const string Name="vosk-model-small-en-us-0.15";
    // Accurate: Whisper small.en (punctuated phrases), quantized to keep the download and memory moderate.
    internal const string WhisperName="ggml-small.en-q5_1.bin";
    private const long WhisperSize=190_098_681;
    private const string WhisperSha256="BFDFF4894DCB76BBF647D56263EA2A96645423F1669176F4844A1BF8E478AD30";
    private const string WhisperUrl="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/"+WhisperName;
    internal static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AutoTyper","Speech");
    internal static string ModelPath=>Path.Combine(Root,Name);
    internal static string WhisperPath=>Path.Combine(Root,WhisperName);
    internal static string PathFor(CaptionEngine engine)=>engine==CaptionEngine.Accurate?WhisperPath:ModelPath;
    internal static bool IsReady=>File.Exists(Path.Combine(ModelPath,"am","final.mdl"))
        &&File.Exists(Path.Combine(ModelPath,"conf","model.conf"));
    internal static bool IsWhisperReady=>File.Exists(WhisperPath)&&new FileInfo(WhisperPath).Length==WhisperSize;
    private static readonly SemaphoreSlim DownloadLock=new(1,1);
    internal static Task EnsureAsync(CaptionEngine engine,IProgress<string> progress,CancellationToken token)=>
        engine==CaptionEngine.Accurate?EnsureWhisperAsync(progress,token):EnsureAsync(progress,token);
    internal static async Task EnsureWhisperAsync(IProgress<string> progress,CancellationToken token)
    {
        await DownloadLock.WaitAsync(token);
        string? partial=null;
        try
        {
            if(IsWhisperReady) return;
            Directory.CreateDirectory(Root);
            partial=Path.Combine(Root,Guid.NewGuid().ToString("N")+".part");
            using var client=new HttpClient { Timeout=TimeSpan.FromMinutes(30) };
            using var response=await client.GetAsync(WhisperUrl,HttpCompletionOption.ResponseHeadersRead,token);
            response.EnsureSuccessStatusCode();
            await using(var input=await response.Content.ReadAsStreamAsync(token))
            await using(var output=File.Create(partial))
            {
                byte[] buffer=new byte[131072]; long total=0; int count;
                while((count=await input.ReadAsync(buffer,token))>0)
                {
                    total+=count;
                    if(total>WhisperSize) throw new IOException("The accurate caption model download was larger than expected.");
                    await output.WriteAsync(buffer.AsMemory(0,count),token);
                    progress.Report($"Downloading accurate captions: {total/1_000_000d:0} of {WhisperSize/1_000_000d:0} MB…");
                }
                if(total!=WhisperSize) throw new IOException("The accurate caption model download was incomplete. Try again.");
            }
            progress.Report("Verifying accurate caption model…");
            // The official model file is pinned; an incomplete or changed download is rejected.
            await using(var file=File.OpenRead(partial))
            {
                string digest=Convert.ToHexString(await SHA256.HashDataAsync(file,token));
                if(!digest.Equals(WhisperSha256,StringComparison.OrdinalIgnoreCase)) throw new IOException("Accurate caption model verification failed. Try downloading again.");
            }
            File.Move(partial,WhisperPath,true);partial=null;
        }
        finally
        {
            try { if(partial is not null&&File.Exists(partial)) File.Delete(partial); }
            catch(IOException) { }
            DownloadLock.Release();
        }
    }
    internal static async Task EnsureAsync(IProgress<string> progress,CancellationToken token)
    {
        await DownloadLock.WaitAsync(token);
        string? staging=null,zip=null;
        try
        {
            if(IsReady) return;
            Directory.CreateDirectory(Root);
            string id=Guid.NewGuid().ToString("N");
            zip=Path.Combine(Root,id+".zip"); staging=Path.Combine(Root,id);
            using var client=new HttpClient { Timeout=TimeSpan.FromMinutes(10) };
            using var response=await client.GetAsync("https://alphacephei.com/vosk/models/"+Name+".zip",HttpCompletionOption.ResponseHeadersRead,token);
            response.EnsureSuccessStatusCode();
            await using(var input=await response.Content.ReadAsStreamAsync(token))
            await using(var output=File.Create(zip))
            {
                byte[] buffer=new byte[65536]; long total=0; int count;
                while((count=await input.ReadAsync(buffer,token))>0)
                {
                    total+=count;
                    if(total>60_000_000) throw new IOException("The English model download was larger than expected.");
                    await output.WriteAsync(buffer.AsMemory(0,count),token);
                    progress.Report($"Downloading fast captions: {total/1_000_000d:0.0} MB…");
                }
            }
            progress.Report("Verifying English model…");
            // The official model archive is pinned; an incomplete or changed download is rejected.
            await using(var file=File.OpenRead(zip))
            {
                string digest=Convert.ToHexString(await SHA256.HashDataAsync(file,token));
                if(!digest.Equals(ArchiveSha256,StringComparison.OrdinalIgnoreCase)) throw new IOException("English model verification failed. Try downloading again.");
            }
            await Task.Run(()=>
            {
                using var archive=ZipFile.OpenRead(zip);
                long expanded=0;
                foreach(var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested(); expanded+=entry.Length;
                    if(expanded>150_000_000) throw new IOException("Invalid English model archive.");
                }
                ZipFile.ExtractToDirectory(zip,staging);
                token.ThrowIfCancellationRequested();
                if(Directory.Exists(ModelPath)) Directory.Delete(ModelPath,true);
                Directory.Move(Path.Combine(staging,Name),ModelPath);
            },token);
            if(!IsReady) throw new IOException("English model files are incomplete.");
        }
        finally
        {
            try { if(zip is not null&&File.Exists(zip)) File.Delete(zip); if(staging is not null&&Directory.Exists(staging)) Directory.Delete(staging,true); }
            catch(IOException) { }
            DownloadLock.Release();
        }
    }
    private const string ArchiveSha256="30F26242C4EB449F948E42CB302DD7A686CB29A3423A8367F99FF41780942498";
}
