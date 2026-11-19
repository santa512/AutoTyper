namespace AutoTyper;

internal sealed record CaptionLine(long Id,string Source,string Text);
internal sealed record CaptionBatch(long Through,string Text,int Count);

// UI-thread owned: recognition updates arrive through the dispatcher. Only successful
// translation acknowledges a batch, so errors and text arriving mid-request are retained.
internal sealed class CaptionTranscript
{
    private readonly List<CaptionLine> _pending=[];
    private long _next;
    internal int PendingCount=>_pending.Count;
    internal void Add(string source,string text)
    {
        text=text.Trim();
        if(text.Length==0) return;
        _pending.Add(new(++_next,source,text));
    }
    internal CaptionBatch? Snapshot(int maximumCharacters=MicrosoftTranslator.MaximumCharacters)
    {
        var lines=new List<string>(); int length=0; long through=0;
        foreach(var line in _pending)
        {
            string value=$"[{line.Source}] {line.Text}";
            int added=value.Length+(lines.Count>0?Environment.NewLine.Length:0);
            if(length+added>maximumCharacters) break;
            lines.Add(value); length+=added; through=line.Id;
        }
        return lines.Count==0?null:new(through,string.Join(Environment.NewLine,lines),lines.Count);
    }
    internal void Acknowledge(CaptionBatch batch)=>_pending.RemoveAll(line=>line.Id<=batch.Through);
}
