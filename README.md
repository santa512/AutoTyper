# Auto Typer

**Version 0.2** · Windows desktop · x64

Type into other apps with a natural rhythm, capture screenshots, scan QR codes, extract English text offline, and share screenshot links. Global shortcuts and optional tray mode keep the tools within reach.

**[Download Auto Typer](https://santa512.github.io/AutoTyper/#downloads)** · **[v0.2 release](https://github.com/santa512/AutoTyper/releases/tag/v0.2)** · **[Changelog](CHANGELOG.md)**

## Choose your download

Both editions are version 0.2 for Windows x64. The recommended download includes the screen tools and tray features. Compact keeps the smaller typing-focused interface.

| Edition | Download | Size | Included |
| --- | --- | --- | --- |
| **Auto Typer — recommended** | [AutoTyper.exe](https://github.com/santa512/AutoTyper/releases/download/v0.2/AutoTyper.exe) · [SHA-256](https://github.com/santa512/AutoTyper/releases/download/v0.2/AutoTyper.exe.sha256) | About 114.1 MB | Typing, clipboard paste, QR scanning, English OCR, screenshot save/copy, R2 sharing and history, five global hotkeys, saved settings, single-instance behavior and tray mode. |
| **Auto Typer Compact** | [AutoTyperCompact.exe](https://github.com/santa512/AutoTyper/releases/download/v0.2/AutoTyperCompact.exe) · [SHA-256](https://github.com/santa512/AutoTyper/releases/download/v0.2/AutoTyperCompact.exe.sha256) | Under 1 MB | A lighter typing app with clipboard paste, natural rhythm, corrections, highlighting, a global typing hotkey and pin on top. No screen tools, OCR, sharing or tray menu. |

The guide below describes the recommended app; see the [Compact guide](native/README.md) for its usage and build instructions. Run one edition at a time to avoid conflicts between their typing shortcuts. Single-instance behavior applies to copies of the recommended app; Compact remains a separate application.

## Download and launch

1. Download your chosen EXE from the links above or the repository's Releases page.
2. Double-click it. Neither edition needs an installer.
3. Keep the recommended filename as **AutoTyper.exe**. Its bundled runtimes and English OCR models extract to a local cache on first launch, which can take a moment.

Only your chosen EXE is needed; the checksum is optional verification. Do not download the source ZIP to run the app. The main download is about 114.1 MB because it bundles its runtimes and offline OCR. Compact is under 1 MB and uses Windows system components directly.

**Upgrading:** choose **Exit** in the old tray menu before starting v0.2. Closing a tray-enabled window only hides it. Existing compatible settings and local sharing history are retained. These executables are unsigned, so Windows may show an unknown-publisher or reputation prompt.

### Requirements

- Windows 10 version 1809 or later, or Windows 11.
- An x64 computer. Version 0.2 does not ship separate x86 or ARM64 binaries.
- A destination application that accepts simulated Windows keyboard input.

### Verify the download

The release includes a matching `.sha256` file for each executable. Calculate your chosen EXE's checksum:

```powershell
Get-FileHash .\AutoTyper.exe -Algorithm SHA256
# Or, for Compact:
Get-FileHash .\AutoTyperCompact.exe -Algorithm SHA256
```

Compare the result with the hash in the accompanying checksum file.

## Use the app

1. Enter your text in **Practice text**, or click **Paste from clipboard** to replace it with clipboard text. The button is disabled during a run. Empty or non-text clipboard contents leave your current input unchanged.
2. Open **Settings → Auto Typer** to adjust the pace, rhythm, mistake chance, and ready delay if needed.
3. Press **Start**, then click the destination text field during the ready countdown. Alternatively, leave the destination focused and press **Ctrl+Shift+Space**.
4. Release the hotkey modifiers. Typing begins after the countdown.
5. Use the global hotkey to **pause or resume** without switching back to Auto Typer.

The input text stays read-only during a run. The current character is highlighted even while the destination app has focus; during corrections, the highlight returns to the character being fixed. A thin, full-width progress line sits below the input, with the character count and status above it. Hover over status for activity details.

**Stop** ends the run. **Reset progress** clears progress without deleting your text. **Pin on top** keeps the window visible above other apps. The Start/Pause and Stop controls remain in the bottom-right corner.

## One running instance

Auto Typer allows one main-edition instance per Windows user and session, including portable copies launched from different folders. Opening the EXE again brings the existing window forward and restores it if minimized. The current input and settings remain in that window. A repeated launch during screen selection cancels that selection and brings the main window back.

The instance check happens before window creation, notification registration and global hotkey registration. Concurrent first launches elect one primary instance. Closing the primary releases the lock; an unexpected exit also allows a later launch to recover. Builds from before this feature do not participate in the shared lock, so close those copies once when upgrading. The separate Compact edition is unchanged.

Run `dotnet run --project tests/SingleInstance.Tests` for isolated process tests of exclusive ownership, simultaneous launches, shutdown and crash recovery, plus tray placement checks for different taskbar edges, scaled screens and small work areas.

## Minimize to tray

Enable **Settings → Auto Typer → Minimize to tray**, then Save. The option is off by default and persists between launches. While enabled, Minimize and Close hide the window in the notification area instead of quitting. Global hotkeys keep working when the main window is hidden. Click the tray icon or open the EXE again to restore the same window.

Right-click the tray icon for **Main**, **QR code**, **OCR / Text extract**, **Screenshot**, **Instant share**, **Settings**, and **Exit**. The menu follows the app's light or dark theme, uses a temporary topmost window, and stays within the monitor work area above the taskbar. Menu entries include icons and the currently registered action shortcuts. Click outside or press Escape to dismiss it; arrow keys navigate enabled actions. Instant Share requires Cloudflare setup; capture tools and Settings are disabled while another operation or settings edit is active. Main always restores the window, and Exit quits and removes the tray icon. Tools started while hidden return to the tray after completion unless you explicitly restore the main window during the operation. Windows may place the icon under its hidden-icons arrow.

Turning the option off removes the tray icon and restores normal Minimize/Close behavior. Auto Typer recreates the icon if Explorer restarts and keeps the window on the taskbar if icon creation fails. This option does not add Windows startup registration.

## Screen-region tools

The main edition includes icon buttons for **Scan QR code**, **Text Extract**, and **Screenshot** at the bottom left, plus **Paste from clipboard** beside the input heading. Hover over each icon for its name. Click a screen tool, then drag across the desktop to select a rectangle. The app hides during selection. Drag in any direction; Escape, right-click, or switching apps cancels. These tools are disabled while typing.

**Scan QR code** captures only the chosen region in memory and decodes it locally with [ZXing.Net](https://github.com/micjahn/ZXing.Net). Include the entire QR code and the clear border around it. Successful scans replace the typing input directly, without an extra dialog. URLs are loaded as text. A failed or cancelled scan preserves your input and clipboard. No image is saved or uploaded.

**Copy result to clipboard** is a shared toggle beside the three tool icons, off by default. When enabled, successful QR results also copy to the clipboard, and Screenshot copies an image instead of opening Save As. If QR copying fails, its decoded text still appears in the input. This preference lasts for the current app session.

**Screenshot** captures the selected region at its original pixel size, then opens **Save As** for a PNG file by default. With the clipboard toggle on, it copies the image for pasting into other apps instead; no save dialog opens. It leaves the typing input unchanged. Cancelling selection or Save As does not copy an image or send a notification.

After a successful save or copy, Auto Typer sends a **Windows notification** with a small screenshot preview when supported. Clicking it brings Auto Typer forward. Notifications respect Windows notification settings and Do Not Disturb; they are not supported when the app runs as administrator. Saving and copying still work when notifications are unavailable. The latest screenshot notification replaces the preceding one. For notification previews, small PNG thumbnails are cached in `%LOCALAPPDATA%\AutoTyper\NotificationPreviews`; previews older than 24 hours are cleaned on the next launch or successful capture. The Screenshot tool does not upload images; the separate Share tool uploads only when selected.

**Text Extract** reads printed English with PaddleOCR PP-OCRv5, using a dedicated English recognizer through RapidOcrNet and ONNX Runtime on the CPU. Its models and native libraries are bundled with the portable EXE; it needs no Python installation, GPU, Windows OCR language pack, or network connection. Select the complete text region with a little surrounding space. Recognized lines replace the typing input directly and also copy as plain text when the shared clipboard toggle is on. Empty, failed, and cancelled extractions leave your input and clipboard unchanged. **Stop** cancels recognition.

The app normalizes dark and low-contrast regions and enlarges small text before recognition. Low-contrast input or uncertain character scores show a review hint. Tiny punctuation, blur, unusual fonts, and complicated layouts can still cause mistakes—even when the model's confidence is high. Review important text before typing it into another app. Recognition is currently configured for English only; the Compact edition is unchanged.

OCR model sources, versions, checksums, and license notices are included under `Assets/OCR/`. Small notification previews are the only screenshot images cached to disk; OCR processes its selected region in memory. The v0.2 main download bundles the inference engine and models, so it is larger than v0.1. No model downloads are needed at runtime.

### Share screenshots to R2

Open the gear icon for the in-window Settings page. **Auto Typer** is the default section, containing pace, rhythm, mistake chance and a wider delay input. **Cloudflare bucket** is the second section and **Hotkeys** is the third. Settings use two columns: vertical navigation on the left and scrollable content on the right. Navigation jumps to each section on the same page. **Back** and **Save** sit together at the bottom right and stay fixed while scrolling. Save stores typing preferences between launches, and Back leaves immediately if nothing changed. With unsaved typing, hotkey or Cloudflare changes, a compact prompt appears above Back and Save: **Yes** saves before leaving; **No** discards the edits. Cloudflare setup is optional when saving typing preferences. Enter your Cloudflare account ID, bucket name, Access Key ID, Secret Access Key, and public bucket URL. The supplied public R2 URL is prefilled. Choose the bucket jurisdiction if it differs from Default. No Worker, server, or command-line setup is required.

Create an [R2 API token](https://developers.cloudflare.com/r2/api/tokens/) with **Object Read & Write** permission scoped to your screenshot bucket. Use its Access Key ID and Secret Access Key, rather than a general Cloudflare bearer token. Enable public access to that same bucket and copy its public URL into the app. Save validates and stores the configuration locally; it does not upload a test file or verify the bucket's public access.

**Share** stays disabled until valid Cloudflare settings are saved. Its global shortcut shows a setup reminder while unconfigured. Click **Share screenshot**, drag a region, and the app uploads a PNG directly to R2, replaces the typing input with the public image URL, and copies that URL to the clipboard. Failed uploads and cancelled selections preserve the typing input. The clipboard toggle still controls the other three tools; Share always copies its link. Existing screenshot notifications also include the shared image when Windows notifications are available.

All sharing settings, including both keys, are encrypted for the current Windows user in `%LOCALAPPDATA%\AutoTyper\sharing.json`. No keys ship in the EXE. Other users configure their own bucket. Back leaves immediately if nothing changed. With unsaved typing, hotkey or Cloudflare changes, a compact prompt appears above Back and Save: **Yes** saves before leaving; **No** discards the edits.

Uploads use random `share/<id>.png` names, with a 20 MB limit. Shared screenshots are saved for **14 days**, using the bucket's existing lifecycle rule. Keep the 14-day expiration rule enabled for the `share/` prefix. R2 may remove expired objects asynchronously; history dates are estimates based on upload time. The app does not change or replace bucket lifecycle rules. Stop cancels the request, but an upload already received by R2 may complete. The app does not retry automatically. The public `r2.dev` endpoint is intended for development and may be rate limited; a public custom domain can be entered instead.

The **Shared URL history** icon beside Share expands a panel inside the text card, above the bottom toolbar, so QR, Screenshot, Text Extract and Share stay visible and clickable. Click the history icon again or its close button to collapse it. The panel lists the 200 most recent uploads, newest first, with upload and estimated expiry dates. Each row has a separate **Timeline** column, its URL and expiry, and **Copy** and **Open** icon buttons. Copy places the URL on the clipboard; Open launches it in your default browser. History persists across app restarts, marks older entries as expired, and does not extend their retention. Links are encrypted for the current Windows user in `%LOCALAPPDATA%\AutoTyper\share-history.dat`. Only uploads made after this feature was added appear in history; the app does not list the bucket. Typing preferences are stored in `%LOCALAPPDATA%\AutoTyper\typing.json`.

Run `dotnet run --project tests/Sharing.Tests` to verify signing, request paths, errors, cancellation, history, typing settings, validation, and Windows encryption without making any external uploads. A real R2 upload requires your own configured keys and bucket.

### OCR validation

Run `dotnet run --project tests/Ocr.Tests` on Windows x64 with .NET 10. The eight synthetic English fixtures cover paragraphs, dark backgrounds, 11-pixel text, closely spaced UI labels, low contrast, monospace text, serif text, and a single word, plus blank regions, invalid images, and cancellation. The current fixture set has 378 characters: 377 are recovered correctly (99.74%). A missing comma in the low-contrast sample is a documented allowance, and that sample must be marked for review. These small, generated fixtures are regression tests, not an accuracy guarantee for arbitrary screenshots.

## Settings

| Control | Default | Behavior |
| --- | --- | --- |
| Pace | 1.0× | 0.5× to 2.0×; scales delays between simulated keys. |
| Rhythm | 50% | 0% to 100%; adds short speed variations and occasional pauses. |
| Mistake chance | 1% | 0% to 12%; applies to letters, with adjacent QWERTY-key substitutions where available. |
| Ready delay | 3 seconds | 0 to 10 seconds to focus the destination. |
| Auto type hotkey | Ctrl+Shift+Space | Starts, pauses, or resumes the run. |
| Screenshot hotkey | Ctrl+Shift+S | Selects a screen region to save or copy. |
| Instant share hotkey | Ctrl+Shift+I | Selects a region to upload to the configured R2 bucket. |
| QR hotkey | Ctrl+Shift+Q | Selects a region to scan for a QR code. |
| Text extract hotkey | Ctrl+Shift+E | Selects a region for English OCR. |
| Pin on top | Off | Keeps the app above other windows. |

In **Settings → Hotkeys**, click the pencil beside an action, hold Ctrl, Alt, Shift, or Windows, and press another key. Escape cancels recording. Each action needs a distinct shortcut. **Save** checks registration with Windows and applies the shortcuts; **Back** asks whether to save unsaved changes; Yes saves and No discards. Shortcuts pause while Settings is open so recording does not invoke tools. Unavailable combinations are reported in Hotkeys. Available shortcuts remain active when another application's shortcut conflicts at startup. Button tooltips show their configured shortcuts and mark unavailable registrations.

Typing settings and all five shortcuts persist between launches. Existing saved delay preferences are preserved; the default for new settings is 3 seconds. Each new Start begins typing from the start of the input text. Pause/Resume continues the current run.

## How external typing behaves

- Output always goes to the destination app; there is no separate preview mode.
- A target foreground window is captured after the ready delay. If another window is foreground before the next simulated key, the run stops.
- Key-down and key-up pairs are sent through Windows `SendInput`. Text uses Unicode input; Enter, Tab, Backspace, and correction arrows use their corresponding keys.
- Held modifier keys suspend output so the start/pause hotkey does not turn simulated text into shortcuts.
- Enter and Tab retain the destination application's behavior, which can include submitting text or moving focus.
- Keep the destination caret and selection unchanged while typing. Automatic indentation, autocorrection, rich-text formatting, and changes of focus inside the same window can affect the result. A plain text editor is the most predictable destination.
- Input into applications with higher privileges, protected fields, or applications that reject simulated input may be blocked.

The app does not read text from the destination, record your physical typing, or synchronize text with a server. It maintains its own progress and cursor counts, so it cannot detect every transformation made by the destination editor.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| “Focus another app during the ready delay” | Click the destination text field before the countdown ends, or start with the hotkey while that field is focused. |
| “Target app lost focus” | A different foreground window was detected. Starting again begins a new run from the start of the source text. |
| Nothing is being typed | Release Ctrl/Alt/Shift/Windows; confirm the destination accepts simulated input and does not run with higher privileges. |
| The hotkey does not work | Hover over the pencil for registration feedback and choose a different combination. |
| Corrections land in the wrong place | Use a plain text field and avoid moving the caret, changing selection, or enabling automatic editor transformations. |
| The app fails after renaming the EXE | Restore the filename `AutoTyper.exe`; WinUI uses it to resolve application resources. |
| First launch takes longer | The single-file build extracts its bundled runtimes on first launch. |

## Build from source

### Compact C++ / Win32 edition

A separate native implementation is available in [`native/`](native/README.md). It has a smaller window and produces an small standalone executable (size varies by compiler) without .NET or the Windows App SDK. The original WinUI edition remains available independently.

### Main edition

Use Windows with the **.NET 10 SDK** and the Windows tooling required by WinUI 3 / Windows App SDK. The project references `Microsoft.WindowsAppSDK` 2.7.0. The release workflow uses a Windows runner with the .NET 10 SDK.

```powershell
git clone https://github.com/santa512/AutoTyper.git
cd AutoTyper
dotnet build AutoTyper.csproj -p:Platform=x64
```

### Build the single-file release

```powershell
dotnet publish AutoTyper.csproj -p:PublishProfile=SingleFile-x64 -c Release -p:Platform=x64
Copy-Item dist\SingleFile-win-x64\AutoTyper.exe dist\AutoTyper.exe
```

Run `dist\AutoTyper.exe`. The profile bundles .NET and the Windows App SDK, compresses the payload, and extracts its content at launch. Trimming is disabled for WinUI compatibility.

For a folder-based build, use `-p:PublishProfile=Portable-x64` instead. Keep every file in that publish folder together.

### Repository layout

| Path | Purpose |
| --- | --- |
| `MainWindow.xaml` | Window layout, input controls, compact status, and theme resources. |
| `MainWindow.xaml.cs` | Typing, region tools, shared settings, global shortcuts, progress and window behavior. |
| `EnglishOcr.cs`, `Assets/OCR/` | Offline English OCR pipeline, bundled recognizer, dictionary, model metadata and licenses. |
| `QrDecoder.cs`, `QrScreenScanner.cs` | Local QR decoding and screen scan integration. |
| `ScreenCapture.cs`, `ScreenRegionSelector.cs`, `ScreenshotPng.cs` | Screen region selection, capture and PNG encoding. |
| `ScreenshotShareClient.cs`, `SharingSettings.cs`, `ShareHistory.cs` | Signed R2 uploads, encrypted configuration and local URL history. |
| `GlobalHotkeys.cs`, `TypingSettings.cs`, `SingleInstance.cs` | Shortcut registration, persisted preferences and cross-folder instance activation. |
| `TrayIcon.cs`, `TrayMenuWindow.*`, `TrayMenuPlacement.cs` | Notification-area icon, themed topmost menu and monitor-aware positioning. |
| `tests/` | QR, PNG, OCR, sharing, hotkey, preference, instance and placement regression checks. |
| `NativeKeyboard.cs` | Native keyboard input interop. |
| `Assets/AutoTyper.ico` | App and executable icon. |
| `scripts/New-AppIcon.ps1` | Recreates the multi-resolution icon. |
| `Properties/PublishProfiles/` | Single-file and folder-based release profiles. |
| `docs/` | Static GitHub Pages download site. |
| `.github/workflows/` | Release publishing and GitHub Pages deployment. |
| `release-notes/` | Notes attached to each release. |

Build outputs, downloaded tooling, and local editor settings are excluded from Git. Executables are distributed as Release assets, rather than stored in source history.

## Publish a new version

1. Update the version in `AutoTyper.csproj` and the visible badge in `MainWindow.xaml`.
2. Update this README, the download page, and `CHANGELOG.md`.
3. Add `release-notes/vX.Y.md`.
4. Push a matching `vX.Y` tag. The release workflow builds both editions, runs the main-edition regression checks and compact engine tests, generates checksums, and publishes all four files together. A manual workflow run can publish only `compact` or `winui` while retaining the other edition's assets.

The Pages workflow deploys `docs/` when it changes on `main`. Configure **Settings → Pages → Source → GitHub Actions** for the repository.

## Feedback

[Open an issue](https://github.com/santa512/AutoTyper/issues) with the app version, Windows version, destination editor, and steps to reproduce. Use a short, non-sensitive sample of input text where necessary.

See [CHANGELOG.md](CHANGELOG.md) for version history.
