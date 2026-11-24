using System.Text.RegularExpressions;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace AutoTyper;

internal interface ICaptionRecognizer : IDisposable
{
    void Accept(byte[] pcm16);
    // Finalize the current phrase and wait until its text has been published.
    Task FlushAsync();
    // End of stream: publish everything still buffered.
    Task CompleteAsync();
}

// Shared by every caption source in a session: one loaded model, one decode at a time
// (so System and Mic do not fight over CPU cores), and one context-free processor for previews.
internal sealed class WhisperCaptionEngine : IDisposable
{
    private readonly WhisperFactory _factory;
    internal SemaphoreSlim Gate { get; }=new(1,1);
    internal WhisperProcessor Preview { get; }
    // Decodes are serialized by Gate, so one decode may use most of the CPU.
    internal static int Threads=>Math.Clamp(Environment.ProcessorCount-1,1,8);
    internal static void ConfigureRuntime()
    {
        // Only the CPU runtime ships with the app; skip probing for CUDA/Vulkan builds.
        RuntimeOptions.RuntimeLibraryOrder=[RuntimeLibrary.Cpu,RuntimeLibrary.CpuNoAvx];
    }
    internal WhisperCaptionEngine(string modelPath)
    {
        ConfigureRuntime();
        _factory=WhisperFactory.FromPath(modelPath,new WhisperFactoryOptions { UseGpu=false });
        try { Preview=Builder().WithNoContext().Build(); }
        catch { _factory.Dispose();throw; }
    }
    internal WhisperProcessorBuilder Builder()=>_factory.CreateBuilder()
        .WithLanguage("en").WithThreads(Threads).WithMaxLastTextTokens(64);
    public void Dispose()
    {
        Preview.Dispose();_factory.Dispose();Gate.Dispose();
    }
}

internal sealed partial class WhisperCaptionRecognizer : ICaptionRecognizer
{
    private const int MinimumSamples=CaptionSegmenter.SampleRate*6/5;    // whisper.cpp ignores input under 1 s
    private const int MergeLimit=CaptionSegmenter.SampleRate*28;         // one 30 s Whisper window
    private const int PreviewStep=CaptionSegmenter.SampleRate;           // refresh previews every 1 s of new speech
    private static readonly TimeSpan PreviewLimit=TimeSpan.FromSeconds(4);  // CPUs this slow skip previews entirely
    internal const string Listening="…";
    private readonly WhisperCaptionEngine _engine;
    private readonly WhisperProcessor _final;
    private readonly Action<string,bool> _emit;
    private readonly Action<Exception> _fail;
    private readonly CaptionSegmenter _segmenter=new();
    private readonly List<float[]> _completed=[];
    private readonly Queue<float[]> _ready=new();
    private readonly List<(long Target,TaskCompletionSource<bool> Done)> _waiters=[];
    private readonly SemaphoreSlim _signal=new(0);
    private readonly CancellationTokenSource _stop=new();
    private readonly object _sync=new();
    private readonly Task _worker;
    private long _queued,_published;
    private int _previewGeneration=-1,_previewSamples;
    private bool _completing,_showingPartial;
    private TimeSpan _lastDecode;

    internal WhisperCaptionRecognizer(WhisperCaptionEngine engine,Action<string,bool> emit,Action<Exception> fail)
    {
        _engine=engine;_emit=emit;_fail=fail;
        _final=engine.Builder().Build();
        _worker=Task.Run(RunAsync);
    }
    public void Accept(byte[] pcm16)
    {
        // Emit under the lock so "listening" and final text from the decoder thread stay ordered.
        lock(_sync)
        {
            _segmenter.Accept(pcm16,_completed);
            foreach(var chunk in _completed) { _ready.Enqueue(chunk);_queued++; }
            _completed.Clear();
            if(_segmenter.Active&&!_showingPartial) { _showingPartial=true;_emit(Listening,false); }
            ClearIdlePartial();
        }
        if(_signal.CurrentCount==0) _signal.Release();
    }
    public Task FlushAsync()
    {
        var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(_sync)
        {
            if(_segmenter.Cut() is { } chunk) { _ready.Enqueue(chunk);_queued++; }
            ClearIdlePartial();
            if(_published>=_queued) done.TrySetResult(true);
            else _waiters.Add((_queued,done));
        }
        _signal.Release();
        return done.Task;
    }
    // A blip too short to transcribe leaves nothing to replace the "…" marker, so clear it.
    private void ClearIdlePartial()
    {
        if(_showingPartial&&!_segmenter.Active&&_published==_queued) { _showingPartial=false;_emit("",false); }
    }
    // Stopping (closing the window or switching mode) must be quick: drop audio not yet decoded
    // and cancel the decode in flight instead of waiting for a backlog to drain.
    public async Task CompleteAsync()
    {
        lock(_sync)
        {
            _completing=true;_ready.Clear();_segmenter.Cut();
            _published=_queued;
            foreach(var (_,done) in _waiters) done.TrySetResult(true);
            _waiters.Clear();
            if(_showingPartial) { _showingPartial=false;_emit("",false); }
        }
        _stop.Cancel();_signal.Release();
        try { await _worker; } catch(OperationCanceledException) { }
    }
    private async Task RunAsync()
    {
        try
        {
            while(!_stop.IsCancellationRequested)
            {
                await _signal.WaitAsync(_stop.Token);
                while(true)
                {
                    float[]? final=null,preview=null;int count=0,generation=0;
                    lock(_sync)
                    {
                        if(_ready.Count>0)
                        {
                            // Catch up after a slow decode by sending everything waiting as one window.
                            var parts=new List<float[]>{_ready.Dequeue()};
                            int length=parts[0].Length;
                            while(_ready.Count>0&&length+_ready.Peek().Length+3200<=MergeLimit)
                            {
                                var next=_ready.Dequeue();length+=next.Length+3200;parts.Add(next);
                            }
                            count=parts.Count;final=Join(parts);
                        }
                        else if(!_completing&&_lastDecode<=PreviewLimit&&_segmenter.ChunkSamples>=PreviewStep
                            &&(_segmenter.Generation!=_previewGeneration||_segmenter.ChunkSamples-_previewSamples>=PreviewInterval)
                            &&_segmenter.Snapshot() is { } snapshot)
                        {
                            preview=snapshot;generation=_segmenter.Generation;
                            _previewGeneration=generation;_previewSamples=snapshot.Length;
                        }
                    }
                    if(final is not null)
                    {
                        string text=await TranscribeAsync(_final,final);
                        lock(_sync)
                        {
                            if(_completing) break;
                            _published+=count;_showingPartial=_segmenter.Active;
                            _previewGeneration=-1;_previewSamples=0;
                            _emit(text,true);
                            if(_showingPartial) _emit(Listening,false);
                            _waiters.RemoveAll(w=> { if(w.Target>_published) return false; w.Done.TrySetResult(true);return true; });
                        }
                        continue;
                    }
                    if(preview is not null)
                    {
                        string text=await TranscribeAsync(_engine.Preview,preview);
                        lock(_sync)
                            if(text.Length>0&&_ready.Count==0&&_segmenter.Active&&_segmenter.Generation==generation)
                                _emit(text+" "+Listening,false);
                        continue;
                    }
                    break;
                }
            }
        }
        catch(OperationCanceledException) when(_stop.IsCancellationRequested) { }
        catch(Exception e) { _fail(e); }
        finally
        {
            lock(_sync)
            {
                foreach(var (_,done) in _waiters) done.TrySetResult(true);
                _waiters.Clear();
            }
        }
    }
    // Refresh previews no faster than every 1 s of speech, and slower on slow CPUs so previews
    // never take more than about half the decoder's time.
    private int PreviewInterval=>Math.Max(PreviewStep,(int)(_lastDecode.TotalSeconds*2*CaptionSegmenter.SampleRate));
    private async Task<string> TranscribeAsync(WhisperProcessor processor,float[] samples)
    {
        if(samples.Length<MinimumSamples) Array.Resize(ref samples,MinimumSamples);
        var parts=new List<string>();
        await _engine.Gate.WaitAsync(_stop.Token);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        try { await foreach(var segment in processor.ProcessAsync(samples,_stop.Token)) parts.Add(segment.Text); }
        finally { _engine.Gate.Release(); }
        _lastDecode=watch.Elapsed;
        return Clean(string.Join(" ",parts));
    }
    private static float[] Join(List<float[]> parts)
    {
        if(parts.Count==1) return parts[0];
        var joined=new List<float>();
        foreach(var part in parts)
        {
            if(joined.Count>0) joined.AddRange(new float[3200]); // 200 ms gap between phrases
            joined.AddRange(part);
        }
        return [..joined];
    }
    // Whisper describes non-speech audio in brackets ("[Music]", "(laughs)") and, on near-silent
    // input, sometimes produces stock video-outro phrases. Neither belongs in live captions.
    internal static string Clean(string text)
    {
        text=Annotations().Replace(text," ");
        text=Spaces().Replace(text," ").Trim();
        string bare=Punctuation().Replace(text,"").ToLowerInvariant();
        if(bare.Length==0||Hallucinations.Contains(bare)||bare.Contains("amaraorg")||bare.Contains("subtitles by")) return "";
        return text;
    }
    private static readonly HashSet<string> Hallucinations=["you","thank you for watching","thanks for watching",
        "please subscribe","like and subscribe","thank you for watching please subscribe"];
    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*|[♪♫]+")] private static partial Regex Annotations();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"[^\p{L}\p{N}\s]")] private static partial Regex Punctuation();
    public void Dispose()
    {
        _stop.Cancel();_signal.Release();
        try { _worker.Wait(TimeSpan.FromSeconds(10)); } catch(AggregateException) { }
        // A decode that is still running owns these; leave them to the finalizer rather than crash it.
        if(_worker.IsCompleted) { _final.Dispose();_signal.Dispose();_stop.Dispose(); }
    }
}
