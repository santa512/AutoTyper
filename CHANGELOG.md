# Changelog

## 0.3.2 — 2026-11-19 (main x64)

- Automatically pause on focus loss and resume at the same typing progress when the original input is focused again. Check native control and accessibility identity; keep manual pause separate.


## 0.3.1 — 2026-11-19 (main x64)

- Move the audio panel to the right, with Pin, Settings and Hamburger controls in that order. Use an icon-only pin and place live captions beside QR.
- Shrink the window when audio is collapsed, enforce a minimum height, align text controls and fix clipped Settings fields.
- Omit the System prefix on new system-only caption lines when microphone input is disabled.
- Add General → Hide from screen capture for the main window and live captions. It applies immediately, verifies the Windows flag, and Save remembers the choice. Requires Windows 10 version 2004 or later; capture-tool compatibility varies.
- Import encrypted settings by selecting the backup first, then entering that backup's password in a separate inline prompt. Wrong passwords can be retried. Passwords are not stored in plaintext or saved locally. Existing backups remain compatible.



## 0.3 — 2026-11-19 (main x64)

- Add Groq summary icons next to Translate in the main input and caption overlay. Show summaries in separate expandable panels while preserving input and ongoing captions; support encrypted primary/secondary keys with bounded quota failover.

- Add a General settings section for startup, tray mode, theme, encrypted export/import and separate tray click/double-click actions; preview theme changes and restore them when discarded.

- Add a pinned live-caption overlay with local English recognition of system audio and an optional microphone stream, labeled System / Mic. Download and verify the English model on first use.
- Translate only new caption segments into the main input using existing Microsoft Translator settings; preserve text on failure and keep newer captions pending during requests.

- Add a collapsible left audio panel for local microphone-to-headphones preview, with microphone and output selection, depth, body, preview volume, original-voice comparison and level meters.
- Use pinned MIT-licensed Signalsmith Stretch/Linear for formant-preserving pitch adjustment, plus local warmth EQ, gentle compression and output limiting. No audio API key, cloud processing, recording or virtual microphone driver is used.
- Keep preview off until explicitly started; stop capture on Stop, panel collapse or app exit. Save audio preferences locally between sessions.
- Add DSP checks for pitch, bypass, silence, overload, stream length, processing delay and bounded buffering, plus an isolated UI layout harness.
- Limit future release builds to the main x64 app. Existing ARM64 and Compact downloads remain at 0.2.2.

## 0.2.2 — 2026-11-18

- Add native ARM64 builds for the recommended app and Compact, alongside x64. Bundle architecture-matched OCR runtime DLLs and retain offline English OCR on ARM64.
- Add per-user MSI setup installers with Start menu shortcuts, upgrade support and uninstall support for both editions and architectures. Keep portable EXEs available; all downloads include checksums.
- Add Run on startup under Auto Typer settings, off by default. Startup follows the current EXE location, opens in the tray when enabled, and is included in settings backups. Uninstall removes the installed app's startup entry.
- Build, test OCR, verify PE architecture, install and uninstall on native x64 and ARM64 GitHub runners before publishing.
- Add architecture and installer choices to the download page and concise README.

## 0.2.1 — 2026-11-18

- Add a configurable global clipboard translation hotkey (Ctrl+Shift+T by default), active after Microsoft Translator setup, with conflict-safe preference migration.
- Match the language selector and translate button heights and remove the blank gap after Hotkeys.
- Add password-encrypted settings export/import, including API keys and all hotkeys, with review before applying an import.
- Add Microsoft Translator clipboard translation with an English-default target-language dropdown, original and translated text together in the input, an encrypted API key setting, optional clipboard output and cancellation.

## 0.2 — 2026-11-18

### Both editions

- Add a user-activated Paste from clipboard button; it loads plain text without starting typing and is disabled during a run.
- Update application and executable version metadata to 0.2.

### Auto Typer

- Add region selection for QR scanning, English OCR, screenshots and Instant Share. Escape and right-click cancel selection.
- Decode QR codes locally with ZXing.Net and load results into the input.
- Add offline English PaddleOCR PP-OCRv5 recognition with bundled models and CPU inference, contrast preprocessing, cancellation and uncertain-result review hints.
- Save screenshots as PNG by default or copy the image using the shared clipboard toggle. Send Windows notifications with previews when supported.
- Upload screenshots directly to a configured Cloudflare R2 bucket, then put the public URL into the input and clipboard. Encrypt keys for the current Windows user and disable sharing before setup.
- Add a 14-day retention notice and encrypted history of the 200 most recent shared URLs, with a timeline column and Copy/Open icons. Keep history above the toolbar so screen tools remain accessible.
- Move typing configuration into persistent settings with vertical navigation for Auto Typer, Cloudflare and Hotkeys. Keep Back and Save together, with a compact save/discard prompt for unsaved changes.
- Add distinct global shortcuts for Screenshot, Instant Share, QR and Text Extract in addition to typing start/pause. Show available shortcuts in tooltips and the tray menu; reject duplicate assignments and report external conflicts.
- Replace circular progress with a thin full-width line. Default new typing settings to a 3-second ready delay while preserving compatible existing preferences.
- Make the app single-instance per Windows user/session across portable copies, restore minimized windows and recover after an unexpected exit.
- Add optional, persistent Minimize to tray behavior. Minimize and Close hide the app; Exit quits. Recreate the icon after Explorer restarts and keep background hotkeys active.
- Add a themed topmost tray menu with Main, QR, OCR, Screenshot, Instant Share, Settings and Exit. Constrain it to the monitor work area, remove the white native frame and visible scrollbar, and support keyboard navigation and dismissal.
- Add regression checks for QR, screenshots, OCR, uploads, encrypted storage, saved preferences, hotkey registration, single-instance races and tray menu placement.

### Downloads and documentation

- Publish v0.2 executables and SHA-256 files for both editions.
- Keep Auto Typer as the recommended download, with Compact presented as the lighter typing option.
- Refresh the download page, interface illustration, release notes and setup guide for the current feature set.

## 0.1 — 2026-11-18

Initial release for Windows x64.

The app and project are now named **Auto Typer**, published from `santa512/AutoTyper`. The renamed executable is `AutoTyper.exe`; the version remains 0.1.

The release also includes **Auto Typer Compact**, a separate native C++ / Win32 implementation with a smaller window and an executable under 1 MB. The download page offers both editions and their checksums.

- Global keyboard output with key-down and key-up events.
- Adjustable pace, rhythm variation, punctuation pauses, and a default 1% mistake chance.
- Adjacent-key mistakes with delayed arrow-key and Backspace corrections.
- Global start/pause hotkey, defaulting to Ctrl+Shift+Space.
- Ready countdown with a default delay of 2.5 seconds.
- Read-only input during a run, with the current character highlighted.
- Compact delay, hotkey, and circular progress controls on one line.
- A title bar matching the app theme and an optional always-on-top pin.
- Single-file, self-contained executable with a keyboard icon.
