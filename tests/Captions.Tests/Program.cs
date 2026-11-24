using AutoTyper;
using System.Text.Json;
using NAudio.Wave;
int checks=0;
void Require(bool condition,string label) { if(!condition) throw new Exception(label);Console.WriteLine("PASS: "+label);checks++; }
var transcript=new CaptionTranscript();
Require(transcript.Snapshot() is null,"Empty captions produce no translation request");
transcript.Add("System","hello there");transcript.Add("Mic","my reply");
var first=transcript.Snapshot()!;
Require(first.Text=="[System] hello there"+Environment.NewLine+"[Mic] my reply","Translation retains the source of each segment");
transcript.Add("System","new speech while request is running");
Require(transcript.Snapshot()!.Text.StartsWith(first.Text),"Failed or unacknowledged requests retain their text");
transcript.Acknowledge(first);
Require(transcript.PendingCount==1&&transcript.Snapshot()!.Text=="[System] new speech while request is running","Success advances only through the captured snapshot");
var second=transcript.Snapshot()!;transcript.Acknowledge(first);
Require(transcript.Snapshot()==second,"Repeated acknowledgement cannot discard newer captions");
transcript.Acknowledge(second);
Require(transcript.Snapshot() is null,"Repeated translate does not resend previously translated captions");
transcript.Add("Mic","   ");Require(transcript.PendingCount==0,"Empty recognition results are ignored");
transcript.Add("System","first");transcript.Add("Mic","second");
var limited=transcript.Snapshot(16)!;
Require(limited.Count==1&&limited.Text=="[System] first","Requests stop at complete segment boundaries within the character limit");
transcript.Acknowledge(limited);Require(transcript.Snapshot()!.Text=="[Mic] second","Text beyond the request limit is kept for the next translation");
byte[] Tone(double seconds,double amplitude)
{
    var bytes=new byte[(int)(seconds*16000)*2];
    for(int i=0;i<bytes.Length/2;i++)
    {
        short value=(short)(amplitude*32767*Math.Sin(2*Math.PI*220*i/16000));
        bytes[i*2]=(byte)value;bytes[i*2+1]=(byte)(value>>8);
    }
    return bytes;
}
var chunks=new List<float[]>();var segmenter=new CaptionSegmenter();
segmenter.Accept(Tone(1,0),chunks);
Require(chunks.Count==0&&!segmenter.Active,"Silence does not start a caption phrase");
segmenter.Accept(Tone(1.5,0.2),chunks);
Require(segmenter.Active&&chunks.Count==0,"Speech-level audio starts a phrase and waits for a pause");
segmenter.Accept(Tone(1,0),chunks);
Require(chunks.Count==1&&!segmenter.Active,"A pause ends the phrase");
Require(chunks[0].Length>=16000*1.5&&chunks[0].Length<=16000*2.2,"Phrases keep short lead-in and tail audio around speech");
chunks.Clear();segmenter.Accept(Tone(0.1,0.2),chunks);segmenter.Accept(Tone(1,0),chunks);
Require(chunks.Count==0,"Clicks shorter than a syllable are ignored");
segmenter.Accept(Tone(25,0.2),chunks);
Require(chunks.Count==1&&chunks[0].Length<=CaptionSegmenter.MaximumSamples&&segmenter.Active,"Continuous speech is split into bounded phrases without losing the remainder");
segmenter.Accept(Tone(0.5,0.2),chunks);
Require(segmenter.Cut() is { Length: >0 }&&!segmenter.Active,"Translate can cut an in-progress phrase immediately");
Require(WhisperCaptionRecognizer.Clean(" [Music] Hello there. (laughs) ♪ ")=="Hello there.","Non-speech annotations are removed from accurate captions");
Require(WhisperCaptionRecognizer.Clean("Thanks for watching!")==""&&WhisperCaptionRecognizer.Clean("Thank you.")=="Thank you.","Stock silence hallucinations are dropped while real short replies are kept");
CaptionSpeechRuntime.EnsureAvailable();
Require(true,"Pinned x64 speech engine and native dependencies load without a model download");
CaptionSpeechRuntime.EnsureWhisperAvailable();
Require(true,"Accurate (Whisper) CPU runtime loads without a model download");
if(args.Length>=2&&args[0].EndsWith(".bin",StringComparison.OrdinalIgnoreCase))
{
    // Accurate mode: feed a real recording through the segmenter and Whisper recognizer.
    using var engine=new WhisperCaptionEngine(args[0]);
    using var reader=new WaveFileReader(args[1]);
    Require(reader.WaveFormat.SampleRate==16000&&reader.WaveFormat.BitsPerSample==16&&reader.WaveFormat.Channels==1,"Real speech test uses mono 16 kHz PCM");
    var finals=new List<string>();Exception? failure=null;
    var recognizer=new WhisperCaptionRecognizer(engine,(text,final)=> { if(final&&text.Length>0) lock(finals) finals.Add(text); },e=>failure=e);
    var watch=System.Diagnostics.Stopwatch.StartNew();
    byte[] buffer=new byte[3200];int count;
    while((count=reader.Read(buffer,0,buffer.Length))>0) recognizer.Accept(buffer[..count]);
    await recognizer.CompleteAsync();recognizer.Dispose();
    if(failure is not null) throw failure;
    string text=string.Join(" ",finals);
    Console.WriteLine("RECOGNIZED: "+text);
    Require(text.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length>=8,"Accurate engine recognizes words from a real speech recording");
    Console.WriteLine($"Accurate recognition: {reader.TotalTime.TotalSeconds:0.0}s audio in {watch.Elapsed.TotalSeconds:0.00}s");
}
else if(args.Length>=2)
{
    using var model=new Vosk.Model(args[0]);
    using var recognizer=new Vosk.VoskRecognizer(model,16000f);
    using var reader=new WaveFileReader(args[1]);
    Require(reader.WaveFormat.SampleRate==16000&&reader.WaveFormat.BitsPerSample==16&&reader.WaveFormat.Channels==1,"Real speech test uses mono 16 kHz PCM");
    byte[] buffer=new byte[1600];int count;var words=new List<string>();
    var watch=System.Diagnostics.Stopwatch.StartNew();
    while((count=reader.Read(buffer,0,buffer.Length))>0)
        if(recognizer.AcceptWaveform(buffer,count)) AddResult(recognizer.Result());
    AddResult(recognizer.FinalResult());
    string text=string.Join(" ",words);
    Console.WriteLine("RECOGNIZED: "+text);
    Require(text.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length>=8,"Local engine recognizes words from a real speech recording");
    Console.WriteLine($"Speech recognition: {reader.TotalTime.TotalSeconds:0.0}s audio in {watch.Elapsed.TotalSeconds:0.00}s");
    void AddResult(string json) { using var doc=JsonDocument.Parse(json);words.Add(doc.RootElement.GetProperty("text").GetString()??""); }
}
if(args.Length==3&&args[2]=="--loopback")
{
    var lines=new List<string>();Exception? failed=null;
    await using var session=new CaptionSession(args[0],false,"");
    session.Text+=(source,text,final)=> { if(final&&!string.IsNullOrWhiteSpace(text)) lock(lines) lines.Add($"[{source}] {text}"); };
    session.Failed+=error=>failed=error;
    using var input=new WaveFileReader(args[1]);
    using var player=new WasapiPlayerBuilder().WithSharedMode().WithLatency(50).Build();
    player.Init(input);await session.StartAsync();player.Play();
    var playbackWatch=System.Diagnostics.Stopwatch.StartNew();bool split=false;
    while(player.PlaybackState==PlaybackState.Playing)
    {
        await Task.Delay(100);
        if(!split&&playbackWatch.Elapsed.TotalSeconds>=3.5)
        {
            await session.FlushAsync();split=true;
            lock(lines) Require(lines.Count>0,"Translate can finalize an in-progress phrase while capture stays active");
        }
    }
    await Task.Delay(500);
    if(failed is not null) throw failed;
    await session.FlushAsync();
    lock(lines)
    {
        Console.WriteLine("LOOPBACK: "+string.Join(" / ",lines));
        Require(lines.Count>=2&&lines.Any(line=>line.StartsWith("[System]")&&line.Split(' ').Length>=5),"Default-system loopback continues recognizing playback after a translation boundary");
    }
}
Console.WriteLine($"PASS: {checks} caption checks");
