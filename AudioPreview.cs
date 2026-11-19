using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AutoTyper;

internal sealed record AudioDeviceChoice(string Id,string Name)
{
    public override string ToString()=>Name;
}

internal static class AudioDevices
{
    internal static List<AudioDeviceChoice> Get(DataFlow flow)
    {
        using var enumerator=new MMDeviceEnumerator();
        var result=new List<AudioDeviceChoice>();
        foreach(var device in enumerator.EnumerateAudioEndPoints(flow,DeviceState.Active))
            using(device) result.Add(new(device.ID,device.FriendlyName));
        return result;
    }
}

// A short, bounded microphone queue: slow playback drops stale frames instead of building up delay.
internal sealed class VoiceWaveProvider : IWaveProvider,IDisposable
{
    private readonly object _sync=new();
    private readonly float[] _ring=new float[4800],_input=new float[4096],_output=new float[4096];
    private readonly VoiceProcessor _processor=new();
    private int _head,_count;
    private AudioSettings _settings=new();
    private bool _original;
    private AudioSettings? _applied;
    private bool _appliedOriginal;
    internal float InputPeak,OutputPeak;
    internal int DroppedFrames;
    internal int LatencySamples=>_processor.LatencySamples;
    public WaveFormat WaveFormat { get; }=WaveFormat.CreateIeeeFloatWaveFormat(48000,1);
    internal void Configure(AudioSettings settings,bool original) { Volatile.Write(ref _settings,settings); Volatile.Write(ref _original,original); }
    internal void Add(ReadOnlySpan<float> samples,bool silent)
    {
        float peak=0;
        lock(_sync)
        {
            foreach(float sample in samples)
            {
                float value=!silent && float.IsFinite(sample)?Math.Clamp(sample,-4,4):0;
                peak=Math.Max(peak,Math.Abs(value));
                if(_count==_ring.Length) { _head=(_head+1)%_ring.Length; _count--; Interlocked.Increment(ref DroppedFrames); }
                _ring[(_head+_count)%_ring.Length]=value; _count++;
            }
        }
        Volatile.Write(ref InputPeak,peak);
    }
    public int Read(Span<byte> buffer)
    {
        int bytes=buffer.Length-buffer.Length%4;
        Span<float> destination=MemoryMarshal.Cast<byte,float>(buffer[..bytes]);
        var settings=Volatile.Read(ref _settings); bool original=Volatile.Read(ref _original);
        if(settings!=_applied || original!=_appliedOriginal) { _processor.Configure(settings,original); _applied=settings; _appliedOriginal=original; }
        float peak=0;
        for(int offset=0;offset<destination.Length;)
        {
            int count=Math.Min(_input.Length,destination.Length-offset);
            Array.Clear(_input,0,count);
            lock(_sync)
            {
                int available=Math.Min(count,_count);
                for(int i=0;i<available;i++) { _input[i]=_ring[_head]; _head=(_head+1)%_ring.Length; }
                _count-=available;
            }
            _processor.Process(_input,_output,count);
            _output.AsSpan(0,count).CopyTo(destination[offset..]);
            for(int i=0;i<count;i++) peak=Math.Max(peak,Math.Abs(_output[i]));
            offset+=count;
        }
        Volatile.Write(ref OutputPeak,peak);
        return bytes;
    }
    public void Dispose()=>_processor.Dispose();
}

internal sealed class AudioPreview : IDisposable
{
    private readonly VoiceWaveProvider _provider=new();
    private MMDevice? _microphone,_headphones;
    private WasapiRecorder? _recorder;
    private WasapiPlayer? _player;
    private bool _stopping;
    private int _disposed;
    internal event Action? DeviceStopped;
    internal float InputPeak=>Volatile.Read(ref _provider.InputPeak);
    internal float OutputPeak=>Volatile.Read(ref _provider.OutputPeak);
    internal double DelayMilliseconds=>_provider.LatencySamples/48d+(_recorder?.LatencyMilliseconds??0)+(_player?.LatencyMilliseconds??0);
    internal AudioPreview(AudioSettings settings,bool original)
    {
        try
        {
            settings.Validate();
            using var devices=new MMDeviceEnumerator();
            _microphone=devices.GetDevice(settings.MicrophoneId);
            _headphones=devices.GetDevice(settings.OutputId);
            _provider.Configure(settings,original);
            _recorder=new WasapiRecorderBuilder().WithDevice(_microphone).WithFormat(_provider.WaveFormat)
                .WithBufferLength(20).WithMmcssThreadPriority("Pro Audio").Build();
            _recorder.DataAvailable+=(buffer,flags,_,_)=>
            {
                _provider.Add(MemoryMarshal.Cast<byte,float>(buffer),(flags&AudioClientBufferFlags.Silent)!=0);
            };
            _recorder.RecordingStopped+=(_,_)=> { if(!_stopping) DeviceStopped?.Invoke(); };
            _player=new WasapiPlayerBuilder().WithDevice(_headphones).WithSharedMode().WithLatency(30)
                .WithMmcssThreadPriority("Pro Audio").Build();
            _player.PlaybackStopped+=(_,_)=> { if(!_stopping) DeviceStopped?.Invoke(); };
            _player.Init(_provider);
        }
        catch { Dispose(); throw; }
    }
    internal void Start() { _recorder!.StartRecording(); _player!.Play(); }
    internal void Configure(AudioSettings settings,bool original)=>_provider.Configure(settings,original);
    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0) return;
        _stopping=true;
        try { _recorder?.Dispose(); }
        finally
        {
            try { _player?.Dispose(); }
            finally { _provider.Dispose(); _microphone?.Dispose(); _headphones?.Dispose(); }
        }
    }
}
