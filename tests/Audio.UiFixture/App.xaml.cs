using Microsoft.UI.Xaml;
namespace AutoTyper;

// Layout harness: uses the real window with default preferences and no global shortcut registration.
public partial class App : Application
{
    private MainWindow? _window;
    internal static ScreenshotNotifications ScreenshotNotifications { get; }=new();
    public App()=>InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window=new MainWindow(new TypingSettings(),registerHotkeys:false) { Title="Auto Typer — Audio UI Preview" };
        _window.Activate();
    }
}
