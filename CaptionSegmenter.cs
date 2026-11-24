namespace AutoTyper;

// Splits a 16 kHz mono PCM16 stream into speech phrases at natural pauses, so Whisper
// always sees whole phrases instead of fixed windows that cut words in half.
internal sealed class CaptionSegmenter
{
    internal const int SampleRate=16000;
    internal const int FrameSamples=480;                 // 30 ms
    private const int OnsetFrames=3;                     // 90 ms of voice starts a phrase
    private const int PreRollFrames=10;                  // keep 300 ms before the onset
    private const int HangoverFrames=22;                 // 660 ms of quiet ends a phrase
    private const int TailFrames=8;                      // keep 240 ms after the last voice
    private const int MinimumVoicedFrames=8;             // ignore clicks shorter than 240 ms
    internal const int MaximumSamples=SampleRate*20;     // longer phrases are split...
    private const int SplitSearchFrames=100;             // ...at the quietest point of the last 3 s
    private const float AbsoluteThreshold=0.004f;        // about -48 dBFS RMS
    private const float NoiseRatio=3.2f;                 // about +10 dB over the noise floor

    private readonly float[] _frame=new float[FrameSamples];
    private int _frameCount;
    private readonly Queue<(float[] Samples,float Energy)> _preRoll=new();
    private readonly List<float> _chunk=[];
    private readonly List<float> _energy=[];
    private int _onset,_silent,_voiced;
    private float _floor=-1;
    internal bool Active { get; private set; }
    internal int Generation { get; private set; }
    internal int ChunkSamples=>_chunk.Count;

    internal void Accept(ReadOnlySpan<byte> pcm16,List<float[]> completed)
    {
        for(int i=0;i+1<pcm16.Length;i+=2)
        {
            _frame[_frameCount++]=(short)(pcm16[i]|pcm16[i+1]<<8)/32768f;
            if(_frameCount==FrameSamples) { _frameCount=0;AcceptFrame(completed); }
        }
    }
    internal float[]? Snapshot()=>Active&&_voiced>=MinimumVoicedFrames?[.._chunk]:null;
    // Finalize the current phrase immediately (for example before a translation request).
    internal float[]? Cut()
    {
        if(!Active) return null;
        var result=_voiced>=MinimumVoicedFrames?_chunk.ToArray():null;
        Reset();
        return result;
    }

    private void AcceptFrame(List<float[]> completed)
    {
        double sum=0;
        foreach(float value in _frame) sum+=value*value;
        float rms=(float)Math.Sqrt(sum/FrameSamples);
        if(_floor<0||rms<_floor) _floor=rms;
        bool voice=rms>Math.Max(AbsoluteThreshold,_floor*NoiseRatio);
        if(!voice) _floor=_floor*0.97f+rms*0.03f;
        else _floor=Math.Min(_floor*1.0005f+1e-6f,rms); // let a rising noise floor catch up slowly

        if(!Active)
        {
            _preRoll.Enqueue(([.._frame],rms));
            if(_preRoll.Count>PreRollFrames) _preRoll.Dequeue();
            _onset=voice?_onset+1:0;
            if(_onset<OnsetFrames) return;
            Active=true;Generation++;_silent=0;_voiced=_onset;_onset=0;
            foreach(var (samples,energy) in _preRoll) { _chunk.AddRange(samples);_energy.Add(energy); }
            _preRoll.Clear();
            return;
        }
        _chunk.AddRange(_frame);_energy.Add(rms);
        if(voice) { _voiced++;_silent=0; } else _silent++;
        if(_silent>=HangoverFrames)
        {
            int keep=_energy.Count-_silent+TailFrames;
            if(_voiced>=MinimumVoicedFrames) completed.Add(_chunk.GetRange(0,keep*FrameSamples).ToArray());
            Reset();
            return;
        }
        if(_chunk.Count>=MaximumSamples)
        {
            int start=Math.Max(0,_energy.Count-SplitSearchFrames),split=start;
            for(int i=start;i<_energy.Count;i++) if(_energy[i]<_energy[split]) split=i;
            if(split==0) split=_energy.Count-1;
            completed.Add(_chunk.GetRange(0,(split+1)*FrameSamples).ToArray());
            _chunk.RemoveRange(0,(split+1)*FrameSamples);_energy.RemoveRange(0,split+1);
            _voiced=_energy.Count;Generation++;
        }
    }
    private void Reset()
    {
        _chunk.Clear();_energy.Clear();_preRoll.Clear();
        Active=false;_silent=_voiced=_onset=0;
    }
}
