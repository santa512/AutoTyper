namespace AutoTyper;

internal sealed record CaptionSummarySnapshot(string Text,bool IsRecentOnly);

// Separate from the translation cursor: translating never consumes summary context.
internal sealed class CaptionSummaryBuffer
{
    private readonly Queue<string> _lines=[];
    private int _length;
    private bool _trimmed;
    internal bool HasText=>_lines.Count>0;
    internal void Add(string source,string text)
    {
        if(string.IsNullOrWhiteSpace(text)) return;
        string line=$"[{source}] {text.Trim()}";
        _lines.Enqueue(line);_length+=line.Length+Environment.NewLine.Length;
        while(_length>GroqSummarizer.MaximumCharacters&&_lines.Count>1)
        { _length-=_lines.Dequeue().Length+Environment.NewLine.Length;_trimmed=true; }
    }
    internal CaptionSummarySnapshot Snapshot(IEnumerable<KeyValuePair<string,string>> partials)
    {
        var lines=_lines.Concat(partials.Where(p=>!string.IsNullOrWhiteSpace(p.Value)).Select(p=>$"[{p.Key}] {p.Value}")).ToList();
        bool recent=_trimmed;
        int length=lines.Sum(line=>line.Length+Environment.NewLine.Length);
        while(length>GroqSummarizer.MaximumCharacters&&lines.Count>1)
        { length-=lines[0].Length+Environment.NewLine.Length;lines.RemoveAt(0);recent=true; }
        return new(string.Join(Environment.NewLine,lines),recent);
    }
}
