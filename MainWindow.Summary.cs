using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoTyper;

public sealed partial class MainWindow
{
    private SummarySettings _summarySettings=new();
    private SummarySettings? _summaryBeforeSettings;
    private CancellationTokenSource? _summaryCancellation;
    private bool _summarizing;
    private string? _summarySource;
    private Func<string,string,CancellationToken,Task<string>>? _summaryRequestOverride;
    private static readonly System.Net.Http.HttpClient SummaryHttpClient=GroqSummarizer.CreateHttpClient();
    private SummarySettings CaptureSummarySettings()=>new() { ApiKey=GroqApiKey.Password.Trim(),SecondaryApiKey=GroqSecondaryKey.Password.Trim() };
    private Task<string> RequestSummaryAsync(string text,string language,CancellationToken token)=>
        _summaryRequestOverride is { } request?request(text,language,token)
        :new GroqSummarizer(SummaryHttpClient).SummarizeAsync(text,_summarySettings,language,token);
    private void UpdateSummaryButton()
    {
        if(SummarizeButton is null||SourceBox is null) return;
        SummarizeButton.IsEnabled=!_isClosing&&!_summarizing&&_summarySettings.IsConfigured&&!string.IsNullOrWhiteSpace(SourceBox.Text);
        ToolTipService.SetToolTip(SummarizeButton,_summarySettings.IsConfigured
            ?(_summarizing?"Summarizing…":"Summarize input text with Groq")
            :"Configure your Groq API key in Settings → AI summary.");
    }
    private void UpdateSummarySourceStatus()
    {
        if(_summarizing||_summarySource is null||MainSummaryPanel.Visibility!=Visibility.Visible) return;
        if(SourceBox.Text!=_summarySource&&string.IsNullOrEmpty(MainSummaryStatus.Text))
            MainSummaryStatus.Text="Text changed. Click summarize to update.";
        else if(SourceBox.Text==_summarySource&&MainSummaryStatus.Text=="Text changed. Click summarize to update.") MainSummaryStatus.Text="";
    }
    private async void Summarize_Click(object sender,RoutedEventArgs e)
    {
        if(_summarizing||_isClosing||!_summarySettings.IsConfigured||string.IsNullOrWhiteSpace(SourceBox.Text)) return;
        string source=SourceBox.Text;
        string language=TranslationLanguage.SelectedItem as string??"English";
        _summarizing=true;_summaryCancellation=new();
        CancellationToken token=_summaryCancellation.Token;
        MainSummaryPanel.Visibility=Visibility.Visible;MainSummaryPanel.Width=300;MainSummaryPanel.IsExpanded=true;
        MainSummaryStatus.Text="Summarizing…";UpdateSummaryButton();
        try
        {
            string result=await RequestSummaryAsync(source,language,token);
            token.ThrowIfCancellationRequested();
            if(_isClosing) return;
            MainSummaryText.Text=result;_summarySource=source;
            MainSummaryStatus.Text=SourceBox.Text==source?"":"Text changed. Summary is for the previous text.";
        }
        catch(OperationCanceledException) { if(!_isClosing) MainSummaryStatus.Text=token.IsCancellationRequested?"Summary cancelled.":"Groq timed out. Try again."; }
        catch(Exception error) when(error is InvalidOperationException or System.Net.Http.HttpRequestException or System.IO.IOException)
        { if(!_isClosing) MainSummaryStatus.Text=error is InvalidOperationException?error.Message:"Could not reach Groq. Check your connection and try again."; }
        finally
        {
            _summaryCancellation.Dispose();_summaryCancellation=null;_summarizing=false;
            if(!_isClosing) UpdateSummaryButton();
        }
    }
    private void MainSummaryClose_Click(object sender,RoutedEventArgs e)
    {
        _summaryCancellation?.Cancel();MainSummaryPanel.Visibility=Visibility.Collapsed;
    }
}
