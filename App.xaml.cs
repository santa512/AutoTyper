using Microsoft.UI.Xaml;

namespace AutoTyper;

public partial class App : Application
{
    private Window? _window;
    private SingleInstance? _instance;
    internal static ScreenshotNotifications ScreenshotNotifications { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instance = new SingleInstance();
        if (!_instance.EnterOrActivateExisting())
        {
            _instance.Dispose();
            _instance = null;
            Exit();
            return;
        }
        try
        {
            _window = new MainWindow();
            _instance.PublishWindow(WinRT.Interop.WindowNative.GetWindowHandle(_window));
        }
        catch
        {
            _instance.Dispose();
            _instance = null;
            throw;
        }
        ScreenshotNotifications.Initialize(() => _window.DispatcherQueue.TryEnqueue(() =>
        {
            if (_window is MainWindow main) main.ActivateFromNotification();
        }));
        _window.Closed += (_, _) =>
        {
            ScreenshotNotifications.Dispose();
            _instance?.Dispose();
            _instance = null;
        };
        _window.Activate();
    }
}
