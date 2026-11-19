using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace AutoTyper;

internal static class CaptionModel
{
    internal const string Name="vosk-model-small-en-us-0.15";
    internal static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AutoTyper","Speech");
    internal static string ModelPath=>Path.Combine(Root,Name);
    internal static bool IsReady=>File.Exists(Path.Combine(ModelPath,"am","final.mdl"))
        &&File.Exists(Path.Combine(ModelPath,"conf","model.conf"));
    private static readonly SemaphoreSlim DownloadLock=new(1,1);
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
                    progress.Report($"Downloading English captions: {total/1_000_000d:0.0} MB…");
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
