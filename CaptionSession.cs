using System.Text.Json;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AutoTyper;

internal sealed class CaptionSession : IAsyncDisposable
{
    private readonly IDisposable _model;
    private readonly List<CaptionTrack> _tracks=[];
    private int _disposed;
    internal event Action<string,string,bool>? Text;
    internal event Action<Exception>? Failed;
    internal CaptionSession(string modelPath,bool includeMicrophone,string microphoneId)
        :this(CaptionEngine.Fast,modelPath,includeMicrophone,microphoneId) { }
    internal CaptionSession(CaptionEngine engine,string modelPath,bool includeMicrophone,string microphoneId)
    {
        Func<Action<string,bool>,Action<Exception>,ICaptionRecognizer> recognizer;
        if(engine==CaptionEngine.Accurate)
        {
            WhisperCaptionEngine whisper;
            try { whisper=new WhisperCaptionEngine(modelPath); }
            catch(Exception e) { throw new InvalidOperationException("Could not load the accurate caption model or speech engine. Retry, or switch captions to Fast.",e); }
            _model=whisper;recognizer=(emit,fail)=>new WhisperCaptionRecognizer(whisper,emit,fail);
        }
        else
        {
            CaptionSpeechRuntime.EnsureAvailable();
            Vosk.Model vosk;
            try { vosk=new Vosk.Model(modelPath); }
            catch(Exception e) { throw new InvalidOperationException("Could not load the local English model. Check its files and try again.",e); }
            _model=vosk;recognizer=(emit,_)=>new VoskCaptionRecognizer(vosk,emit);
        }
        try
        {
            using var devices=new MMDeviceEnumerator();
            _tracks.Add(new CaptionTrack(recognizer,devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia),"System",true));
            if(includeMicrophone)
                _tracks.Add(new CaptionTrack(recognizer,string.IsNullOrEmpty(microphoneId)
                    ?devices.GetDefaultAudioEndpoint(DataFlow.Capture,Role.Communications):devices.GetDevice(microphoneId),"Mic",false));
            foreach(var track in _tracks)
            {
                track.Text+=(source,text,final)=>Text?.Invoke(source,text,final);
                track.Failed+=e=>Failed?.Invoke(e);
            }
        }
        catch { foreach(var track in _tracks) track.DisposeAsync().AsTask().GetAwaiter().GetResult(); _model.Dispose();throw; }
    }
    internal Task StartAsync()=>Task.WhenAll(_tracks.Select(track=>track.StartAsync()));
    internal Task FlushAsync()=>Task.WhenAll(_tracks.Select(track=>track.FlushAsync()));
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0) return;
        try { await Task.WhenAll(_tracks.Select(track=>track.DisposeAsync().AsTask())); }
        finally { _model.Dispose(); }
    }
}

internal sealed class CaptionTrack : IAsyncDisposable
{
    private sealed record Packet(byte[]? Data,TaskCompletionSource<bool>? Flush=null);
    private readonly Channel<Packet> _queue=Channel.CreateBounded<Packet>(new BoundedChannelOptions(256)
        { SingleReader=true,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait });
    private readonly MMDevice _device;
    private readonly WasapiRecorder _recorder;
    private readonly Func<Action<string,bool>,Action<Exception>,ICaptionRecognizer> _createRecognizer;
    private readonly string _source;
    private readonly Task _worker;
    private readonly TaskCompletionSource<bool> _ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _captureSync=new();
    private readonly byte[] _captureBuffer=new byte[3200]; // 100 ms at mono PCM16 / 16 kHz
    private int _captureCount;
    private int _disposed,_failed;
    internal event Action<string,string,bool>? Text;
    internal event Action<Exception>? Failed;
    internal CaptionTrack(Func<Action<string,bool>,Action<Exception>,ICaptionRecognizer> createRecognizer,MMDevice device,string source,bool loopback)
    {
        _createRecognizer=createRecognizer;_device=device;_source=source;
        try
        {
            var builder=new WasapiRecorderBuilder().WithDevice(device).WithFormat(new WaveFormat(16000,16,1))
                .WithBufferLength(50).WithMmcssThreadPriority("Audio");
            if(loopback) builder.WithLoopbackCapture();
            _recorder=builder.Build();
            _recorder.DataAvailable+=(data,flags,_,_)=>
            {
                if(Volatile.Read(ref _disposed)!=0) return;
                lock(_captureSync)
                {
                    while(!data.IsEmpty)
                    {
                        int count=Math.Min(_captureBuffer.Length-_captureCount,data.Length);
                        if((flags&AudioClientBufferFlags.Silent)!=0) _captureBuffer.AsSpan(_captureCount,count).Clear();
                        else data[..count].CopyTo(_captureBuffer.AsSpan(_captureCount,count));
                        _captureCount+=count;data=data[count..];
                        if(_captureCount==_captureBuffer.Length) EnqueueTail();
                    }
                }
            };
            _recorder.RecordingStopped+=(_,e)=> { if(Volatile.Read(ref _disposed)==0) Fail(e.Exception??new InvalidOperationException("The caption audio device stopped. Check its connection.")); };
            _worker=Task.Run(ProcessAsync);
        }
        catch { device.Dispose();throw; }
    }
    internal async Task StartAsync()
    {
        try { await _ready.Task; }
        catch(Exception e) { throw new InvalidOperationException("The local speech recognizer could not initialize. Check the English model and try again.",e); }
        if(Volatile.Read(ref _failed)!=0) throw new InvalidOperationException("The speech engine could not start. Check the English model files.");
        _recorder.StartRecording();
    }
    private void EnqueueTail()
    {
        if(_captureCount==0) return;
        byte[] bytes=_captureBuffer.AsSpan(0,_captureCount).ToArray();_captureCount=0;
        if(!_queue.Writer.TryWrite(new(bytes))) Fail(new InvalidOperationException("Caption processing fell behind. Stop and restart captions."));
    }
    private void Fail(Exception e)
    {
        if(Interlocked.Exchange(ref _failed,1)!=0) return;
        _queue.Writer.TryComplete(e);Failed?.Invoke(e);
    }
    internal async Task FlushAsync()
    {
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(_captureSync)
        {
            EnqueueTail();
            if(!_queue.Writer.TryWrite(new(null,completion))) throw new InvalidOperationException("Caption processing is busy. Try translating again shortly.");
        }
        await Task.WhenAny(completion.Task,_worker);
        if(!completion.Task.IsCompleted) throw new InvalidOperationException("Caption capture stopped.");
        await completion.Task;
    }
    private async Task ProcessAsync()
    {
        ICaptionRecognizer? recognizer=null;
        try
        {
            recognizer=_createRecognizer((text,final)=>Text?.Invoke(_source,text,final),Fail);
            _ready.TrySetResult(true);
            await foreach(var packet in _queue.Reader.ReadAllAsync())
            {
                if(packet.Flush is { } flush)
                {
                    try { await recognizer.FlushAsync();flush.TrySetResult(true); }
                    catch(Exception e) { flush.TrySetException(e);throw; }
                    continue;
                }
                recognizer.Accept(packet.Data!);
            }
            await recognizer.CompleteAsync();
        }
        catch(Exception e) { _ready.TrySetException(e);Fail(e); }
        finally
        {
            recognizer?.Dispose();
            while(_queue.Reader.TryRead(out var packet)) packet.Flush?.TrySetException(new InvalidOperationException("Caption capture stopped."));
        }
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0) return;
        try { await _recorder.DisposeAsync(); }
        finally
        {
            lock(_captureSync) EnqueueTail();
            _queue.Writer.TryComplete();await _worker;_device.Dispose();
        }
    }
}

internal sealed class VoskCaptionRecognizer : ICaptionRecognizer
{
    private readonly Vosk.Model _model;
    private readonly Action<string,bool> _emit;
    private Vosk.VoskRecognizer _recognizer;
    private long _samples;
    private string _partial="";
    internal VoskCaptionRecognizer(Vosk.Model model,Action<string,bool> emit)
    {
        _model=model;_emit=emit;_recognizer=new Vosk.VoskRecognizer(model,16000f);
    }
    public void Accept(byte[] data)
    {
        _samples+=data.Length/2;
        if(_recognizer.AcceptWaveform(data,data.Length)) { Publish(_recognizer.Result());_samples=0;_partial=""; }
        else
        {
            using var result=JsonDocument.Parse(_recognizer.PartialResult());
            string text=result.RootElement.GetProperty("partial").GetString()??"";
            if(_partial!=text) { _partial=text;_emit(text,false); }
        }
        // Bound unfinished utterances, even when a continuous stream has no silence.
        if(_samples>=16000*15) FinalizeSegment();
    }
    public Task FlushAsync() { FinalizeSegment();return Task.CompletedTask; }
    public Task CompleteAsync() { Publish(_recognizer.FinalResult());return Task.CompletedTask; }
    private void FinalizeSegment()
    {
        Publish(_recognizer.FinalResult());
        _recognizer.Dispose();_recognizer=new Vosk.VoskRecognizer(_model,16000f);
        _samples=0;_partial="";
    }
    private void Publish(string json)
    {
        using var result=JsonDocument.Parse(json);
        _emit(result.RootElement.GetProperty("text").GetString()??"",true);
    }
    public void Dispose()=>_recognizer.Dispose();
}
