using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;

namespace AutoTyper;

public sealed partial class MainWindow
{
    private AudioPreview? _audioPreview;
    private DispatcherTimer? _audioMeterTimer;
    private bool _audioReady,_audioStarting;
    private bool _audioPersistSettings=true;
    private AudioSettings _audioSettings=new();

    private void InitializeAudioPanel()
    {
        _audioSettings=_audioPersistSettings?AudioSettingsStore.Load():new();
        AudioDepthSlider.Value=_audioSettings.Depth;
        AudioBodySlider.Value=_audioSettings.Body*100;
        AudioVolumeSlider.Value=_audioSettings.Volume*100;
        _audioReady=true;
        UpdateAudioLabels();
        _audioMeterTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) };
        _audioMeterTimer.Tick+=(_,_)=>
        {
            if(_audioPreview is not { } preview) return;
            AudioInputMeter.Value=Math.Clamp(preview.InputPeak*100,0,100);
            AudioOutputMeter.Value=Math.Clamp(preview.OutputPeak*100,0,100);
        };
        _=RefreshAudioDevicesAsync();
    }

    private async Task RefreshAudioDevicesAsync()
    {
        if(!_audioReady || _audioPreview is not null || _audioStarting) return;
        AudioRefreshButton.IsEnabled=AudioStartButton.IsEnabled=false;
        try
        {
            string microphone=(AudioMicrophone.SelectedItem as AudioDeviceChoice)?.Id??_audioSettings.MicrophoneId;
            string output=(AudioHeadphones.SelectedItem as AudioDeviceChoice)?.Id??_audioSettings.OutputId;
            var devices=await Task.Run(()=>(Inputs:AudioDevices.Get(DataFlow.Capture),Outputs:AudioDevices.Get(DataFlow.Render)));
            if(_isClosing) return;
            AudioMicrophone.ItemsSource=devices.Inputs;
            AudioHeadphones.ItemsSource=devices.Outputs;
            AudioMicrophone.SelectedItem=devices.Inputs.Find(device=>device.Id==microphone)??devices.Inputs.FirstOrDefault();
            AudioHeadphones.SelectedItem=devices.Outputs.Find(device=>device.Id==output)
                ??devices.Outputs.Find(device=>device.Name.Contains("headphone",StringComparison.OrdinalIgnoreCase)
                    ||device.Name.Contains("headset",StringComparison.OrdinalIgnoreCase))??devices.Outputs.FirstOrDefault();
            AudioStatus.Text=devices.Inputs.Count==0?"No microphone found. Connect a mic and refresh."
                :devices.Outputs.Count==0?"No output device found. Connect headphones and refresh.":"Ready for headphone preview";
        }
        catch(Exception e) when(e is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            if(!_isClosing) AudioStatus.Text="Audio devices are unavailable. Check Windows audio settings and refresh.";
        }
        finally
        {
            if(!_isClosing) { AudioRefreshButton.IsEnabled=true; UpdateAudioControls(); }
        }
    }

    private void AudioRefresh_Click(object sender,RoutedEventArgs e)=>_=RefreshAudioDevicesAsync();
    private void AudioDevice_SelectionChanged(object sender,SelectionChangedEventArgs e) { if(_audioReady) UpdateAudioControls(); }
    private AudioSettings CaptureAudioSettings()=>new()
    {
        MicrophoneId=(AudioMicrophone.SelectedItem as AudioDeviceChoice)?.Id??"",
        OutputId=(AudioHeadphones.SelectedItem as AudioDeviceChoice)?.Id??"",
        Depth=AudioDepthSlider.Value,Body=AudioBodySlider.Value/100,Volume=AudioVolumeSlider.Value/100
    };
    private void AudioOption_Changed(object sender,Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if(!_audioReady) return;
        UpdateAudioLabels();
        _audioPreview?.Configure(CaptureAudioSettings(),AudioOriginalToggle.IsOn);
    }
    private void AudioOriginal_Toggled(object sender,RoutedEventArgs e)
    {
        if(!_audioReady) return;
        _audioPreview?.Configure(CaptureAudioSettings(),AudioOriginalToggle.IsOn);
        if(_audioPreview is not null) AudioStatus.Text=AudioOriginalToggle.IsOn?"Previewing original voice":"Previewing thicker voice";
        UpdateAudioPreviewAppearance(_audioPreview is not null,_audioStarting);
    }
    private void UpdateAudioLabels()
    {
        AudioDepthValue.Text=$"−{AudioDepthSlider.Value:0.0} semitones";
        AudioBodyValue.Text=$"{AudioBodySlider.Value:0}%";
        AudioVolumeValue.Text=$"{AudioVolumeSlider.Value:0}%";
    }
    private void UpdateAudioControls()
    {
        bool active=_audioPreview is not null;
        AudioStartButton.IsEnabled=!_audioStarting&&(active||(AudioMicrophone.SelectedItem is AudioDeviceChoice&&AudioHeadphones.SelectedItem is AudioDeviceChoice));
        UpdateAudioPreviewAppearance(active,_audioStarting);
        AudioMicrophone.IsEnabled=AudioHeadphones.IsEnabled=AudioRefreshButton.IsEnabled=!active&&!_audioStarting;
    }
    internal void UpdateAudioPreviewAppearance(bool active,bool starting=false)
    {
        AudioStartLabel.Text=starting?"Starting…":active?"Stop preview":"Start preview";
        AudioStartIcon.Glyph=active?"\uE71A":"\uE768";
        AudioStartButton.Style=(Style)Application.Current.Resources[active?"DefaultButtonStyle":"AccentButtonStyle"];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AudioStartButton,AudioStartLabel.Text);
        AudioStateText.Text=starting?"Starting preview…":active
            ?(AudioOriginalToggle.IsOn?"Live · Original":"Live · Processed"):"Preview off";
        AudioStateDot.Fill=new SolidColorBrush(active&&!starting
            ?Windows.UI.Color.FromArgb(255,60,180,110):Windows.UI.Color.FromArgb(255,140,140,140));
        if(active) AudioPanel.BorderBrush=AudioStateDot.Fill;
        else AudioPanel.ClearValue(Border.BorderBrushProperty);
        ToolTipService.SetToolTip(AudioStartButton,active
            ?"Stop Auto Typer's audio output. Audio playing in other apps continues."
            :"Start monitoring the selected input through Auto Typer.");
    }
    private async void AudioStart_Click(object sender,RoutedEventArgs e)
    {
        if(_audioPreview is not null) { StopAudioPreview(); return; }
        if(_audioStarting || _isClosing) return;
        _audioStarting=true;
        UpdateAudioControls();
        AudioStatus.Text="Starting local preview…";
        await Task.Yield();
        try
        {
            if(_isClosing || AudioPanel.Visibility!=Visibility.Visible) return;
            if(AudioMicrophone.SelectedItem is not AudioDeviceChoice || AudioHeadphones.SelectedItem is not AudioDeviceChoice)
                throw new InvalidOperationException("Choose a microphone and headphones first.");
            var preview=new AudioPreview(CaptureAudioSettings(),AudioOriginalToggle.IsOn);
            _audioPreview=preview;
            preview.DeviceStopped+=()=>DispatcherQueue.TryEnqueue(() =>
            {
                if(!ReferenceEquals(_audioPreview,preview)) return;
                StopAudioPreview();
                if(!_isClosing) AudioStatus.Text="An audio device stopped. Check its connection and refresh.";
            });
            preview.Start();
            _audioMeterTimer!.Start();
            AudioStatus.Text=AudioOriginalToggle.IsOn?"Previewing original voice":"Previewing thicker voice";
            AudioLatency.Text=$"Estimated stream delay: {preview.DelayMilliseconds:0} ms";
            AudioLatency.Visibility=Visibility.Visible;
        }
        catch(Exception exception) when(exception is COMException or InvalidOperationException or UnauthorizedAccessException
            or DllNotFoundException or BadImageFormatException or System.IO.IOException)
        {
            StopAudioPreview(save:false);
            AudioStatus.Text=exception is DllNotFoundException or BadImageFormatException
                ?"The local audio files are unavailable. Use the complete latest EXE."
                :exception is UnauthorizedAccessException?"Allow desktop microphone access in Windows settings."
                :"Could not start preview. Check microphone access and whether the selected devices are available.";
        }
        finally { _audioStarting=false; if(!_isClosing) UpdateAudioControls(); }
    }
    private void StopAudioPreview(bool save=true)
    {
        _audioMeterTimer?.Stop();
        var preview=_audioPreview; _audioPreview=null;
        try { preview?.Dispose(); }
        catch(Exception e) when(e is COMException or InvalidOperationException) { System.Diagnostics.Debug.WriteLine(e); }
        if(save&&_audioReady&&_audioPersistSettings)
        {
            _audioSettings=CaptureAudioSettings();
            try { AudioSettingsStore.Save(_audioSettings); }
            catch(Exception e) when(e is System.IO.IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(e); }
        }
        if(_isClosing) return;
        AudioStatus.Text="Preview stopped";
        AudioLatency.Text=""; AudioLatency.Visibility=Visibility.Collapsed; AudioInputMeter.Value=AudioOutputMeter.Value=0;
        UpdateAudioControls();
    }
    private void AudioPanel_Click(object sender,RoutedEventArgs e)
    {
        bool open=AudioPanelButton.IsChecked==true;
        if(!open) StopAudioPreview();
        AudioPanel.Visibility=open?Visibility.Visible:Visibility.Collapsed;
        if(open && _windowHandle!=IntPtr.Zero)
        {
            var window=Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle));
            int minimum=(int)Math.Ceiling(1200*Math.Max(96u,GetDpiForWindow(_windowHandle))/96d);
            if(window.Size.Width<minimum) window.Resize(new Windows.Graphics.SizeInt32(minimum,window.Size.Height));
        }
    }
}
