using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace AutoTyper;

public sealed partial class CaptionWindow : Window
{
    private readonly CaptionTranscript _transcript=new();
    private readonly Queue<string> _display=[];
    private readonly Dictionary<string,string> _partial=[];
    private readonly CaptionSummaryBuffer _summaryBuffer=new();
    private readonly Func<bool> _summaryConfigured;
    private readonly Func<string,string,CancellationToken,Task<string>> _summarize;
    private CancellationTokenSource? _summaryCancellation;
    private bool _summarizing;
    private readonly Func<TranslationSettings> _settings;
    private readonly Func<string,TranslationSettings,CancellationToken,Task> _translate;
    private readonly string _microphoneId;
    private readonly CancellationTokenSource _lifetime=new();
    private CancellationTokenSource? _startup;
    private CaptionSession? _session;
    private bool _starting,_stopping,_translating,_closing,_loaded;
    private int _displayCharacters;

    internal CaptionWindow(ElementTheme theme,string microphoneId,string microphoneName,
        Func<TranslationSettings> settings,Func<string,TranslationSettings,CancellationToken,Task> translate,
        Func<bool> summaryConfigured,Func<string,string,CancellationToken,Task<string>> summarize)
    {
        InitializeComponent();
        CaptionSurface.RequestedTheme=theme;
        _microphoneId=microphoneId;_settings=settings;_translate=translate;
        _summaryConfigured=summaryConfigured;_summarize=summarize;
        ToolTipService.SetToolTip(IncludeMicrophone,$"Also caption {microphoneName}. Source labels identify System / Mic; they do not identify individual people within either source.");
        CaptionLanguage.ItemsSource=TranslationSettings.Languages;
        CaptionLanguage.SelectedItem=settings().TargetLanguage;
        var presenter=(OverlappedPresenter)AppWindow.Presenter;
        presenter.IsAlwaysOnTop=true;
        presenter.IsMaximizable=false;
        var handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale=Math.Max(96u,GetDpiForWindow(handle))/96d;
        presenter.PreferredMinimumWidth=(int)Math.Ceiling(440*scale);
        presenter.PreferredMinimumHeight=(int)Math.Ceiling(200*scale);
        var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        int width=(int)Math.Ceiling(720*scale),height=(int)Math.Ceiling(280*scale);
        AppWindow.MoveAndResize(new RectInt32(area.X+Math.Max(0,(area.Width-width)/2),area.Y+Math.Max(0,area.Height-height-32),width,height));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","AutoTyper.ico"));
        CaptionSurface.Loaded+=async (_,_)=>
        {
            ApplyTheme();
            if(_loaded) return;
            _loaded=true;
            await StartAsync();
        };
        FinalCaptions.SizeChanged+=(_,_)=>CaptionScroll.ChangeView(null,CaptionScroll.ScrollableHeight,null,true);
        PartialCaptions.SizeChanged+=(_,_)=>CaptionScroll.ChangeView(null,CaptionScroll.ScrollableHeight,null,true);
        CaptionSurface.ActualThemeChanged+=(_,_)=>ApplyTheme();
        CaptionSummaryPanel.Expanding+=(_,_)=>ResizeForSummary(true);
        CaptionSummaryPanel.Collapsed+=(_,_)=>ResizeForSummary(false);
        Activated+=(_,args)=> { if(args.WindowActivationState!=WindowActivationState.Deactivated) UpdateControls(); };
        Closed+=(_,_)=> { _closing=true;_lifetime.Cancel();_startup?.Cancel();_ = StopAsync(); };
        UpdateControls();
    }
    internal void ApplyRequestedTheme(ElementTheme theme) => CaptionSurface.RequestedTheme=theme;
    private void ApplyTheme()
    {
        var handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        int dark=CaptionSurface.ActualTheme==ElementTheme.Dark?1:0;
        DwmSetWindowAttribute(handle,20,ref dark,sizeof(int));
        int color=dark==1?0x00202020:0x00f3f3f3;
        DwmSetWindowAttribute(handle,35,ref color,sizeof(int));
    }
    private void Pin_Click(object sender,RoutedEventArgs e)
    {
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop=CaptionPin.IsChecked==true;
        ToolTipService.SetToolTip(CaptionPin,CaptionPin.IsChecked==true?"Pinned above other windows":"Pin captions above other windows");
    }
    private void UpdateControls()
    {
        if(_closing) return;
        CaptionRetry.Visibility=!_starting&&!_stopping&&_session is null&&_loaded?Visibility.Visible:Visibility.Collapsed;
        IncludeMicrophone.IsEnabled=!_starting&&!_stopping;
        CaptionLanguage.IsEnabled=!_translating&&!_summarizing;
        CaptionSummarize.IsEnabled=!_summarizing&&_summaryConfigured()
            &&(_summaryBuffer.HasText||_partial.Values.Any(text=>!string.IsNullOrWhiteSpace(text)));
        ToolTipService.SetToolTip(CaptionSummarize,_summaryConfigured()
            ?(_summarizing?"Summarizing…":"Summarize captured captions with Groq")
            :"Configure your Groq API key in Settings → AI summary.");
        CaptionTranslate.IsEnabled=!_starting&&!_stopping&&!_translating&&_settings().IsConfigured
            &&(_transcript.PendingCount>0||_partial.Values.Any(text=>!string.IsNullOrWhiteSpace(text)));
        ToolTipService.SetToolTip(CaptionTranslate,_settings().IsConfigured
            ?(_translating?"Translating new captions…":$"Translate new captions · {_transcript.PendingCount} pending segments")
            :"Configure Microsoft Translator in Settings → Translation to enable this button.");
    }
    private void SetStatus(string message)
    {
        if(_closing) return;
        CaptionStatus.Text=message;
        CaptionStatus.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;
    }
    private async void Retry_Click(object sender,RoutedEventArgs e)=>await StartAsync();
    private async void Microphone_Changed(object sender,RoutedEventArgs e)
    {
        if(!_loaded||_starting||_stopping||_closing) return;
        await StopAsync();
        await StartAsync();
    }
    private async Task StartAsync()
    {
        if(_starting||_stopping||_session is not null||_closing) return;
        _starting=true;
        _startup=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token=_startup.Token;
        bool mic=IncludeMicrophone.IsChecked==true;
        UpdateControls();
        try
        {
            await CaptionModel.EnsureAsync(new Progress<string>(message=> { if(_starting) SetStatus(message); }),token);
            SetStatus("Loading captions…");
            var session=await Task.Run(()=>new CaptionSession(CaptionModel.ModelPath,mic,_microphoneId),token);
            if(token.IsCancellationRequested) { await session.DisposeAsync();token.ThrowIfCancellationRequested(); }
            _session=session;
            session.Text+=(source,text,final)=>DispatcherQueue.TryEnqueue(()=>
            {
                if(_closing||!ReferenceEquals(_session,session)) return;
                ReceiveText(source,text,final);
            });
            session.Failed+=exception=>DispatcherQueue.TryEnqueue(async ()=>
            {
                if(_closing||_stopping||!ReferenceEquals(_session,session)) return;
                await StopAsync();
                SetStatus(DescribeError(exception));
            });
            await session.StartAsync();
            token.ThrowIfCancellationRequested();
            SetStatus("");
        }
        catch(OperationCanceledException) { await StopAsync();SetStatus(""); }
        catch(Exception error) when(error is IOException or HttpRequestException or InvalidOperationException or COMException
            or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException)
        {
            await StopAsync();
            SetStatus(DescribeError(error));
        }
        finally { _starting=false;_startup.Dispose();_startup=null;UpdateControls(); }
    }
    private void ReceiveText(string source,string text,bool final)
    {
        if(final)
        {
            _partial.Remove(source);
            if(!string.IsNullOrWhiteSpace(text))
            {
                _transcript.Add(source,text);
                _summaryBuffer.Add(source,text);
                string line=$"[{source}] {text}";
                _display.Enqueue(line);_displayCharacters+=line.Length;
                while(_displayCharacters>12000&&_display.Count>1) _displayCharacters-=_display.Dequeue().Length;
                FinalCaptions.Text=string.Join(Environment.NewLine+Environment.NewLine,_display);
            }
        }
        else _partial[source]=text;
        PartialCaptions.Text=string.Join(Environment.NewLine,_partial.Where(p=>!string.IsNullOrWhiteSpace(p.Value)).Select(p=>$"[{p.Key}] {p.Value}"));
        CaptionScroll.ChangeView(null,CaptionScroll.ScrollableHeight,null,true);
        UpdateControls();
    }
    private async Task DrainUpdatesAsync()
    {
        var marker=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!DispatcherQueue.TryEnqueue(()=>marker.TrySetResult(true))) return;
        await marker.Task;
    }
    private async Task StopAsync()
    {
        if(_stopping) return;
        _stopping=true;UpdateControls();
        var session=_session;
        try
        {
            if(session is not null) { await session.DisposeAsync();await DrainUpdatesAsync(); }
        }
        catch(Exception error) when(error is COMException or InvalidOperationException) { SetStatus(DescribeError(error)); }
        finally
        {
            _session=null;_partial.Clear();_stopping=false;
            if(!_closing) { PartialCaptions.Text="";UpdateControls(); }
        }
    }
    private async void Translate_Click(object sender,RoutedEventArgs e)
    {
        if(_translating||_starting||_stopping||_closing||!_settings().IsConfigured) return;
        _translating=true;UpdateControls();
        try
        {
            // Finalize the current partial utterance first. Later recognition keeps running
            // into a new segment and cannot change the immutable translation snapshot.
            if(_session is { } session) await session.FlushAsync();
            await DrainUpdatesAsync();
            var batch=_transcript.Snapshot();
            if(batch is null) { SetStatus("");return; }
            var settings=_settings() with { TargetLanguage=CaptionLanguage.SelectedItem as string??"English" };
            SetStatus("");
            await _translate(batch.Text,settings,_lifetime.Token);
            if(_closing) return;
            _transcript.Acknowledge(batch);
            SetStatus("");
        }
        catch(OperationCanceledException) { SetStatus("Translation cancelled or timed out. Captions are kept for retry."); }
        catch(Exception error) when(error is InvalidOperationException or HttpRequestException or IOException or System.Threading.Channels.ChannelClosedException)
        { SetStatus(error.Message+" Captions are kept for retry."); }
        finally { _translating=false;UpdateControls(); }
    }
    private static string DescribeError(Exception error)=>error switch
    {
        DllNotFoundException or BadImageFormatException=>"The local speech engine is missing. Use the complete latest x64 EXE.",
        UnauthorizedAccessException=>"Audio access was denied. Check Windows audio permissions.",
        COMException=>"Could not open the audio device. Check the default output and selected microphone, then restart captions.",
        _=>error.Message
    };
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
}
