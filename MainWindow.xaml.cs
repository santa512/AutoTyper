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
    private uint _hotkeyModifiers = ModControl | ModShift | ModNoRepeat;
    private uint _hotkeyVirtualKey = 0x20;
    private string _hotkeyLabel = "Ctrl+Shift+Space";
    private bool _isRunning;
    private bool _isPaused;
    private bool _isCountingDown;
    private bool _isRecordingHotkey;
    private bool _windowSubclassed;
    private bool _hotkeyRegistered;
    private int _typedLength;
    private bool _hasProgress;
    private int[] _sourceTextOffsets = Array.Empty<int>();
    private IntPtr _targetWindow;

    public MainWindow()
    {
        _windowSubclassProc = WindowSubclassProc;
        InitializeComponent();
        RegisterGlobalHotkey();
        AppSurface.Loaded += (_, _) => UpdateTitleBarTheme();
        AppSurface.ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateTitleBarTheme);
        UpdateTitleBarTheme();
        UpdateOptionLabels();
        UpdateProgressText();
        Closed += MainWindow_Closed;
    }

    private async void StartPauseButton_Click(object sender, RoutedEventArgs e)
    {
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
        SourceBox.IsHitTestVisible = false;
        ReadyDelayBox.IsEnabled = false;
        _targetWindow = IntPtr.Zero;
        RecordHotkeyButton.IsEnabled = false;
        _isRunning = true;
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
            _isPaused = false;
            _isCountingDown = false;
            SourceBox.IsReadOnly = false;
            SourceBox.IsHitTestVisible = true;
            SourceBox.SelectionLength = 0;
            ReadyDelayBox.IsEnabled = true;
            RecordHotkeyButton.IsEnabled = _windowSubclassed;
            _targetWindow = IntPtr.Zero;
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
            PinButtonText.Text = presenter.IsAlwaysOnTop ? "Pinned on top" : "Pin on top";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        _resumeSignal?.TrySetResult(true);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
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
                InsertAtCaret(typo);
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
                InsertAtCaret(next);
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
        TypingProgressRing.Value = total == 0 ? 0 : completed * 100d / total;
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
            SendExternalKey(0x25); // Left arrow
            _caret--;
        }

        await Task.Delay(_random.Next(250, 601), cancellationToken);
        await WaitIfPausedAsync(cancellationToken);

        await DelayForKeyAsync(cancellationToken);
        await WaitIfPausedAsync(cancellationToken);
        SendExternalKey(0x08); // Backspace
        _typedLength--;
        _caret--;
        ActivityText.Text = "Backspace";

        await DelayForKeyAsync(cancellationToken);
        await WaitIfPausedAsync(cancellationToken);
        InsertAtCaret(correction.CorrectCharacter);
        ActivityText.Text = "Typo corrected";

        while (_caret < _typedLength)
        {
            await DelayForKeyAsync(cancellationToken);
            await WaitIfPausedAsync(cancellationToken);
            SendExternalKey(0x27); // Right arrow
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

            // Do not turn simulated keys into shortcuts while the hotkey is held.
            if ((GetPressedHotkeyModifiers() & ~ModNoRepeat) != 0)
            {
                await Task.Delay(20, cancellationToken);
                continue;
            }

            return;
        }
    }

    private void InsertAtCaret(char character)
    {
        EnsureExternalTarget();
        NativeKeyboard.SendCharacter(character);
        _typedLength++;
        _caret++;
    }

    private void SendExternalKey(ushort virtualKey)
    {
        EnsureExternalTarget();
        NativeKeyboard.SendKey(virtualKey);
    }

    private void EnsureExternalTarget()
    {
        if (_targetWindow == IntPtr.Zero
            || NativeKeyboard.GetForegroundWindow() != _targetWindow)
        {
            throw new InvalidOperationException("Target app lost focus. Typing stopped; start a new run to continue.");
        }
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

    private void RecordHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_hotkeyRegistered && !UnregisterHotKey(_windowHandle, GlobalHotkeyId))
        {
            HotkeyStatusText.Text =
                $"Could not start recording (Windows error {Marshal.GetLastWin32Error()}).";
            return;
        }

        _hotkeyRegistered = false;
        _isRecordingHotkey = true;
        HotkeyInput.Text = "Press a key combination";
        HotkeyStatusText.Text = "Hold a modifier and press a key. Esc cancels.";
        HotkeyInput.Focus(FocusState.Programmatic);
    }

    private void HotkeyInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isRecordingHotkey)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == VirtualKey.Escape)
        {
            _isRecordingHotkey = false;
            TryChangeGlobalHotkey(
                _hotkeyModifiers,
                _hotkeyVirtualKey,
                _hotkeyLabel,
                restorePreviousOnFailure: false);
            return;
        }

        uint modifiers = GetPressedHotkeyModifiers();
        if (IsModifierKey(e.Key))
        {
            string pressedModifiers = FormatModifiers(modifiers);
            HotkeyInput.Text = pressedModifiers.Length == 0
                ? "Press a key combination"
                : $"{pressedModifiers}+...";
            HotkeyStatusText.Text = "Now press the key to complete the combination.";
            return;
        }

        if ((modifiers & (ModControl | ModAlt | ModShift | ModWindows)) == 0)
        {
            HotkeyStatusText.Text = "Use Ctrl, Alt, Shift, or Windows with another key.";
            return;
        }

        _isRecordingHotkey = false;
        uint virtualKey = (uint)e.Key;
        string label = FormatHotkey(modifiers, e.Key);
        TryChangeGlobalHotkey(modifiers, virtualKey, label, restorePreviousOnFailure: true);
    }

    private static uint GetPressedHotkeyModifiers()
    {
        uint modifiers = ModNoRepeat;
        if (IsKeyDown(VkControl))
        {
            modifiers |= ModControl;
        }

        if (IsKeyDown(VkAlt))
        {
            modifiers |= ModAlt;
        }

        if (IsKeyDown(VkShift))
        {
            modifiers |= ModShift;
        }

        if (IsKeyDown(VkLeftWindows) || IsKeyDown(VkRightWindows))
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

    private void RegisterGlobalHotkey()
    {
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Microsoft.UI.Windowing.AppWindow appWindow =
            Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
                Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle));
        appWindow.Resize(new Windows.Graphics.SizeInt32(1000, 800));
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
        TryChangeGlobalHotkey(
            _hotkeyModifiers,
            _hotkeyVirtualKey,
            _hotkeyLabel,
            restorePreviousOnFailure: false);
    }

    private bool TryChangeGlobalHotkey(
        uint modifiers,
        uint virtualKey,
        string label,
        bool restorePreviousOnFailure)
    {
        uint previousModifiers = _hotkeyModifiers;
        uint previousVirtualKey = _hotkeyVirtualKey;
        string previousLabel = _hotkeyLabel;
        bool hadPreviousHotkey = _hotkeyRegistered;
        if (hadPreviousHotkey && !UnregisterHotKey(_windowHandle, GlobalHotkeyId))
        {
            int error = Marshal.GetLastWin32Error();
            HotkeyStatusText.Text = $"Could not change hotkey (Windows error {error}); kept {previousLabel}.";
            return false;
        }

        _hotkeyRegistered = false;
        if (RegisterHotKey(_windowHandle, GlobalHotkeyId, modifiers, virtualKey))
        {
            _hotkeyRegistered = true;
            _hotkeyModifiers = modifiers;
            _hotkeyVirtualKey = virtualKey;
            _hotkeyLabel = label;
            HotkeyInput.Text = label;
            HotkeyStatusText.Text = $"Global hotkey registered: {label}";
            return true;
        }

        int registrationError = Marshal.GetLastWin32Error();
        if ((hadPreviousHotkey || restorePreviousOnFailure)
            && RegisterHotKey(
                _windowHandle,
                GlobalHotkeyId,
                previousModifiers,
                previousVirtualKey))
        {
            _hotkeyRegistered = true;
            HotkeyInput.Text = previousLabel;
            HotkeyStatusText.Text =
                $"Could not register {label} (Windows error {registrationError}); kept {previousLabel}.";
            return false;
        }

        HotkeyInput.Text = previousLabel;
        HotkeyStatusText.Text = $"Could not register {label} (Windows error {registrationError}).";
        return false;
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
        if (message == WmHotkey && wParam.ToInt32() == GlobalHotkeyId)
        {
            StartPauseButton_Click(this, new RoutedEventArgs());
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _cancellation?.Cancel();

        if (_hotkeyRegistered && !UnregisterHotKey(_windowHandle, GlobalHotkeyId))
        {
            Debug.WriteLine($"Unable to unregister global hotkey (Windows error {Marshal.GetLastWin32Error()}).");
        }

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
}
