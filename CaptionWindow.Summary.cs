using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace AutoTyper;

public sealed partial class CaptionWindow
{
    private void ResizeForSummary(bool expanded)
    {
        double scale=Math.Max(96u,GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)))/96d;
        ((OverlappedPresenter)AppWindow.Presenter).PreferredMinimumHeight=(int)Math.Ceiling((expanded?480:200)*scale);
        if(expanded&&AppWindow.Size.Height<520*scale)
        {
            var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
            int height=(int)Math.Ceiling(520*scale);
            AppWindow.MoveAndResize(new RectInt32(AppWindow.Position.X,Math.Max(area.Y,Math.Min(AppWindow.Position.Y,area.Y+area.Height-height)),AppWindow.Size.Width,height));
        }
    }
    private async void Summarize_Click(object sender,RoutedEventArgs e)
    {
        if(_closing||_summarizing||!_summaryConfigured()) return;
        var snapshot=_summaryBuffer.Snapshot(_partial.Select(p=>KeyValuePair.Create(p.Key,SpokenText(p.Value))));
        if(string.IsNullOrWhiteSpace(snapshot.Text)) return;
        _summarizing=true;_summaryCancellation=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        CancellationToken token=_summaryCancellation.Token;
        CaptionSummaryPanel.Header=snapshot.IsRecentOnly?"AI summary · Recent captions":"AI summary";
        CaptionSummaryPanel.Visibility=Visibility.Visible;CaptionSummaryPanel.IsExpanded=true;ResizeForSummary(true);
        CaptionSummaryStatus.Text="Summarizing…";UpdateControls();
        try
        {
            string result=await _summarize(snapshot.Text,CaptionLanguage.SelectedItem as string??"English",token);
            token.ThrowIfCancellationRequested();
            if(_closing) return;
            CaptionSummaryText.Text=result;CaptionSummaryStatus.Text="";
        }
        catch(OperationCanceledException) { if(!_closing) CaptionSummaryStatus.Text=token.IsCancellationRequested?"Summary cancelled.":"Groq timed out. Try again."; }
        catch(Exception error) when(error is InvalidOperationException or System.Net.Http.HttpRequestException or System.IO.IOException)
        { if(!_closing) CaptionSummaryStatus.Text=error is InvalidOperationException?error.Message:"Could not reach Groq. Check your connection and try again."; }
        finally { _summaryCancellation.Dispose();_summaryCancellation=null;_summarizing=false;UpdateControls(); }
    }
    private void SummaryClose_Click(object sender,RoutedEventArgs e)
    {
        _summaryCancellation?.Cancel();CaptionSummaryPanel.Visibility=Visibility.Collapsed;ResizeForSummary(false);
    }
}
