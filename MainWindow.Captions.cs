using Microsoft.UI.Xaml;

namespace AutoTyper;

public sealed partial class MainWindow
{
    private CaptionWindow? _captionWindow;
    private void Captions_Click(object sender,RoutedEventArgs e)
    {
        if(_isClosing) return;
        if(_captionWindow is not null) { _captionWindow.Activate();return; }
        var microphone=AudioMicrophone.SelectedItem as AudioDeviceChoice;
        var window=new CaptionWindow(AppSurface.RequestedTheme,microphone?.Id??"",microphone?.Name??"the default microphone",
            ()=>_translationSettings,TranslateCaptionsAsync,()=>_summarySettings.IsConfigured,RequestSummaryAsync);
        _captionWindow=window;
        window.ApplyCaptureProtection(_hideFromScreenCapture);
        window.Closed+=(_,_)=> { if(ReferenceEquals(_captionWindow,window)) _captionWindow=null; };
        window.Activate();
    }
    private async Task TranslateCaptionsAsync(string text,TranslationSettings settings,CancellationToken token)
    {
        if(_isClosing||_isRunning||_isReadingClipboard||_isSelectingRegion||SharingPanel.Visibility==Visibility.Visible)
            throw new InvalidOperationException("Finish the current task and return to the main text box before translating captions.");
        _isReadingClipboard=true;
        SetRegionToolsEnabled(false);ClipboardButton.IsEnabled=StartPauseButton.IsEnabled=false;
        SourceBox.IsReadOnly=true;
        try
        {
            string translated=await new MicrosoftTranslator(TranslationHttpClient).TranslateAsync(text,settings,token);
            token.ThrowIfCancellationRequested();
            if(_isClosing) throw new OperationCanceledException(token);
            string result=MicrosoftTranslator.FormatResult(text,translated,settings.TargetLanguage);
            SourceBox.Text=result;SourceBox.Select(0,0);
            ActivityText.Text="";StatusText.Text="Caption translation loaded";
            RegionSelectionText.Text=$"New captions translated into {settings.TargetLanguage}.";
            ActivateExistingWindow();
        }
        finally
        {
            _isReadingClipboard=false;
            if(!_isClosing)
            {
                SourceBox.IsReadOnly=false;SetRegionToolsEnabled(true);
                ClipboardButton.IsEnabled=StartPauseButton.IsEnabled=true;
            }
        }
    }
}
