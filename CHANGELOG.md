# Changelog

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
