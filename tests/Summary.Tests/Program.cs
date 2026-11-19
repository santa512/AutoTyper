using AutoTyper;
using System.Net;
using System.Text;
using System.Text.Json;
int checks=0;
void Require(bool value,string label) { if(!value) throw new Exception(label);checks++;Console.WriteLine("PASS: "+label); }
async Task Reject(Func<Task> action,string label)
{
    try { await action();throw new Exception("Expected rejection: "+label); }
    catch(InvalidOperationException) { checks++;Console.WriteLine("PASS: "+label); }
}
var settings=new SummarySettings { ApiKey="gsk_test-key-not-a-real-secret-123456",SecondaryApiKey="gsk_secondary-test-key-not-a-real-secret" };
const string source="[System] We agreed to ship on Friday.\n[Mic] I will write the notes.";
int calls=0;
using(var client=new HttpClient(new FakeHandler(async (request,token)=>
{
    calls++;
    Require(request.RequestUri!.AbsoluteUri=="https://api.groq.com/openai/v1/chat/completions","Only the official Groq endpoint receives the request");
    Require(request.Headers.Authorization?.Scheme=="Bearer"&&request.Headers.Authorization.Parameter==settings.ApiKey,"Groq key is sent as a Bearer header");
    using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var root=json.RootElement;
    Require(root.GetProperty("model").GetString()==GroqSummarizer.Model&&!root.GetProperty("include_reasoning").GetBoolean(),"Pinned model requests the final answer without reasoning output");
    Require(root.GetProperty("messages")[1].GetProperty("content").GetString()==source,"Immutable source snapshot is sent exactly as captured");
    Require(root.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("French"),"Selected language is used for the summary");
    string body=await request.Content.ReadAsStringAsync(token);
    Require(!body.Contains(settings.ApiKey)&&!body.Contains(settings.SecondaryApiKey),"Both API keys stay out of source text and request body");
    return new(HttpStatusCode.OK) { Content=new StringContent("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\" Ship Friday; prepare notes. \"}}]}") };
})))
{
    string result=await new GroqSummarizer(client).SummarizeAsync(source,settings,"French",CancellationToken.None);
    Require(result=="Ship Friday; prepare notes.","Only summary content is returned");
    await Reject(()=>new GroqSummarizer(client).SummarizeAsync(" ",settings,"English",default),"Empty text is rejected before sending");
    await Reject(()=>new GroqSummarizer(client).SummarizeAsync(new string('x',60001),settings,"English",default),"Oversized input is rejected without silent truncation");
    await Reject(()=>new GroqSummarizer(client).SummarizeAsync(source,new(),"English",default),"Missing Groq configuration is rejected before sending");
    await Reject(()=>new GroqSummarizer(client).SummarizeAsync(source,settings,"Unknown",default),"Unsupported summary languages are rejected");
    using var cancellation=new CancellationTokenSource();cancellation.Cancel();
    try { await new GroqSummarizer(client).SummarizeAsync(source,settings,"English",cancellation.Token);throw new Exception("Expected cancellation"); }
    catch(OperationCanceledException) { checks++; }
    Require(calls==1,"Invalid or cancelled operations make no extra API calls");
}
foreach(var quotaStatus in new[]{HttpStatusCode.TooManyRequests,HttpStatusCode.PaymentRequired})
{
    var keys=new List<string>();var bodies=new List<string>();
    using var client=new HttpClient(new FakeHandler(async(request,token)=>
    {
        keys.Add(request.Headers.Authorization!.Parameter!);bodies.Add(await request.Content!.ReadAsStringAsync(token));
        return keys.Count==1?new HttpResponseMessage(quotaStatus):new(HttpStatusCode.OK)
            { Content=new StringContent("{\"choices\":[{\"message\":{\"content\":\"Fallback summary\"}}]}") };
    }));
    string result=await new GroqSummarizer(client).SummarizeAsync(source,settings,"English",default);
    Require(result=="Fallback summary"&&keys.SequenceEqual(new[]{settings.ApiKey,settings.SecondaryApiKey}),"Primary quota/rate limit retries once with the secondary key");
    Require(bodies.Count==2&&bodies[0]==bodies[1],"Failover retries the same immutable source and language");
}
{
    int attempts=0;using var cancellation=new CancellationTokenSource();
    using var client=new HttpClient(new FakeHandler((_,_)=>
    {
        attempts++;cancellation.Cancel();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
    }));
    try { await new GroqSummarizer(client).SummarizeAsync(source,settings,"English",cancellation.Token);throw new Exception("Expected cancellation"); }
    catch(OperationCanceledException) { Require(attempts==1,"Cancellation prevents the secondary retry"); }
}
{
    using var client=new HttpClient(new FakeHandler((request,_)=>
    {
        Require(request.Headers.Authorization?.Parameter==settings.SecondaryApiKey,"A secondary-only configuration uses its saved key");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent("{\"choices\":[{\"message\":{\"content\":\"Summary\"}}]}") });
    }));
    Require(await new GroqSummarizer(client).SummarizeAsync(source,settings with { ApiKey="" },"English",default)=="Summary","Either configured key can enable summarization");
}
foreach(var status in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.TooManyRequests})
{
    int attempts=0;
    using var client=new HttpClient(new FakeHandler((_,_)=> { attempts++;return Task.FromResult(new HttpResponseMessage(status)); }));
    await Reject(()=>new GroqSummarizer(client).SummarizeAsync(source,settings,"English",default),"Exhausted or invalid keys return a safe error");
    Require(attempts==(status==HttpStatusCode.Unauthorized?1:2),"Authentication errors do not rotate keys, and quota failover is bounded to two requests");
}
{
    int attempts=0;using var client=new HttpClient(new FakeHandler((_,_)=> { attempts++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)); }));
    await Reject(()=>new GroqSummarizer(client).SummarizeAsync(source,settings with { SecondaryApiKey=settings.ApiKey },"English",default),"Identical primary and secondary keys are not retried");
    Require(attempts==1,"Duplicate key produces only one API attempt");
}
foreach(var response in new[]{"{}","[]","{\"choices\":[]}","{\"choices\":[{\"message\":{\"content\":null}}]}","{\"choices\":[{\"message\":{\"content\":\"\"}}]}","{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"partial\"}}]}"})
{
    using var client=new HttpClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(response) })));
    await Reject(()=>new GroqSummarizer(client).SummarizeAsync(source,settings,"English",default),"Invalid, empty or incomplete responses preserve the prior result");
}
foreach(var code in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden,HttpStatusCode.TooManyRequests,HttpStatusCode.BadRequest,HttpStatusCode.InternalServerError})
{
    using var client=new HttpClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(code) { Content=new StringContent(settings.ApiKey) })));
    try { await new GroqSummarizer(client).SummarizeAsync(source,settings,"English",default);throw new Exception("Expected service error"); }
    catch(InvalidOperationException e) { Require(!e.Message.Contains(settings.ApiKey),"Service errors never echo credentials or raw response bodies"); }
}
var encrypted=SummarySettingsStore.Encode(settings);
Require(SummarySettingsStore.Decode(encrypted)==settings&&!Encoding.UTF8.GetString(encrypted).Contains(settings.ApiKey)
    &&!Encoding.UTF8.GetString(encrypted).Contains(settings.SecondaryApiKey),"Both Groq keys round trip with Windows encryption");
Require(!settings.ToString().Contains(settings.ApiKey),"Summary settings description hides the key");
Require(!new SummarySettings().IsConfigured&&settings.IsConfigured,"Summary availability follows saved API configuration");
var captions=new CaptionSummaryBuffer();var translation=new CaptionTranscript();
captions.Add("System","first statement");translation.Add("System","first statement");
var snapshot=captions.Snapshot([]);translation.Acknowledge(translation.Snapshot()!);
captions.Add("Mic","new reply");
Require(snapshot.Text=="[System] first statement","Caption summary snapshot does not change as speech arrives");
var next=captions.Snapshot(new Dictionary<string,string> { ["System"]="unfinished phrase" });
Require(next.Text.Contains("first statement")&&next.Text.Contains("[Mic] new reply")&&next.Text.Contains("unfinished phrase"),"Summaries include translated captions, newer segments and current partials without consuming any captions");
for(int i=0;i<100;i++) captions.Add("System",new string('x',1000));
var recent=captions.Snapshot([]);
Require(recent.IsRecentOnly&&recent.Text.Length<=GroqSummarizer.MaximumCharacters,"Long sessions keep bounded summary context and mark it as recent captions");
Console.WriteLine($"PASS: {checks} Groq summary and caption context checks. No external requests made.");
internal sealed class FakeHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token); }
