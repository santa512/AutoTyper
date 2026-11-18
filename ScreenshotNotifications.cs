using System.Diagnostics;
using System.IO;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AutoTyper;

internal sealed class ScreenshotNotifications : IDisposable
{
    private bool _registered;
    internal string UnavailableReason { get; private set; } = "Windows notification unavailable";
    internal void Initialize(Action activate)
    {
        try
        {
            if (!AppNotificationManager.IsSupported())
            {
                UnavailableReason = "Windows notifications require running without administrator privileges";
                return;
            }
            AppNotificationManager.Default.NotificationInvoked += (_, _) => activate();
            AppNotificationManager.Default.Register();
            _registered = true;
            CleanOldPreviews();
        }
        catch (Exception exception)
        {
            UnavailableReason = "Windows notification registration failed";
            Debug.WriteLine($"Windows notifications unavailable: {exception}");
        }
    }

    private static string PreviewDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTyper", "NotificationPreviews");

    internal async Task<bool> ShowAsync(string title, string detail, CapturedScreen capture)
    {
        if (!_registered) return false;
        try
        {
            if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled)
            {
                UnavailableReason = "Windows notifications are disabled";
                return false;
            }
            CleanOldPreviews();
            AppNotificationBuilder builder = new AppNotificationBuilder().AddText(title).AddText(detail);
            try
            {
                byte[] preview = await ScreenshotPng.EncodeAsync(capture, 480);
                Directory.CreateDirectory(PreviewDirectory);
                string path = Path.Combine(PreviewDirectory, $"screenshot-{Guid.NewGuid():N}.png");
                await File.WriteAllBytesAsync(path, preview);
                builder.SetInlineImage(new Uri(path));
            }
            catch (Exception exception)
            {
                // A preview failure must still allow a text-only Windows notification.
                Debug.WriteLine($"Notification preview unavailable: {exception}");
            }
            if (!_registered) return false;
            AppNotification notification = builder.BuildNotification();
            notification.Expiration = DateTimeOffset.Now.AddDays(1);
            notification.Tag = "screenshot";
            notification.Group = "capture";
            AppNotificationManager.Default.Show(notification);
            return notification.Id != 0;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Windows notification failed: {exception}");
            return false;
        }
    }

    private static void CleanOldPreviews()
    {
        try
        {
            if (!Directory.Exists(PreviewDirectory)) return;
            foreach (string file in Directory.EnumerateFiles(PreviewDirectory, "screenshot-*.png"))
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1)) File.Delete(file);
        }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }

    public void Dispose()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception exception) { Debug.WriteLine(exception); }
        _registered = false;
    }
}
