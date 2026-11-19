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
CaptionSpeechRuntime.EnsureAvailable();
Require(true,"Pinned x64 speech engine and native dependencies load without a model download");
if(args.Length>=2)
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
