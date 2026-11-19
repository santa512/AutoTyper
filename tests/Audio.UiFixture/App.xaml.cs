using Microsoft.UI.Xaml;
namespace AutoTyper;

// Layout harness: real window, default preferences and no global shortcut registration.
// Summary responses are deterministic local fixtures; no real Groq request is made.
public partial class App : Application
{
    private MainWindow? _window;
    internal static ScreenshotNotifications ScreenshotNotifications { get; }=new();
    public App()=>InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window=new MainWindow(new TypingSettings(),registerHotkeys:false,
            initialSummary:new SummarySettings { ApiKey="gsk_local-ui-fixture-not-a-real-key" },
            summaryRequest:async(text,language,token)=>
            {
                await Task.Delay(350,token);
                return $"Summary preview ({language})\n\n{text[..Math.Min(text.Length,220)]}\n\nLocal layout test response.";
            }) { Title="Auto Typer — Audio UI Preview" };
        _window.Activate();
    }
}
