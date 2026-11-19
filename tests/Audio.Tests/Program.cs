using AutoTyper;
using System.Diagnostics;
using System.Runtime.InteropServices;
int checks=0;
void Require(bool value,string label) { if(!value) throw new Exception(label); checks++; Console.WriteLine("PASS: "+label); }
const int rate=48000;
float[] Run(float[] source,AudioSettings settings,bool original=false)
{
    using var voice=new VoiceProcessor(rate);
    voice.Configure(settings,original);
    float[] result=new float[source.Length],input=new float[960],output=new float[960];
    for(int offset=0;offset<source.Length;)
    {
        int count=Math.Min(input.Length,source.Length-offset);
        source.AsSpan(offset,count).CopyTo(input);
        voice.Process(input,output,count);
        output.AsSpan(0,count).CopyTo(result.AsSpan(offset)); offset+=count;
    }
    return result;
}
float[] Tone(double hz,int seconds=3)=>Enumerable.Range(0,rate*seconds).Select(i=>(float)(0.12*Math.Sin(2*Math.PI*hz*i/rate))).ToArray();
double Frequency(float[] samples,double low,double high)
{
    int start=rate,count=rate;
    double best=0,frequency=0;
    for(double hz=low;hz<=high;hz+=0.5)
    {
        double coefficient=2*Math.Cos(2*Math.PI*hz/rate),s1=0,s2=0;
        for(int i=start;i<start+count;i++) { double s=samples[i]+coefficient*s1-s2; s2=s1;s1=s; }
        double power=s1*s1+s2*s2-coefficient*s1*s2;
        if(power>best) { best=power; frequency=hz; }
    }
    return frequency;
}
using(var voice=new VoiceProcessor(rate)) Require(voice.LatencySamples>0&&voice.LatencySamples<rate/4,"Reported processing delay is bounded below 250 ms");
var settings=new AudioSettings { Depth=1.5,Body=0.45,Volume=0.6 };
var watch=Stopwatch.StartNew();
float[] tone=Tone(180),shifted=Run(tone,settings);
double measured=Frequency(shifted,150,195),expected=180*Math.Pow(2,-1.5/12);
Require(Math.Abs(measured-expected)<1.5,$"Pitch is lowered without changing stream duration ({measured:0.0} Hz, expected {expected:0.0})");
Require(shifted.Length==tone.Length&&shifted.All(float.IsFinite),"Streaming output preserves sample count and finite samples");
var untouched=Run(tone,settings with { Depth=0,Body=0 });
Require(Math.Abs(Frequency(untouched,170,190)-180)<1,"Zero-depth preset preserves the original pitch");
var original=Run(tone,settings,original:true);
Require(Math.Abs(Frequency(original,170,190)-180)<1,"Original comparison preserves the microphone pitch");
Require(original.AsSpan(rate,rate).ToArray().Max(Math.Abs)<0.08f,"Preview volume also applies to original comparison");
var silence=Run(new float[rate],settings);
Require(silence.All(value=>value==0),"Silent input stays silent");
var hostile=Enumerable.Range(0,rate).Select(i=>i%3==0?float.NaN:i%3==1?float.PositiveInfinity:4f).ToArray();
var limited=Run(hostile,settings with { Volume=1 });
Require(limited.All(value=>float.IsFinite(value)&&Math.Abs(value)<=0.90001f),"Invalid and overloaded input cannot produce non-finite or excessive output");
using(var provider=new VoiceWaveProvider())
{
    provider.Configure(settings,false);
    provider.Add(new float[10000],false);
    Require(provider.DroppedFrames==5200,"Backpressure drops stale frames and bounds input buffering to 100 ms");
    var buffer=new byte[960*4];
    Require(provider.Read(buffer)==buffer.Length&&MemoryMarshal.Cast<byte,float>(buffer).ToArray().All(float.IsFinite),"Device adapter supplies full float frames without a microphone");
}
foreach(var invalid in new[]{settings with { Depth=4 },settings with { Body=double.NaN },settings with { Volume=-1 }})
{
    try { invalid.Validate();throw new Exception("Invalid audio settings accepted"); }
    catch(InvalidOperationException) { checks++; }
}
Console.WriteLine($"PASS: {checks} audio DSP and buffering checks in {watch.Elapsed.TotalSeconds:0.00}s. No microphone captured or audio transmitted.");
