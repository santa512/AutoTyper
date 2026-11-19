using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoTyper;

public sealed partial class MainWindow
{
    private string _themeChoice = "System", _trayClickAction = "Main", _trayDoubleClickAction = "Main";
    private bool _generalSettingsReady;
    private void InitializeGeneralSettings()
    {
        ThemeSelector.ItemsSource = GeneralPreferenceOptions.Themes;
        TrayClickSelector.ItemsSource = GeneralPreferenceOptions.TrayActions;
        TrayDoubleClickSelector.ItemsSource = GeneralPreferenceOptions.TrayActions;
        _generalSettingsReady = true;
    }
    private void ApplyGeneralDraft(TypingSettings settings)
    {
        MinimizeToTrayToggle.IsOn = settings.MinimizeToTray;
        RunOnStartupToggle.IsOn = settings.RunOnStartup;
        ThemeSelector.SelectedItem = settings.Theme;
        TrayClickSelector.SelectedItem = GeneralPreferenceOptions.TrayActions.First(choice => choice.Id == settings.TrayClickAction);
        TrayDoubleClickSelector.SelectedItem = GeneralPreferenceOptions.TrayActions.First(choice => choice.Id == settings.TrayDoubleClickAction);
        ApplyThemeChoice(settings.Theme);
    }
    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_generalSettingsReady && ThemeSelector.SelectedItem is string choice) ApplyThemeChoice(choice);
    }
    private void ApplyThemeChoice(string choice)
    {
        ElementTheme theme = choice switch { "Dark" => ElementTheme.Dark, "Light" => ElementTheme.Light, _ => ElementTheme.Default };
        AppSurface.RequestedTheme = theme;
        _captionWindow?.ApplyRequestedTheme(theme);
        if (_windowHandle != IntPtr.Zero) UpdateTitleBarTheme();
    }
    private void UpdateSettingsEndPadding()
    {
        if(SharingPanel.Visibility!=Visibility.Visible||SettingsScroller.ViewportHeight<=0) return;
        double bottom=Math.Max(16,SettingsScroller.ViewportHeight-SummarySettingsSection.ActualHeight);
        if(Math.Abs(SettingsSections.Padding.Bottom-bottom)>1) SettingsSections.Padding=new Thickness(0,0,16,bottom);
    }
}
