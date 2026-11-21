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
        if (Environment.GetCommandLineArgs().Contains("--caption-self-test"))
        {
            try { CaptionSpeechRuntime.EnsureAvailable(); CaptionSpeechRuntime.EnsureWhisperAvailable(); Environment.ExitCode=0; }
            catch { Environment.ExitCode=1; }
            Exit(); return;
        }
        if (Environment.GetCommandLineArgs().Contains("--audio-self-test"))
        {
            try
            {
                using var voice = new VoiceProcessor();
                voice.Configure(new AudioSettings(), original: false);
                var input = new float[960]; var output = new float[960];
                double energy = 0;
                for (int block = 0; block < 50; block++)
                {
                    for (int i = 0; i < input.Length; i++) input[i] = (float)(0.1 * Math.Sin(2 * Math.PI * 180 * (block * 960 + i) / 48000));
                    voice.Process(input, output, input.Length);
                    foreach (float value in output)
                    {
                        if (!float.IsFinite(value) || Math.Abs(value) > 0.901f) throw new InvalidOperationException();
                        energy += value * value;
                    }
                }
                Environment.ExitCode = energy > 0.01 ? 0 : 1;
            }
            catch { Environment.ExitCode = 1; }
            Exit();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--remove-startup"))
        {
            StartupRegistration.RemoveIfMatchingExecutable();
            Exit();
            return;
        }
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
        if (Environment.GetCommandLineArgs().Contains("--startup") && _window is MainWindow main)
            _window.DispatcherQueue.TryEnqueue(main.StartFromWindowsLogin);
    }
}
