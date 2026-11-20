using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using System.IO;
using System.Net.Http;

namespace AutoTyper;

public sealed partial class MainWindow : Window
{
    private static readonly (string Keys, double Offset)[] KeyboardRows =
    {
        ("qwertyuiop", 0),
        ("asdfghjkl", 0.25),
        ("zxcvbnm", 0.75)
    };

    private const int GlobalHotkeyId = 0x5450;
    private const uint WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModAlt = 0x0001;
    private const uint ModWindows = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkAlt = 0x12;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private static readonly UIntPtr HotkeySubclassId = new(0x5450u);

    private readonly Random _random = new();
    private readonly List<PendingCorrection> _corrections = new();
    private readonly SubclassProc _windowSubclassProc;
    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource<bool>? _resumeSignal;
    private int _rhythmGroupCharactersRemaining;
    private double _rhythmGroupScale = 1;
    private IntPtr _windowHandle;
    private string _source = string.Empty;
    private int _sourceIndex;
    private int _caret;
    private bool _isRunning;
    private bool _isPaused;
    private bool _isCountingDown;
    private bool _isRecordingHotkey;
    private bool _windowSubclassed;
    private GlobalHotkeySet? _globalHotkeys;
    private Dictionary<string, HotkeyGesture> _activeHotkeys = new TypingSettings().GetHotkeys();
    private Dictionary<string, HotkeyGesture> _draftHotkeys = new TypingSettings().GetHotkeys();
    private TextBox? _recordingInput;
    private string _recordingAction = "Typing";
    private int _typedLength;
    private bool _hasProgress;
    private int[] _sourceTextOffsets = Array.Empty<int>();
    private IntPtr _targetWindow;
    private TypingFocusTarget? _typingFocusTarget;
    private bool _focusPaused;
    private bool _isReadingClipboard;
    private bool _isClosing;
    private bool _isSelectingRegion;
    private ScreenRegionSelector? _regionSelector;
    private InMemoryRandomAccessStream? _screenshotClipboardStream;
    private CancellationTokenSource? _ocrCancellation;
    private CancellationTokenSource? _shareCancellation;
    private CancellationTokenSource? _translationCancellation;
    private TranslationSettings _translationSettings = new();
    private TranslationSettings? _translationBeforeSettings;
    private string _translationDraftLanguage = "English";
    private SharingSettings? _sharingSettings;
    private TaskCompletionSource<SharingSettings?>? _sharingCompletion;
    private TypingSettings? _typingBeforeSettings;
    private string[] _cloudBeforeSettings = [];
    private bool _settingsSaved;
    private bool _sharingRequired;
    private TrayIcon? _trayIcon;
    private bool _minimizeToTray;
    private bool _isInTray;
    private bool _exitRequested;
    private readonly bool _persistPreferences;
    private static readonly HttpClient ShareHttpClient = ScreenshotShareClient.CreateHttpClient();
    private static readonly HttpClient TranslationHttpClient = MicrosoftTranslator.CreateHttpClient();
    internal ScreenRegion? SelectedRegion { get; private set; }

    private void SetRegionToolsEnabled(bool enabled)
    {
        foreach (UIElement child in RegionTools.Children)
            if (child is Button button) button.IsEnabled = enabled;
        ShareToolButton.IsEnabled = enabled && _sharingSettings is not null;
        RefreshActionTooltips();
        CopyResultToggle.IsEnabled = enabled;
        SharingSettingsButton.IsEnabled = enabled;
        TranslationLanguage.IsEnabled = enabled;
        TranslateButton.IsEnabled = enabled && _translationSettings.IsConfigured;
        UpdateSummaryButton();
    }

    private async void RegionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isReadingClipboard || _isSelectingRegion || _isClosing) return;
        string tool = (sender as Button)?.Tag as string ?? "Screenshot";
        if (tool == "Share" && _sharingSettings is null)
        {
            RegionSelectionText.Text = "Configure your Cloudflare bucket in Settings to enable sharing.";
            return;
        }
        bool copyResult = CopyResultToggle.IsOn;
        bool returnToTray = _isInTray;
        _isSelectingRegion = true;
        SetRegionToolsEnabled(false);
        ClipboardButton.IsEnabled = StartPauseButton.IsEnabled = RecordHotkeyButton.IsEnabled = false;
        EndHotkeyRecording();
        try
        {
            if (tool == "Share")
            {
                _sharingSettings ??= SharingSettingsStore.Load();
                _sharingSettings ??= await ConfigureSharingAsync(requireSharing: true);
                if (_sharingSettings is null || _isClosing) return;
            }
            using var selector = new ScreenRegionSelector(tool);
            _regionSelector = selector;
            ShowWindow(_windowHandle, 0);
            ScreenRegion? region = await selector.SelectAsync();
            if (_isClosing) return;
            SelectedRegion = region;
            if (region is { } shareRegion && tool == "Share")
            {
                _shareCancellation = new CancellationTokenSource();
                CancellationToken shareToken = _shareCancellation.Token;
                CapturedScreen capture = await Task.Run(() => ScreenCapture.Capture(shareRegion));
                if (_isClosing) return;
                byte[] png = await ScreenshotPng.EncodeAsync(capture);
                if (_isClosing) return;
                ShowWindow(_windowHandle, 5);
                Activate();
                StopButton.IsEnabled = true;
                StatusText.Text = "Uploading";
                RegionSelectionText.Text = "Uploading screenshot to your public R2 bucket…";
                Uri link = await new ScreenshotShareClient(ShareHttpClient).UploadAsync(png, _sharingSettings!, shareToken);
                if (_isClosing) return;
                bool historySaved = true;
                try { ShareHistoryStore.Add(link, DateTimeOffset.UtcNow); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                { historySaved = false; }
                SourceBox.Text = link.AbsoluteUri;
                SourceBox.Select(0, 0);
                ActivityText.Text = string.Empty;
                RegionSelectionText.Text = $"Saved for 14 days · {link.AbsoluteUri}";
                if (!historySaved) RegionSelectionText.Text += " · Could not save history";
                StatusText.Text = "Shared";
                bool copied = false;
                try
                {
                    var content = new DataPackage();
                    content.SetText(link.AbsoluteUri);
                    Clipboard.SetContent(content);
                    Clipboard.Flush();
                    StatusText.Text = "Link copied";
                    copied = true;
                }
                catch (Exception exception) when (exception is COMException or InvalidOperationException or UnauthorizedAccessException)
                {
                    RegionSelectionText.Text = $"Shared (could not copy): {link.AbsoluteUri}";
                    Debug.WriteLine(exception);
                }
                await App.ScreenshotNotifications.ShowAsync("Screenshot shared",
                    copied ? "Public link copied · Saved for 14 days" : $"{link.AbsoluteUri} · Saved for 14 days", capture);
                return;
            }
            if (region is { } textRegion && tool == "Text Extract")
            {
                _ocrCancellation = new CancellationTokenSource();
                CancellationToken ocrToken = _ocrCancellation.Token;
                SourceBox.IsReadOnly = true;
                StopButton.IsEnabled = true;
                // Capture while hidden, then restore the UI during recognition.
                CapturedScreen capture = await Task.Run(() => ScreenCapture.Capture(textRegion));
                if (_isClosing) return;
                ShowWindow(_windowHandle, 5);
                Activate();
                RegionSelectionText.Text = "Recognizing English text…";
                StatusText.Text = "Reading text";
                OcrText? result = await Task.Run(() => EnglishOcr.Recognize(capture, ocrToken), ocrToken);
                if (_isClosing) return;
                if (result is null)
                {
                    StatusText.Text = "No text found";
                    RegionSelectionText.Text = "No clear English text found. Select a tighter region with readable text.";
                }
                else
                {
                    var clipboardResult = new DataPackage();
                    clipboardResult.SetText(result.Text);
                    ApplyToolResult(tool, result.Text, clipboardResult, copyResult);
                    if (result.NeedsReview || result.Confidence < 0.85f)
                        RegionSelectionText.Text += " · Review text: recognition was uncertain";
                }
                return;
            }
            if (region is { } screenshotRegion && tool == "Screenshot")
            {
                CapturedScreen capture = await Task.Run(() => ScreenCapture.Capture(screenshotRegion));
                if (_isClosing) return;
                byte[] png = await ScreenshotPng.EncodeAsync(capture);
                if (_isClosing) return;
                ShowWindow(_windowHandle, 5);
                Activate();
                await CompleteScreenshotAsync(capture, png, copyResult);
                return;
            }
            if (region is { } qrRegion && tool == "QR")
            {
                RegionSelectionText.Text = "Scanning QR code…";
                string? decoded = await Task.Run(() => QrScreenScanner.Scan(qrRegion));
                if (_isClosing) return;
                ShowWindow(_windowHandle, 5);
                Activate();
                if (decoded is null)
                    RegionSelectionText.Text = "No QR code found. Include the entire code and its border, then try again.";
                else
                {
                    var clipboardResult = new DataPackage();
                    clipboardResult.SetText(decoded);
                    ApplyToolResult(tool, decoded, clipboardResult, copyResult);
                }
                return;
            }
            RegionSelectionText.Text = region is { } selected
                ? $"{tool}: {selected.Width} × {selected.Height} px at ({selected.X}, {selected.Y}) · Selection only"
                : $"{tool}: selection cancelled";
        }
        catch (OperationCanceledException) when (tool == "Share")
        {
            if (!_isClosing)
            {
                StatusText.Text = "Sharing stopped";
                RegionSelectionText.Text = _shareCancellation?.IsCancellationRequested == true
                    ? "Sharing stopped. An upload already sent may have completed."
                    : "Upload timed out. It may have completed, but no link was received.";
            }
        }
        catch (HttpRequestException) when (tool == "Share")
        {
            if (!_isClosing)
            {
                StatusText.Text = "Upload failed";
                RegionSelectionText.Text = "Could not reach the upload service. Check your connection and Sharing settings.";
            }
        }
        catch (OperationCanceledException) when (tool == "Text Extract")
        {
            if (!_isClosing)
            {
                StatusText.Text = "Cancelled";
                RegionSelectionText.Text = "Text extraction cancelled";
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            if (!_isClosing) RegionSelectionText.Text = "Could not select or read the screen region. Try again.";
        }
        catch (ArgumentException exception)
        {
            if (!_isClosing) RegionSelectionText.Text = exception.Message;
        }
        catch (Exception exception) when (tool == "Text Extract" && (exception is Microsoft.ML.OnnxRuntime.OnnxRuntimeException
            or DllNotFoundException or BadImageFormatException or TypeInitializationException))
        {
            if (!_isClosing)
            {
                StatusText.Text = "OCR unavailable";
                RegionSelectionText.Text = "English OCR could not start. Use the latest complete Auto Typer EXE.";
            }
            Debug.WriteLine(exception);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            if (!_isClosing && tool == "Share") StatusText.Text = "Upload failed";
            if (!_isClosing) RegionSelectionText.Text = tool == "Share"
                ? exception.Message : $"Could not finish {tool}. Try again.";
            Debug.WriteLine(exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!_isClosing && tool == "Share") StatusText.Text = "Upload failed";
            if (!_isClosing) RegionSelectionText.Text = tool == "Share" ? "Could not upload the screenshot. Try again." : tool == "Text Extract"
                ? "Could not read the English OCR files. Use the latest complete EXE."
                : "Could not save the screenshot. Try another location.";
            Debug.WriteLine(exception);
        }
        finally
        {
            _shareCancellation?.Dispose();
            _shareCancellation = null;
            _ocrCancellation?.Dispose();
            _ocrCancellation = null;
            _regionSelector = null;
            _isSelectingRegion = false;
            if (!_isClosing)
            {
                if (returnToTray && _isInTray && _minimizeToTray && _trayIcon?.IsVisible == true) HideToTray();
                else { ShowWindow(_windowHandle, 5); Activate(); }
                SetRegionToolsEnabled(true);
                ClipboardButton.IsEnabled = StartPauseButton.IsEnabled = true;
                RecordHotkeyButton.IsEnabled = _windowSubclassed;
                if (tool is "Text Extract" or "Share")
                {
                    SourceBox.IsReadOnly = false;
                    StopButton.IsEnabled = false;
                }
            }
        }
    }

    private async void SharingSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isReadingClipboard || _isSelectingRegion || _isClosing) return;
        _isSelectingRegion = true;
        SetRegionToolsEnabled(false);
        StartPauseButton.IsEnabled = ClipboardButton.IsEnabled = false;
        try { await ConfigureSharingAsync(); }
        finally
        {
            _isSelectingRegion = false;
            if (!_isClosing)
            {
                SetRegionToolsEnabled(true);
                StartPauseButton.IsEnabled = ClipboardButton.IsEnabled = true;
                RecordHotkeyButton.IsEnabled = _windowSubclassed;
            }
        }
    }

    private async Task<SharingSettings?> ConfigureSharingAsync(bool requireSharing = false, string? initialSection = null)
    {
        _typingBeforeSettings = CaptureTypingSettings();
        _translationBeforeSettings = _translationSettings;
        _summaryBeforeSettings = _summarySettings;
        GroqApiKey.Password = _summarySettings.ApiKey;
        GroqSecondaryKey.Password = _summarySettings.SecondaryApiKey;
        _translationDraftLanguage = _translationSettings.TargetLanguage;
        BackupPassword.Password = BackupStatusText.Text = "";
        TranslationApiKey.Password = _translationSettings.ApiKey;
        TranslationRegion.Text = _translationSettings.Region;
        _draftHotkeys = new(_activeHotkeys);
        _globalHotkeys?.Release();
        UpdateHotkeyLabels();
        HotkeyStatusText.Text = "Shortcuts pause while Settings is open. Save to apply changes.";
        _settingsSaved = false;
        _sharingRequired = requireSharing;
        SharingSettings current = _sharingSettings ?? (_persistPreferences ? SharingSettingsStore.Load() : null) ?? new SharingSettings();
        SharingAccount.Text = current.AccountId;
        SharingBucket.Text = current.BucketName;
        SharingAccessKey.Password = current.AccessKeyId;
        SharingSecretKey.Password = current.SecretAccessKey;
        SharingPublicUrl.Text = current.PublicBaseUrl;
        SharingJurisdiction.SelectedIndex = current.Jurisdiction switch { "eu" => 1, "us" => 2, "fedramp" => 3, _ => 0 };
        _cloudBeforeSettings = CaptureCloudSettingsValues();
        UnsavedChangesPrompt.Visibility = Visibility.Collapsed;
        SharingError.Text = "";
        _sharingCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainContent.Visibility = Visibility.Collapsed;
        SharingPanel.Visibility = Visibility.Visible;
        RecordHotkeyButton.IsEnabled = _windowSubclassed;
        SettingsScroller.UpdateLayout();
        UpdateSettingsEndPadding();
        SettingsScroller.UpdateLayout();
        ScrollToSettingsSection(initialSection ?? (requireSharing ? "Cloud" : "General"));
        if (requireSharing) SharingAccount.Focus(FocusState.Programmatic);
        else GeneralSettingsTab.Focus(FocusState.Programmatic);
        try { return await _sharingCompletion.Task; }
        finally
        {
            if (!_settingsSaved && _typingBeforeSettings is { } previous && !_isClosing)
                ApplyTypingSettings(previous);
            EndHotkeyRecording();
            if (!_isClosing) RegisterActiveHotkeys();
            SharingPanel.Visibility = Visibility.Collapsed;
            CancelImportPassword_Click(this, new RoutedEventArgs());
            BackupPassword.Password = "";
            MainContent.Visibility = Visibility.Visible;
            SharingAccessKey.Password = SharingSecretKey.Password = "";
            TranslationApiKey.Password = "";
            GroqApiKey.Password = "";
            GroqSecondaryKey.Password = "";
            BackupPassword.Password = "";
            _translationBeforeSettings = null;
            _summaryBeforeSettings = null;
            _cloudBeforeSettings = [];
            _sharingCompletion = null;
        }
    }

    private string[] CaptureCloudSettingsValues() =>
    [
        SharingAccount.Text.Trim(), SharingBucket.Text.Trim(), SharingAccessKey.Password.Trim(),
        SharingSecretKey.Password.Trim(), SharingPublicUrl.Text.Trim(),
        (SharingJurisdiction.SelectedItem as ComboBoxItem)?.Tag as string ?? "default"
    ];

    private void SharingBack_Click(object sender, RoutedEventArgs e)
    {
        bool changed = CaptureTypingSettings(useDraft: true) != _typingBeforeSettings
            || CaptureTranslationSettings() != _translationBeforeSettings
            || CaptureSummarySettings() != _summaryBeforeSettings
            || !CaptureCloudSettingsValues().SequenceEqual(_cloudBeforeSettings);
        if (changed) UnsavedChangesPrompt.Visibility = Visibility.Visible;
        else _sharingCompletion?.TrySetResult(null);
    }

    private void SaveChangesYes_Click(object sender, RoutedEventArgs e) => SharingSave_Click(sender, e);

    private void DiscardChangesNo_Click(object sender, RoutedEventArgs e) => _sharingCompletion?.TrySetResult(null);

    private void SharingSave_Click(object sender, RoutedEventArgs e)
    {
        UnsavedChangesPrompt.Visibility = Visibility.Collapsed;
        try
        {
            var settings = new SharingSettings
            {
                AccountId = SharingAccount.Text.Trim(), BucketName = SharingBucket.Text.Trim(),
                AccessKeyId = SharingAccessKey.Password.Trim(), SecretAccessKey = SharingSecretKey.Password.Trim(),
                Jurisdiction = (string)((ComboBoxItem)SharingJurisdiction.SelectedItem).Tag,
                PublicBaseUrl = SharingPublicUrl.Text.Trim()
            };
            TypingSettings typing = CaptureTypingSettings(useDraft: true);
            typing.Validate();
            TranslationSettings translation = CaptureTranslationSettings();
            translation.Validate(requireKey: false);
            SummarySettings summary = CaptureSummarySettings();
            summary.Validate(requireKey: false);
            bool hasCloudSettings = new[] { settings.AccountId, settings.BucketName, settings.AccessKeyId, settings.SecretAccessKey }
                .Any(value => !string.IsNullOrWhiteSpace(value));
            if (hasCloudSettings || _sharingRequired) settings.Validate();
            if (_windowSubclassed && _globalHotkeys?.TryApply(BuildHotkeyRegistrations(typing.GetHotkeys(), translation.IsConfigured)) == false)
                throw new InvalidOperationException($"Could not register {_globalHotkeys.FailedAction}. Choose another shortcut in Hotkeys; another app may be using it.");
            if (_persistPreferences)
            {
                StartupRegistration.Apply(typing.RunOnStartup);
                if (hasCloudSettings || _sharingRequired) SharingSettingsStore.Save(settings);
                else SharingSettingsStore.Clear();
                TypingSettingsStore.Save(typing);
                TranslationSettingsStore.Save(translation);
                SummarySettingsStore.Save(summary);
            }
            _summarySettings = summary;
            UpdateSummaryButton();
            _translationSettings = translation;
            TranslationLanguage.SelectedItem = translation.TargetLanguage;
            _sharingSettings = hasCloudSettings ? settings : null;
            ApplyTypingSettings(typing);
            _settingsSaved = true;
            _sharingCompletion?.TrySetResult(hasCloudSettings ? settings : null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            _globalHotkeys?.Release();
            SharingError.Text = exception is InvalidOperationException ? exception.Message : "Could not save settings. Try again.";
        }
    }

    private SettingsBundle CaptureSettingsBackup()
    {
        var cloud = new SharingSettings
        {
            AccountId = SharingAccount.Text.Trim(), BucketName = SharingBucket.Text.Trim(),
            AccessKeyId = SharingAccessKey.Password.Trim(), SecretAccessKey = SharingSecretKey.Password.Trim(),
            Jurisdiction = (string)((ComboBoxItem)SharingJurisdiction.SelectedItem).Tag,
            PublicBaseUrl = SharingPublicUrl.Text.Trim()
        };
        bool hasCloud = new[] { cloud.AccountId, cloud.BucketName, cloud.AccessKeyId, cloud.SecretAccessKey }
            .Any(value => !string.IsNullOrWhiteSpace(value));
        var bundle = new SettingsBundle(CaptureTypingSettings(useDraft: true), hasCloud ? cloud : null, CaptureTranslationSettings(), CaptureSummarySettings());
        bundle.Validate();
        return bundle;
    }

    private async void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_sharingCompletion is null || _isClosing) return;
        try
        {
            string password = BackupPassword.Password;
            SettingsBackup.ValidatePassword(password);
            SettingsBundle bundle = CaptureSettingsBackup();
            SharingPanel.IsEnabled = false;
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id)
            {
                SuggestedFileName = $"AutoTyper-Settings-{DateTime.Now:yyyy-MM-dd}",
                DefaultFileExtension = ".autotyper"
            };
            picker.FileTypeChoices.Add("Encrypted Auto Typer settings", new List<string> { ".autotyper" });
            var file = await picker.PickSaveFileAsync();
            if (_isClosing) return;
            if (file is null) { BackupStatusText.Text = "Export cancelled."; return; }
            byte[] data = await Task.Run(() => SettingsBackup.Export(bundle, password));
            if (_isClosing) return;
            await File.WriteAllBytesAsync(file.Path, data);
            if (!_isClosing) { BackupPassword.Password = ""; BackupStatusText.Text = "Settings exported with all configured keys. Keep the backup password."; }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or COMException)
        {
            if (!_isClosing) BackupStatusText.Text = exception is InvalidOperationException
                ? exception.Message : "Could not export settings. Try another location.";
        }
        finally { if (!_isClosing) SharingPanel.IsEnabled = true; }
    }

    private byte[]? _pendingImportData;
    private void CancelImportPassword_Click(object sender, RoutedEventArgs e)
    {
        _pendingImportData = null;
        ImportBackupPassword.Password = "";
        ImportPasswordPanel.Visibility = Visibility.Collapsed;
    }
    private async void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_sharingCompletion is null || _isClosing) return;
        CancelImportPassword_Click(sender, e);
        try
        {
            SharingPanel.IsEnabled = false;
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id);
            picker.FileTypeFilter.Add(".autotyper");
            var file = await picker.PickSingleFileAsync();
            if (_isClosing) return;
            if (file is null) { BackupStatusText.Text = "Import cancelled."; return; }
            if (new FileInfo(file.Path).Length > SettingsBackup.MaximumFileBytes)
                throw new InvalidOperationException("The settings backup is too large.");
            _pendingImportData = await File.ReadAllBytesAsync(file.Path);
            if (_isClosing) return;
            ImportBackupPassword.Header = "Password for " + Path.GetFileName(file.Path);
            ImportPasswordPanel.Visibility = Visibility.Visible;
            BackupStatusText.Text = "Enter the password used when this backup was exported.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or COMException)
        {
            BackupStatusText.Text = exception is InvalidOperationException ? exception.Message : "Could not open the backup file.";
        }
        finally
        {
            if (!_isClosing)
            {
                SharingPanel.IsEnabled = true;
                if (_pendingImportData is not null) ImportBackupPassword.Focus(FocusState.Programmatic);
            }
        }
    }
    private async void UnlockImport_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingImportData is null || _sharingCompletion is null || _isClosing) return;
        try
        {
            byte[] data = _pendingImportData;
            string password = ImportBackupPassword.Password;
            SettingsBackup.ValidatePassword(password);
            SharingPanel.IsEnabled = false;
            SettingsBundle bundle = await Task.Run(() => SettingsBackup.Import(data, password));
            if (_isClosing) return;
            CancelImportPassword_Click(sender, e);
            var typing = bundle.Typing;
            PaceSlider.Value = typing.Pace;
            RhythmSlider.Value = typing.Rhythm;
            MistakeSlider.Value = typing.MistakeChance;
            ReadyDelayBox.Value = typing.DelaySeconds;
            ApplyGeneralDraft(typing);
            _draftHotkeys = typing.GetHotkeys();
            UpdateHotkeyLabels();
            var cloud = bundle.Sharing ?? new SharingSettings();
            SharingAccount.Text = cloud.AccountId;
            SharingBucket.Text = cloud.BucketName;
            SharingAccessKey.Password = cloud.AccessKeyId;
            SharingSecretKey.Password = cloud.SecretAccessKey;
            SharingPublicUrl.Text = cloud.PublicBaseUrl;
            SharingJurisdiction.SelectedIndex = cloud.Jurisdiction switch { "eu" => 1, "us" => 2, "fedramp" => 3, _ => 0 };
            TranslationApiKey.Password = bundle.Translation.ApiKey;
            GroqApiKey.Password = (bundle.Summary ?? new SummarySettings()).ApiKey;
            GroqSecondaryKey.Password = (bundle.Summary ?? new SummarySettings()).SecondaryApiKey;
            TranslationRegion.Text = bundle.Translation.Region;
            _translationDraftLanguage = bundle.Translation.TargetLanguage;
            UnsavedChangesPrompt.Visibility = Visibility.Collapsed;
            SharingError.Text = "";
            BackupStatusText.Text = "Backup loaded for review. Save applies imported settings and keys; Back lets you discard them.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or COMException)
        {
            if (!_isClosing) BackupStatusText.Text = exception is InvalidOperationException
                ? exception.Message : "Could not import settings. Check the file and try again.";
        }
        finally { if (!_isClosing) SharingPanel.IsEnabled = true; }
    }

    internal void ActivateFromNotification()
    {
        if (!_isClosing && !_isSelectingRegion) ActivateExistingWindow();
    }

    internal void StartFromWindowsLogin()
    {
        if (_minimizeToTray) HideToTray();
    }

    private void ActivateExistingWindow()
    {
        if (_isClosing) return;
        _isInTray = false;
        _regionSelector?.Cancel();
        ShowWindow(_windowHandle, IsIconic(_windowHandle) ? 9 : 5);
        Activate();
    }

    private TypingSettings CaptureTypingSettings(bool useDraft = false)
    {
        var keys = useDraft ? _draftHotkeys : _activeHotkeys;
        return new()
        {
            Pace = PaceSlider.Value, Rhythm = RhythmSlider.Value, MistakeChance = MistakeSlider.Value,
            MinimizeToTray = MinimizeToTrayToggle.IsOn,
            RunOnStartup = RunOnStartupToggle.IsOn,
            HideFromScreenCapture = useDraft ? HideFromScreenCaptureToggle.IsOn : _hideFromScreenCapture,
            Theme = useDraft ? ThemeSelector.SelectedItem as string ?? "System" : _themeChoice,
            TrayClickAction = useDraft ? (TrayClickSelector.SelectedItem as TrayActionChoice)?.Id ?? "Main" : _trayClickAction,
            TrayDoubleClickAction = useDraft ? (TrayDoubleClickSelector.SelectedItem as TrayActionChoice)?.Id ?? "Main" : _trayDoubleClickAction,
            DelaySeconds = ReadyDelayBox.Value, HotkeyModifiers = keys["Typing"].Modifiers,
            HotkeyKey = keys["Typing"].Key, HotkeyLabel = keys["Typing"].Label,
            ScreenshotHotkey = keys["Screenshot"], ShareHotkey = keys["Share"],
            QrHotkey = keys["QR"], TextExtractHotkey = keys["Text Extract"], TranslateHotkey = keys["Translate"]
        };
    }

    private void ApplyTypingSettings(TypingSettings settings)
    {
        PaceSlider.Value = settings.Pace;
        RhythmSlider.Value = settings.Rhythm;
        MistakeSlider.Value = settings.MistakeChance;
        ReadyDelayBox.Value = settings.DelaySeconds;
        _minimizeToTray = settings.MinimizeToTray;
        _hideFromScreenCapture = settings.HideFromScreenCapture;
        ApplyCaptureProtection();
        _themeChoice = settings.Theme;
        _trayClickAction = settings.TrayClickAction;
        _trayDoubleClickAction = settings.TrayDoubleClickAction;
        _trayIcon?.CancelPendingClick();
        ApplyGeneralDraft(settings);
        _activeHotkeys = settings.GetHotkeys();
        _draftHotkeys = new(_activeHotkeys);
        UpdateHotkeyLabels();
        UpdateTrayIcon();
    }

    private void UpdateTrayIcon()
    {
        if (_windowHandle == IntPtr.Zero || _isClosing) return;
        if (!_minimizeToTray)
        {
            if (_isInTray) ActivateExistingWindow();
            _trayIcon?.Dispose();
            _trayIcon = null;
            return;
        }
        if (!_windowSubclassed) return;
        _trayIcon ??= new TrayIcon(_windowHandle, BuildTrayMenu,
            action => DispatcherQueue.TryEnqueue(() => HandleTrayCommand(action)), () => AppSurface.ActualTheme,
            () => (_trayClickAction, _trayDoubleClickAction));
        if (!_trayIcon.Show()) RegionSelectionText.Text = "Tray icon unavailable. The window will minimize normally.";
    }

    private List<TrayCommand> BuildTrayMenu()
    {
        bool ready = !_isRunning && !_isReadingClipboard && !_isSelectingRegion && !_isClosing;
        string? Shortcut(string action, int index) => _globalHotkeys?.IsRegistered(GlobalHotkeyId + index) == true
            ? _activeHotkeys[action].Label : null;
        return
        [
            new("Main", "Main", "\uE80F"),
            new("QR", "QR code", "\uED14", Shortcut("QR", 3), ready, true),
            new("Text Extract", "OCR / Text extract", "\uE8E9", Shortcut("Text Extract", 4), ready),
            new("Screenshot", "Screenshot", "\uE722", Shortcut("Screenshot", 1), ready),
            new("Share", "Instant share", "\uE72D", Shortcut("Share", 2), ready && _sharingSettings is not null),
            new("Settings", "Settings", "\uE713", Enabled: ready, SeparatorBefore: true),
            new("Exit", "Exit", "\uE711", SeparatorBefore: true)
        ];
    }

    private void HandleTrayCommand(string action)
    {
        if (_isClosing) return;
        if (action == "Exit") { _exitRequested = true; Close(); return; }
        if (action == "Main") { ActivateExistingWindow(); return; }
        if (action == "Captions") { Captions_Click(this, new RoutedEventArgs()); return; }
        if (action == "Typing") { if (_sharingCompletion is null) StartPauseButton_Click(this, new RoutedEventArgs()); return; }
        if (_isRunning || _isReadingClipboard || _isSelectingRegion) return;
        if (action == "Translate")
        {
            if (_sharingCompletion is not null) return;
            ActivateExistingWindow(); TranslateButton_Click(this, new RoutedEventArgs()); return;
        }
        if (action == "Settings") { ActivateExistingWindow(); SharingSettings_Click(this, new RoutedEventArgs()); return; }
        Button? tool = action switch { "QR" => QrToolButton, "Text Extract" => TextExtractToolButton,
            "Screenshot" => ScreenshotToolButton, "Share" => ShareToolButton, _ => null };
        if (tool is not null) RegionButton_Click(tool, new RoutedEventArgs());
    }

    private bool HideToTray()
    {
        if (!_minimizeToTray || _trayIcon?.IsVisible != true || _isClosing) return false;
        _isInTray = true;
        ShowWindow(_windowHandle, 0);
        return true;
    }

    private void SettingsSection_Click(object sender, RoutedEventArgs e) =>
        ScrollToSettingsSection((sender as ToggleButton)?.Tag as string ?? "General");

    private void ScrollToSettingsSection(string section)
    {
        FrameworkElement target = section switch { "Summary" => SummarySettingsSection, "Typing" => TypingSettingsSection, "Translation" => TranslationSettingsSection, "Cloud" => CloudSettingsSection, "Hotkeys" => HotkeysSettingsSection, _ => GeneralSettingsSection };
        double offset = target.TransformToVisual(SettingsSections).TransformPoint(new(0, 0)).Y;
        SettingsScroller.ChangeView(null, offset, null, disableAnimation: true);
        SetSettingsSectionSelection(section);
    }

    private void SetSettingsSectionSelection(string section)
    {
        TypingSettingsTab.IsChecked = section == "Typing";
        GeneralSettingsTab.IsChecked = section is "General" or "Backup";
        CloudSettingsTab.IsChecked = section == "Cloud";
        HotkeysSettingsTab.IsChecked = section == "Hotkeys";
        TranslationSettingsTab.IsChecked = section == "Translation";
        SummarySettingsTab.IsChecked = section == "Summary";
    }

    private void SettingsScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (SummarySettingsSection is null || SummarySettingsTab is null || GeneralSettingsSection is null || GeneralSettingsTab is null || TranslationSettingsSection is null || TranslationSettingsTab is null || HotkeysSettingsSection is null || SettingsSections is null || TypingSettingsTab is null || HotkeysSettingsTab is null) return;
        double typingOffset = TypingSettingsSection.TransformToVisual(SettingsSections).TransformPoint(new(0, 0)).Y;
        double cloudOffset = CloudSettingsSection.TransformToVisual(SettingsSections).TransformPoint(new(0, 0)).Y;
        double keysOffset = HotkeysSettingsSection.TransformToVisual(SettingsSections).TransformPoint(new(0, 0)).Y;
        double translationOffset = TranslationSettingsSection.TransformToVisual(SettingsSections).TransformPoint(new(0, 0)).Y;
        double summaryOffset = SummarySettingsSection.TransformToVisual(SettingsSections).TransformPoint(new(0, 0)).Y;
        bool atEnd = SettingsScroller.VerticalOffset > 0 && SettingsScroller.VerticalOffset >= SettingsScroller.ScrollableHeight - 1;
        SetSettingsSectionSelection(atEnd || SettingsScroller.VerticalOffset >= summaryOffset - 10 ? "Summary" : SettingsScroller.VerticalOffset >= translationOffset - 10 ? "Translation" : SettingsScroller.VerticalOffset >= keysOffset - 10 ? "Hotkeys"
            : SettingsScroller.VerticalOffset >= cloudOffset - 10 ? "Cloud" : SettingsScroller.VerticalOffset >= typingOffset - 10 ? "Typing" : "General");
    }

    private void ShareHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryPanel.Visibility == Visibility.Visible)
        {
            HistoryPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var entries = ShareHistoryStore.Load();
        ShareHistoryList.ItemsSource = entries;
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = Visibility.Visible;
    }

    private void HistoryClose_Click(object sender, RoutedEventArgs e) => HistoryPanel.Visibility = Visibility.Collapsed;

    private async void HistoryOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isSelectingRegion || _isClosing || (sender as Button)?.Tag is not string value
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? url) || url.Scheme != "https"
            || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0) return;
        try
        {
            if (!await Launcher.LaunchUriAsync(url)) RegionSelectionText.Text = "Could not open the shared URL in your browser.";
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        { RegionSelectionText.Text = "Could not open the shared URL in your browser."; }
    }

    private void HistoryCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isSelectingRegion || _isClosing || (sender as Button)?.Tag is not string url) return;
        try
        {
            var content = new DataPackage();
            content.SetText(url);
            Clipboard.SetContent(content);
            Clipboard.Flush();
            StatusText.Text = "Link copied";
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or UnauthorizedAccessException)
        { RegionSelectionText.Text = "Could not copy the history URL. Try again."; }
    }

    private async Task CompleteScreenshotAsync(CapturedScreen capture, byte[] png, bool copyResult)
    {
        string title, detail;
        if (copyResult)
        {
            var stream = new InMemoryRandomAccessStream();
            try
            {
                using (var writer = new DataWriter(stream))
                {
                    writer.WriteBytes(png);
                    await writer.StoreAsync();
                    writer.DetachStream();
                }
                if (_isClosing) { stream.Dispose(); return; }
                stream.Seek(0);
                var content = new DataPackage();
                content.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                if (!ApplyToolResult("Screenshot", null, content, true))
                {
                    stream.Dispose();
                    return;
                }
                _screenshotClipboardStream?.Dispose();
                _screenshotClipboardStream = stream;
                // Materialize the image so it can still be pasted after Auto Typer closes.
                try { Clipboard.Flush(); }
                catch (Exception exception) when (exception is COMException or InvalidOperationException)
                { Debug.WriteLine(exception); }
            }
            catch { stream.Dispose(); throw; }
            title = "Screenshot copied";
            detail = $"{capture.Width} × {capture.Height} px · Ready to paste from your clipboard";
        }
        else
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id)
            {
                SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"AutoTyper-Screenshot-{DateTime.Now:yyyy-MM-dd-HHmmss-fff}",
                DefaultFileExtension = ".png"
            };
            picker.FileTypeChoices.Add("PNG image", new List<string> { ".png" });
            var file = await picker.PickSaveFileAsync();
            if (_isClosing) return;
            if (file is null)
            {
                RegionSelectionText.Text = "Screenshot: save cancelled";
                return;
            }
            await File.WriteAllBytesAsync(file.Path, png);
            if (_isClosing) return;
            title = "Screenshot saved";
            detail = $"{capture.Width} × {capture.Height} px · {Path.GetFileName(file.Path)}";
            RegionSelectionText.Text = $"Screenshot saved: {file.Path}";
        }
        StatusText.Text = copyResult ? "Image copied" : "Image saved";
        bool notified = await App.ScreenshotNotifications.ShowAsync(title, detail, capture);
        if (!_isClosing && !notified) RegionSelectionText.Text += " · " + App.ScreenshotNotifications.UnavailableReason;
    }

    private bool ApplyToolResult(string tool, string? text, DataPackage clipboardResult, bool copyResult)
    {
        if (text is not null)
        {
            SourceBox.Text = text;
            SourceBox.Select(0, 0);
            StatusText.Text = tool == "QR" ? "QR text loaded" : "Text loaded";
            ActivityText.Text = string.Empty;
        }
        RegionSelectionText.Text = text is not null ? $"{tool}: text loaded into input" : $"{tool}: captured";
        if (!copyResult) return true;
        try
        {
            Clipboard.SetContent(clipboardResult);
            RegionSelectionText.Text += " · Copied to clipboard";
            return true;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException
            or UnauthorizedAccessException)
        {
            RegionSelectionText.Text += " · Could not copy to clipboard";
            Debug.WriteLine(exception);
            return false;
        }
    }

    public MainWindow() : this(TypingSettingsStore.Load() with { RunOnStartup = StartupRegistration.IsEnabled }, true) { }

    internal MainWindow(TypingSettings initialSettings, bool registerHotkeys,
        SummarySettings? initialSummary = null, Func<string,string,CancellationToken,Task<string>>? summaryRequest = null)
    {
        _persistPreferences = registerHotkeys;
        _audioPersistSettings = registerHotkeys;
        _windowSubclassProc = WindowSubclassProc;
        InitializeComponent();
        _summaryRequestOverride = summaryRequest;
        _summarySettings = initialSummary ?? (registerHotkeys ? SummarySettingsStore.Load() : new());
        MainSummaryPanel.Expanding += (_, _) => MainSummaryPanel.Width = 300;
        MainSummaryPanel.Collapsed += (_, _) => MainSummaryPanel.Width = 160;
        InitializeGeneralSettings();
        SettingsScroller.SizeChanged += (_, _) => UpdateSettingsEndPadding();
        InitializeAudioPanel();
        _translationSettings = registerHotkeys ? TranslationSettingsStore.Load() : new();
        TranslationLanguage.ItemsSource = TranslationSettings.Languages;
        TranslationLanguage.SelectedItem = _translationSettings.TargetLanguage;
        ApplyTypingSettings(initialSettings);
        _sharingSettings = registerHotkeys ? SharingSettingsStore.Load() : null;
        SetRegionToolsEnabled(true);
        RegisterGlobalHotkey(registerHotkeys);
        UpdateTrayIcon();
        AppSurface.Loaded += (_, _) => UpdateTitleBarTheme();
        AppSurface.ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateTitleBarTheme);
        UpdateTitleBarTheme();
        UpdateOptionLabels();
        UpdateProgressText();
        Closed += MainWindow_Closed;
    }

    private TranslationSettings CaptureTranslationSettings() => new()
    {
        ApiKey = TranslationApiKey.Password.Trim(), Region = TranslationRegion.Text.Trim().ToLowerInvariant(),
        TargetLanguage = _translationDraftLanguage
    };

    private void TranslationLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TranslationLanguage.SelectedItem is not string language || language == _translationSettings.TargetLanguage) return;
        var updated = _translationSettings with { TargetLanguage = language };
        try
        {
            if (_persistPreferences) TranslationSettingsStore.Save(updated);
            _translationSettings = updated;
            SetRegionToolsEnabled(!_isRunning && !_isReadingClipboard && !_isSelectingRegion);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            TranslationLanguage.SelectedItem = _translationSettings.TargetLanguage;
            StatusText.Text = "Could not save the translation language. Try again.";
        }
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isReadingClipboard || _isSelectingRegion || _isClosing) return;
        if (!_translationSettings.IsConfigured)
        {
            RegionSelectionText.Text = "Configure Microsoft Translator in Settings → Translation first.";
            return;
        }
        _isReadingClipboard = true;
        _translationCancellation = new CancellationTokenSource();
        CancellationToken token = _translationCancellation.Token;
        TranslationSettings settings = _translationSettings;
        bool copyResult = CopyResultToggle.IsOn;
        SetRegionToolsEnabled(false);
        ClipboardButton.IsEnabled = StartPauseButton.IsEnabled = false;
        SourceBox.IsReadOnly = true;
        StopButton.IsEnabled = true;
        EndHotkeyRecording();
        try
        {
            var clipboard = Clipboard.GetContent();
            if (!clipboard.Contains(StandardDataFormats.Text))
                throw new InvalidOperationException("The clipboard does not contain text to translate.");
            string original = await clipboard.GetTextAsync();
            if (_isClosing) return;
            token.ThrowIfCancellationRequested();
            StatusText.Text = "Translating…";
            RegionSelectionText.Text = $"Translating clipboard text into {settings.TargetLanguage}…";
            TypingProgressBar.IsIndeterminate = true;
            string translated = await new MicrosoftTranslator(TranslationHttpClient).TranslateAsync(original, settings, token);
            if (_isClosing) return;
            token.ThrowIfCancellationRequested();
            string result = MicrosoftTranslator.FormatResult(original, translated, settings.TargetLanguage);
            var content = new DataPackage();
            content.SetText(result);
            ApplyToolResult("Translation", result, content, copyResult);
            StatusText.Text = "Translation loaded";
            ActivateExistingWindow();
        }
        catch (OperationCanceledException)
        {
            if (!_isClosing)
            {
                StatusText.Text = token.IsCancellationRequested ? "Translation cancelled" : "Translation timed out";
                RegionSelectionText.Text = token.IsCancellationRequested ? "Translation cancelled." : "Microsoft Translator took too long. Try again.";
            }
        }
        catch (InvalidOperationException exception)
        {
            if (!_isClosing) { StatusText.Text = "Translation unavailable"; RegionSelectionText.Text = exception.Message; }
        }
        catch (Exception exception) when (exception is HttpRequestException or COMException or UnauthorizedAccessException or IOException)
        {
            if (!_isClosing) { StatusText.Text = "Could not translate"; RegionSelectionText.Text = "Check your connection and clipboard, then try again."; }
        }
        finally
        {
            _translationCancellation.Dispose();
            _translationCancellation = null;
            _isReadingClipboard = false;
            if (!_isClosing)
            {
                TypingProgressBar.IsIndeterminate = false;
                SourceBox.IsReadOnly = false;
                StopButton.IsEnabled = false;
                SetRegionToolsEnabled(true);
                ClipboardButton.IsEnabled = StartPauseButton.IsEnabled = true;
                RecordHotkeyButton.IsEnabled = _windowSubclassed;
            }
        }
    }

    private async void ClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isReadingClipboard || _isSelectingRegion || _isClosing) return;

        _isReadingClipboard = true;
        SetRegionToolsEnabled(false);
        ClipboardButton.IsEnabled = false;
        StartPauseButton.IsEnabled = false;
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                StatusText.Text = "The clipboard does not contain text.";
                return;
            }

            string text = await content.GetTextAsync();
            if (_isClosing) return;
            if (string.IsNullOrEmpty(text))
            {
                StatusText.Text = "The clipboard is empty.";
                return;
            }

            SourceBox.Text = text;
            SourceBox.Select(0, 0);
            ActivityText.Text = string.Empty;
            StatusText.Text = "Clipboard text loaded";
        }
        catch (Exception exception) when (exception is COMException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            if (!_isClosing) StatusText.Text = "Could not read the clipboard. Try again.";
        }
        finally
        {
            _isReadingClipboard = false;
            if (!_isClosing)
            {
                ClipboardButton.IsEnabled = !_isRunning;
                StartPauseButton.IsEnabled = true;
                SetRegionToolsEnabled(true);
            }
        }
    }

    private async void StartPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isReadingClipboard || _isSelectingRegion || _isClosing) return;
        if (_isRunning)
        {
            if (_isPaused)
            {
                _isPaused = false;
                _resumeSignal?.TrySetResult(true);
                _resumeSignal = null;
                StartPauseButton.Content = "Pause";
                StatusText.Text = _isCountingDown ? "Getting ready" : "Typing";
            }
            else
            {
                _isPaused = true;
                _resumeSignal = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                StartPauseButton.Content = "Resume";
                StatusText.Text = "Paused";
            }

            return;
        }

        _source = SourceBox.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (string.IsNullOrEmpty(_source))
        {
            StatusText.Text = "Enter some practice text first.";
            UpdateProgressText();
            return;
        }

        if (!double.IsFinite(ReadyDelayBox.Value)
            || ReadyDelayBox.Value < ReadyDelayBox.Minimum
            || ReadyDelayBox.Value > ReadyDelayBox.Maximum)
        {
            StatusText.Text = "Enter a ready delay between 0 and 10 seconds.";
            return;
        }

        BuildSourceTextOffsets();
        _typedLength = 0;
        _corrections.Clear();
        _sourceIndex = 0;
        _caret = 0;
        _rhythmGroupCharactersRemaining = 0;
        _hasProgress = true;
        SourceBox.IsReadOnly = true;
        ClipboardButton.IsEnabled = false;
        SourceBox.IsHitTestVisible = false;
        ReadyDelayBox.IsEnabled = false;
        _targetWindow = IntPtr.Zero;
        _typingFocusTarget = null;
        _focusPaused = false;
        RecordHotkeyButton.IsEnabled = false;
        _isRunning = true;
        SetRegionToolsEnabled(false);
        RegionTools.Opacity = 0.5;
        UpdateCurrentCharacter(_sourceIndex);
        UpdateProgressText();
        _isPaused = false;
        _cancellation = new CancellationTokenSource();
        StartPauseButton.Content = "Pause";
        StopButton.IsEnabled = true;
        StatusText.Text = "Typing";
        ActivityText.Text = string.Empty;

        try
        {
            await RunSimulationAsync(_cancellation.Token);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            StatusText.Text = "Stopped";
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = exception.Message;
            ActivityText.Text = "Stopped";
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _resumeSignal = null;
            _isRunning = false;
            SetRegionToolsEnabled(true);
            RegionTools.Opacity = 1;
            _isPaused = false;
            _isCountingDown = false;
            SourceBox.IsReadOnly = false;
            ClipboardButton.IsEnabled = true;
            SourceBox.IsHitTestVisible = true;
            SourceBox.SelectionLength = 0;
            ReadyDelayBox.IsEnabled = true;
            RecordHotkeyButton.IsEnabled = _windowSubclassed;
            _targetWindow = IntPtr.Zero;
        _typingFocusTarget = null;
        _focusPaused = false;
            StartPauseButton.Content = "Start";
            StopButton.IsEnabled = false;
            UpdateProgressText();
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = PinButton.IsChecked == true;
            ToolTipService.SetToolTip(PinButton, presenter.IsAlwaysOnTop ? "Unpin window" : "Keep window on top");
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _shareCancellation?.Cancel();
        _translationCancellation?.Cancel();
        _ocrCancellation?.Cancel();
        _cancellation?.Cancel();
        _resumeSignal?.TrySetResult(true);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isReadingClipboard || _isSelectingRegion)
        {
            return;
        }

        _typedLength = 0;
        _hasProgress = false;
        _corrections.Clear();
        _sourceIndex = 0;
        _caret = 0;
        SourceBox.SelectionLength = 0;
        ActivityText.Text = string.Empty;
        StatusText.Text = "Ready";
        UpdateProgressText();
    }

    private async Task RunSimulationAsync(CancellationToken cancellationToken)
    {
        await RunReadyCountdownAsync(cancellationToken);
        await WaitIfPausedAsync(cancellationToken);
        _typingFocusTarget = TypingFocusTarget.Capture(_windowHandle);
        _targetWindow = NativeKeyboard.GetForegroundWindow();
        if (_targetWindow == IntPtr.Zero || _targetWindow == _windowHandle)
        {
            throw new InvalidOperationException("Focus another app during the ready delay, then start again.");
        }

        while (_sourceIndex < _source.Length || _corrections.Count > 0)
        {
            await WaitIfPausedAsync(cancellationToken);

            int correctionIndex = _corrections.FindIndex(
                correction => correction.DueAtSourceIndex <= _sourceIndex);
            if (correctionIndex >= 0 || _sourceIndex >= _source.Length)
            {
                int index = correctionIndex >= 0 ? correctionIndex : 0;
                PendingCorrection correction = _corrections[index];
                _corrections.RemoveAt(index);
                await CorrectMistakeAsync(correction, cancellationToken);
                continue;
            }

            char next = _source[_sourceIndex];
            UpdateCurrentCharacter(_sourceIndex);
            await DelayForCharacterAsync(next, cancellationToken);
            await WaitIfPausedAsync(cancellationToken);

            if (char.IsLetter(next) && _random.NextDouble() < MistakeSlider.Value)
            {
                char typo = GetNearbyLetter(next);
                await InsertAtCaretAsync(typo, cancellationToken);
                int typoPosition = _caret - 1;
                int delayCharacters = _random.NextDouble() < 0.7
                    ? 1
                    : _random.Next(4, 10);
                _corrections.Add(new PendingCorrection(
                    typoPosition,
                    next,
                    _sourceIndex + delayCharacters));
                ActivityText.Text = "A small typo";
            }
            else
            {
                await InsertAtCaretAsync(next, cancellationToken);
                ActivityText.Text = GetActivityFor(next);
            }

            _sourceIndex++;
            UpdateCurrentCharacter(_sourceIndex);
            UpdateProgressText();
        }

        StatusText.Text = "Complete";
        ActivityText.Text = "All corrections made";
    }

    private async Task RunReadyCountdownAsync(CancellationToken cancellationToken)
    {
        int remainingMilliseconds = (int)Math.Round(ReadyDelayBox.Value * 1000);
        if (remainingMilliseconds <= 0)
        {
            return;
        }

        _isCountingDown = true;
        CountdownOverlay.Visibility = Visibility.Visible;
        try
        {
            while (remainingMilliseconds > 0)
            {
                await WaitIfPausedAsync(cancellationToken);
                int remainingSeconds = (int)Math.Ceiling(remainingMilliseconds / 1000d);
                StatusText.Text = $"Get ready ({remainingSeconds})";
                CountdownOverlayText.Text = remainingSeconds.ToString();
                ProgressText.Text =
                    $"{remainingMilliseconds / 1000d:0.0}s";

                int delay = Math.Min(100, remainingMilliseconds);
                await Task.Delay(delay, cancellationToken);
                remainingMilliseconds -= delay;
            }
        }
        finally
        {
            _isCountingDown = false;
            CountdownOverlay.Visibility = Visibility.Collapsed;
        }

        StatusText.Text = "Typing";
        UpdateProgressText();
    }

    private void SourceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSummaryButton();
        UpdateSummarySourceStatus();
        if (!_isRunning)
        {
            _hasProgress = false;
            _sourceIndex = 0;
            UpdateProgressText();
        }
    }

    private void UpdateProgressText()
    {
        if (ProgressText is null || SourceBox is null)
        {
            return;
        }

        int total = _isRunning || _hasProgress
            ? _source.Length
            : SourceBox.Text.Replace("\r\n", "\n").Length;
        int completed = _isRunning || _hasProgress ? Math.Min(_sourceIndex, total) : 0;
        int percentage = total == 0 ? 0 : completed * 100 / total;
        ProgressText.Text = $"{completed}/{total} · {percentage}%";
        TypingProgressBar.Value = total == 0 ? 0 : completed * 100d / total;
    }

    private async Task CorrectMistakeAsync(
        PendingCorrection correction,
        CancellationToken cancellationToken)
    {
        ActivityText.Text = "Noticed a typo";
        UpdateCurrentCharacter(correction.Position);
        await Task.Delay(_random.Next(900, 1801), cancellationToken);
        await WaitIfPausedAsync(cancellationToken);

        int targetCaret = correction.Position + 1;
        ActivityText.Text = _caret == targetCaret
            ? "Fixing the last key"
            : "Navigating to fix a typo";

        while (_caret > targetCaret)
        {
            await DelayForKeyAsync(cancellationToken);
            await WaitIfPausedAsync(cancellationToken);
            await SendExternalKeyAsync(0x25, cancellationToken); // Left arrow
            _caret--;
        }

        await Task.Delay(_random.Next(250, 601), cancellationToken);
        await WaitIfPausedAsync(cancellationToken);

        await DelayForKeyAsync(cancellationToken);
        await WaitIfPausedAsync(cancellationToken);
        await SendExternalKeyAsync(0x08, cancellationToken); // Backspace
        _typedLength--;
        _caret--;
        ActivityText.Text = "Backspace";

        await DelayForKeyAsync(cancellationToken);
        await WaitIfPausedAsync(cancellationToken);
        await InsertAtCaretAsync(correction.CorrectCharacter, cancellationToken);
        ActivityText.Text = "Typo corrected";

        while (_caret < _typedLength)
        {
            await DelayForKeyAsync(cancellationToken);
            await WaitIfPausedAsync(cancellationToken);
            await SendExternalKeyAsync(0x27, cancellationToken); // Right arrow
            _caret++;
        }
        UpdateCurrentCharacter(_sourceIndex);
    }

    private async Task DelayForCharacterAsync(char character, CancellationToken cancellationToken)
    {
        int baseDelay = 75;
        if (char.IsPunctuation(character))
        {
            baseDelay += _random.Next(100, 261);
        }
        else if (char.IsWhiteSpace(character))
        {
            baseDelay += _random.Next(15, 71);
        }

        await Task.Delay(ScaleDelay(baseDelay), cancellationToken);
    }

    private async Task DelayForKeyAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(ScaleDelay(100), cancellationToken);
    }

    private int ScaleDelay(int milliseconds)
    {
        double rhythm = RhythmSlider.Value;
        if (_rhythmGroupCharactersRemaining == 0)
        {
            _rhythmGroupCharactersRemaining = _random.Next(3, 8);
            _rhythmGroupScale = 1 + ((_random.NextDouble() * 2) - 1) * 0.35 * rhythm;
        }

        _rhythmGroupCharactersRemaining--;
        int jitter = (int)(milliseconds * 0.12 * rhythm);
        int variedDelay = (int)(milliseconds * _rhythmGroupScale);
        if (jitter > 0)
        {
            variedDelay += _random.Next(-jitter, jitter + 1);
        }

        if (rhythm > 0 && _random.NextDouble() < rhythm * 0.025)
        {
            variedDelay += _random.Next(180, 451);
        }

        return Math.Clamp((int)(variedDelay / PaceSlider.Value), 15, 1200);
    }

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_isPaused)
            {
                TaskCompletionSource<bool> signal = _resumeSignal
                    ?? throw new InvalidOperationException("The pause signal is unavailable.");
                await signal.Task.WaitAsync(cancellationToken);
                continue;
            }

            if (_typingFocusTarget is not null && !_typingFocusTarget.IsFocused())
            {
                _focusPaused = true;
                StatusText.Text = "Paused — return to the original text input";
                await Task.Delay(100, cancellationToken);
                continue;
            }
            if (_focusPaused)
            {
                _focusPaused = false;
                StatusText.Text = "Typing";
            }

            // Do not turn simulated keys into shortcuts while the hotkey is held.
            if ((GetPressedHotkeyModifiers() & ~ModNoRepeat) != 0)
            {
                await Task.Delay(20, cancellationToken);
                continue;
            }

            return;
        }
    }

    private async Task InsertAtCaretAsync(char character, CancellationToken cancellationToken)
    {
        await WaitIfPausedAsync(cancellationToken);
        NativeKeyboard.SendCharacter(character);
        _typedLength++;
        _caret++;
    }

    private async Task SendExternalKeyAsync(ushort virtualKey, CancellationToken cancellationToken)
    {
        await WaitIfPausedAsync(cancellationToken);
        NativeKeyboard.SendKey(virtualKey);
    }

    private void BuildSourceTextOffsets()
    {
        // TextBox may store CRLF while simulated Enter is one source character.
        string text = SourceBox.Text;
        _sourceTextOffsets = new int[_source.Length + 1];
        int sourceIndex = 0;
        for (int textIndex = 0; textIndex < text.Length; textIndex++)
        {
            _sourceTextOffsets[sourceIndex++] = textIndex;
            if (text[textIndex] == '\r' && textIndex + 1 < text.Length && text[textIndex + 1] == '\n')
            {
                textIndex++;
            }
        }
        _sourceTextOffsets[sourceIndex] = text.Length;
    }

    private void UpdateCurrentCharacter(int sourceIndex)
    {
        int index = Math.Clamp(sourceIndex, 0, _source.Length);
        int textOffset = _sourceTextOffsets[index];
        int length = index < _source.Length ? _sourceTextOffsets[index + 1] - textOffset : 0;
        // Select without focusing the control so typing stays in the target app.
        SourceBox.Select(textOffset, length);
    }

    private char GetNearbyLetter(char character)
    {
        bool isUpper = char.IsUpper(character);
        char lower = char.ToLowerInvariant(character);
        double keyX = 0;
        int keyY = 0;
        bool foundKey = false;

        for (int rowIndex = 0; rowIndex < KeyboardRows.Length; rowIndex++)
        {
            int column = KeyboardRows[rowIndex].Keys.IndexOf(lower);
            if (column < 0)
            {
                continue;
            }

            keyX = column + KeyboardRows[rowIndex].Offset;
            keyY = rowIndex;
            foundKey = true;
            break;
        }

        if (!foundKey)
        {
            return character;
        }

        List<char> neighbors = new();
        for (int rowIndex = 0; rowIndex < KeyboardRows.Length; rowIndex++)
        {
            (string keys, double offset) = KeyboardRows[rowIndex];
            for (int column = 0; column < keys.Length; column++)
            {
                char candidate = keys[column];
                double dx = column + offset - keyX;
                int dy = rowIndex - keyY;
                if (candidate != lower && dx * dx + dy * dy <= 1.65)
                {
                    neighbors.Add(candidate);
                }
            }
        }

        if (neighbors.Count == 0)
        {
            return character;
        }

        char result = neighbors[_random.Next(neighbors.Count)];
        return isUpper ? char.ToUpperInvariant(result) : result;
    }

    private static string GetActivityFor(char character)
    {
        if (character == ' ')
        {
            return "Space";
        }

        if (character == '\n')
        {
            return "Return";
        }

        return char.IsPunctuation(character) ? "Brief pause" : "Typing";
    }

    private void PaceSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateOptionLabels();
    }

    private void MistakeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateOptionLabels();
    }

    private void RhythmSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateOptionLabels();
    }

    private void ReadyDelayBox_ValueChanged(object sender, NumberBoxValueChangedEventArgs e)
    {
        UpdateOptionLabels();
    }

    private Dictionary<string, TextBox> HotkeyInputs => new()
    {
        ["Typing"] = HotkeyInput, ["Screenshot"] = ScreenshotHotkeyInput, ["Share"] = ShareHotkeyInput,
        ["QR"] = QrHotkeyInput, ["Text Extract"] = TextExtractHotkeyInput, ["Translate"] = TranslateHotkeyInput
    };

    private void UpdateHotkeyLabels()
    {
        foreach (var entry in HotkeyInputs) entry.Value.Text = _draftHotkeys[entry.Key].Label;
    }

    private void EndHotkeyRecording()
    {
        _isRecordingHotkey = false;
        _recordingInput = null;
        UpdateHotkeyLabels();
    }

    private void RecordHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        EndHotkeyRecording();
        _recordingAction = (sender as Button)?.Tag as string ?? "Typing";
        _recordingInput = HotkeyInputs[_recordingAction];
        _isRecordingHotkey = true;
        _recordingInput.Text = "Press a combination";
        HotkeyStatusText.Text = "Hold a modifier and press a key. Esc cancels.";
        _recordingInput.Focus(FocusState.Programmatic);
    }

    private void HotkeyInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isRecordingHotkey || !ReferenceEquals(sender, _recordingInput)) return;
        e.Handled = true;
        if (e.Key == VirtualKey.Escape)
        {
            EndHotkeyRecording();
            HotkeyStatusText.Text = "Recording cancelled.";
            return;
        }
        uint modifiers = GetPressedHotkeyModifiers(messageState: true);
        if (IsModifierKey(e.Key))
        {
            _recordingInput!.Text = FormatModifiers(modifiers) + "+…";
            return;
        }
        if ((modifiers & 0xf) == 0)
        {
            HotkeyStatusText.Text = "Use Ctrl, Alt, Shift or Windows with another key.";
            return;
        }
        if (_draftHotkeys.Any(h => h.Key != _recordingAction && (h.Value.Modifiers & 0xf) == (modifiers & 0xf) && h.Value.Key == (uint)e.Key))
        {
            HotkeyStatusText.Text = "That shortcut is already assigned to another action. Choose a different combination.";
            return;
        }
        _draftHotkeys[_recordingAction] = new(modifiers, (uint)e.Key, FormatHotkey(modifiers, e.Key));
        EndHotkeyRecording();
        HotkeyStatusText.Text = "Shortcut recorded. Save to apply.";
    }

    private static uint GetPressedHotkeyModifiers(bool messageState = false)
    {
        bool Down(int key) => messageState ? GetKeyState(key) < 0 : IsKeyDown(key);
        uint modifiers = ModNoRepeat;
        if (Down(VkControl))
        {
            modifiers |= ModControl;
        }

        if (Down(VkAlt))
        {
            modifiers |= ModAlt;
        }

        if (Down(VkShift))
        {
            modifiers |= ModShift;
        }

        if (Down(VkLeftWindows) || Down(VkRightWindows))
        {
            modifiers |= ModWindows;
        }

        return modifiers;
    }

    private void UpdateOptionLabels()
    {
        if (PaceValueText is not null && PaceSlider is not null)
        {
            PaceValueText.Text = $"{PaceSlider.Value:0.0}×";
        }

        if (MistakeValueText is not null && MistakeSlider is not null)
        {
            MistakeValueText.Text = $"{MistakeSlider.Value:P0}";
        }

        if (RhythmValueText is not null && RhythmSlider is not null)
        {
            RhythmValueText.Text = $"{RhythmSlider.Value:P0}";
        }

    }

    private void UpdateTitleBarTheme()
    {
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        bool isDark = AppSurface.ActualTheme == ElementTheme.Dark;
        Windows.UI.Color background =
            ((Microsoft.UI.Xaml.Media.SolidColorBrush)AppSurface.Background).Color;
        Windows.UI.Color foreground = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = isDark
            ? Windows.UI.Color.FromArgb(255, 55, 55, 55)
            : Windows.UI.Color.FromArgb(255, 225, 225, 225);
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = background;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void RegisterGlobalHotkey(bool enabled = true)
    {
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ApplyCaptureProtection();
        Microsoft.UI.Windowing.AppWindow appWindow =
            Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
                Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle));
        double windowScale = Math.Max(96u, GetDpiForWindow(_windowHandle)) / 96d;
        appWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Ceiling(1280 * windowScale), (int)Math.Ceiling(820 * windowScale)));
        appWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AutoTyper.ico"));

        if (!SetWindowSubclass(
                _windowHandle,
                _windowSubclassProc,
                HotkeySubclassId,
                IntPtr.Zero))
        {
            HotkeyStatusText.Text =
                $"Global hotkey unavailable (Windows error {Marshal.GetLastWin32Error()}).";
            RecordHotkeyButton.IsEnabled = false;
            return;
        }

        _windowSubclassed = true;
        if (!enabled) { RecordHotkeyButton.IsEnabled = false; return; }
        _globalHotkeys = new((id, modifiers, key) => RegisterHotKey(_windowHandle, id, modifiers, key),
            id => UnregisterHotKey(_windowHandle, id));
        RegisterActiveHotkeys();
    }

    private List<HotkeyRegistration> BuildHotkeyRegistrations(Dictionary<string, HotkeyGesture> hotkeys, bool? translationConfigured = null) =>
        new[] { "Typing", "Screenshot", "Share", "QR", "Text Extract", "Translate" }
            .Select((action, index) => new HotkeyRegistration(GlobalHotkeyId + index, action, hotkeys[action]))
            .Where(binding => binding.Action != "Translate" || (translationConfigured ?? _translationSettings.IsConfigured)).ToList();

    private void RegisterActiveHotkeys()
    {
        if (!_windowSubclassed || _globalHotkeys is null) return;
        var failed = _globalHotkeys.RegisterAvailable(BuildHotkeyRegistrations(_activeHotkeys));
        HotkeyStatusText.Text = failed.Count == 0 ? "All global shortcuts are active."
            : "Unavailable: " + string.Join(", ", failed) + ". Another app may be using these shortcuts. Choose different combinations and Save.";
        RefreshActionTooltips();
    }

    private void RefreshActionTooltips()
    {
        string Shortcut(string action, int index) => _activeHotkeys[action].Label
            + (_globalHotkeys?.IsRegistered(GlobalHotkeyId + index) == true ? "" : " · unavailable");
        ToolTipService.SetToolTip(StartPauseButton, $"Start / pause typing ({Shortcut("Typing", 0)})");
        ToolTipService.SetToolTip(ScreenshotToolButton, $"Screenshot ({Shortcut("Screenshot", 1)}) — save PNG, or copy image when clipboard is on.");
        ToolTipService.SetToolTip(ShareToolButton, _sharingSettings is null
            ? "Configure your Cloudflare bucket in Settings to enable sharing."
            : $"Instant share ({Shortcut("Share", 2)}) — shared screenshots are saved for 14 days.");
        ToolTipService.SetToolTip(QrToolButton, $"Scan QR code ({Shortcut("QR", 3)})");
        ToolTipService.SetToolTip(TextExtractToolButton, $"Extract English text ({Shortcut("Text Extract", 4)})");
        ToolTipService.SetToolTip(TranslateButton, !_translationSettings.IsConfigured
            ? "Translate clipboard text — configure Microsoft Translator in Settings → Translation"
            : $"Translate clipboard text into {_translationSettings.TargetLanguage} ({Shortcut("Translate", 5)}) with Microsoft Translator");
    }

    private static bool IsModifierKey(VirtualKey key)
    {
        return key is VirtualKey.Control
            or VirtualKey.Shift
            or VirtualKey.Menu
            or VirtualKey.LeftControl
            or VirtualKey.RightControl
            or VirtualKey.LeftShift
            or VirtualKey.RightShift
            or VirtualKey.LeftMenu
            or VirtualKey.RightMenu
            or VirtualKey.LeftWindows
            or VirtualKey.RightWindows;
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return GetAsyncKeyState(virtualKey) < 0;
    }

    private static string FormatHotkey(uint modifiers, VirtualKey key)
    {
        string modifiersText = FormatModifiers(modifiers);
        string keyName = key switch
        {
            VirtualKey.Space => "Space",
            VirtualKey.Enter => "Enter",
            VirtualKey.Back => "Backspace",
            _ => key.ToString()
        };
        return $"{modifiersText}+{keyName}";
    }

    private static string FormatModifiers(uint modifiers)
    {
        List<string> parts = new();
        if ((modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModWindows) != 0)
        {
            parts.Add("Win");
        }

        return string.Join("+", parts);
    }

    private IntPtr WindowSubclassProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        IntPtr refData)
    {
        // Apply a DPI-scaled native resize limit, including when shortcuts are disabled.
        if (message == 0x0024 && lParam != IntPtr.Zero) // WM_GETMINMAXINFO
        {
            IntPtr result = DefSubclassProc(hWnd, message, wParam, lParam);
            var limits = Marshal.PtrToStructure<WindowSizeLimits>(lParam);
            double scale = Math.Max(96u, GetDpiForWindow(hWnd)) / 96d;
            int minimumWidth = AudioPanel.Visibility == Visibility.Visible ? 1200 : 940;
            limits.MinimumTrack.X = Math.Max(limits.MinimumTrack.X, (int)Math.Ceiling(minimumWidth * scale));
            limits.MinimumTrack.Y = Math.Max(limits.MinimumTrack.Y, (int)Math.Ceiling(600 * scale));
            Marshal.StructureToPtr(limits, lParam, false);
            return result;
        }
        if (_trayIcon?.ProcessMessage(message, wParam, lParam) == true) return IntPtr.Zero;
        if (!_exitRequested && !_isClosing && _minimizeToTray && _trayIcon?.IsVisible == true)
        {
            if (message == 0x0010 || (message == 0x0112 && (wParam.ToInt64() & 0xfff0) == 0xf020))
            {
                HideToTray();
                return IntPtr.Zero;
            }
            if (message == 0x0005 && wParam.ToInt64() == 1) DispatcherQueue.TryEnqueue(() => HideToTray());
        }
        if (message == SingleInstance.ActivationMessage)
        {
            if (_isClosing) return IntPtr.Zero;
            ActivateExistingWindow();
            return new IntPtr(1);
        }
        if (message == WmHotkey)
        {
            int action = wParam.ToInt32() - GlobalHotkeyId;
            if (action == 0) StartPauseButton_Click(this, new RoutedEventArgs());
            else if (!_isRunning && !_isReadingClipboard && !_isSelectingRegion && !_isClosing)
            {
                if (action == 5 && _translationSettings.IsConfigured)
                {
                    ActivateExistingWindow();
                    TranslateButton_Click(TranslateButton, new RoutedEventArgs());
                }
                Button? tool = action switch { 1 => ScreenshotToolButton, 2 => ShareToolButton, 3 => QrToolButton, 4 => TextExtractToolButton, _ => null };
                if (tool is not null) RegionButton_Click(tool, new RoutedEventArgs());
            }
            if (action is >= 0 and <= 5) return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _isClosing = true;
        _summaryCancellation?.Cancel();
        _captionWindow?.Close();
        StopAudioPreview();
        _trayIcon?.Dispose();
        _trayIcon = null;
        _sharingCompletion?.TrySetResult(null);
        _translationCancellation?.Cancel();
        _shareCancellation?.Cancel();
        _ocrCancellation?.Cancel();
        _screenshotClipboardStream?.Dispose();
        _regionSelector?.Cancel();
        _cancellation?.Cancel();

        _globalHotkeys?.Release();

        if (_windowSubclassed
            && !RemoveWindowSubclass(_windowHandle, _windowSubclassProc, HotkeySubclassId))
        {
            Debug.WriteLine($"Unable to remove the hotkey window subclass (Windows error {Marshal.GetLastWin32Error()}).");
        }
    }

    private sealed record PendingCorrection(
        int Position,
        char CorrectCharacter,
        int DueAtSourceIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowSizeLimits
    {
        public WindowPoint Reserved, MaximumSize, MaximumPosition, MinimumTrack, MaximumTrack;
    }
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        IntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd,
        SubclassProc subclassProc,
        UIntPtr subclassId,
        IntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd,
        SubclassProc subclassProc,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
}
