# Auto Typer

**Version 0.1** · Windows desktop · x64 · Initial release

A small Windows app that types your text into another application with an adjustable, natural rhythm. It supports punctuation pauses, occasional adjacent-key mistakes, and delayed corrections using the arrow keys and Backspace.

**[Choose an edition](https://santa512.github.io/AutoTyper/#downloads)** · **[Release history](https://github.com/santa512/AutoTyper/releases)**

## Choose your download

Both editions are version 0.1 for Windows x64. Both support global typing, pause/resume, a ready countdown, typo corrections, current-character highlighting, and pin on top.

| Edition | Download | Size | Main differences |
| --- | --- | --- | --- |
| **Compact — native C++ / Win32** | [AutoTyperCompact.exe](https://github.com/santa512/AutoTyper/releases/download/v0.1/AutoTyperCompact.exe) · [SHA-256](https://github.com/santa512/AutoTyper/releases/download/v0.1/AutoTyperCompact.exe.sha256) | Under 1 MB; varies by compiler | Smaller window, no bundled runtime or extraction. Ctrl/Alt/Shift hotkeys and whole-percent mistake settings. |
| **WinUI — modern Windows UI** | [AutoTyper.exe](https://github.com/santa512/AutoTyper/releases/download/v0.1/AutoTyper.exe) · [SHA-256](https://github.com/santa512/AutoTyper/releases/download/v0.1/AutoTyper.exe.sha256) | About 92 MB | Modern Windows controls, bundled runtimes, Windows-key hotkeys, and finer mistake adjustments. |

Run one edition at a time so they do not compete for the same global hotkey. The guide below describes the WinUI edition; see the [Compact guide](native/README.md) for native build and usage details.

## Download and launch

1. Download your chosen edition from the table above or the repository's **Releases** page.
2. Double-click `AutoTyperCompact.exe` or `AutoTyper.exe`.
3. Neither edition needs an installer. Compact uses Windows system components directly. WinUI bundles its .NET / Windows App SDK dependencies and unpacks them into a local cache on first launch; keep its filename as `AutoTyper.exe`.

The WinUI download is approximately **92 MB** because it includes both runtimes; the extracted runtime uses additional disk space. Compact is **under 1 MB**. You only need your chosen EXE; do not download GitHub's source ZIP to run the app.

### Requirements

- Windows 10 version 1809 or later, or Windows 11.
- An x64 computer. Version 0.1 does not ship separate x86 or ARM64 binaries.
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
2. Adjust the pace, rhythm, mistake chance, and ready delay if needed.
3. Press **Start**, then click the destination text field during the ready countdown. Alternatively, leave the destination focused and press **Ctrl+Shift+Space**.
4. Release the hotkey modifiers. Typing begins after the countdown.
5. Use the global hotkey to **pause or resume** without switching back to Auto Typer.

The input text stays read-only during a run. The current character is highlighted even while the destination app has focus; during corrections, the highlight returns to the character being fixed. A small circular indicator, character count, and status sit on the same row as the delay and hotkey controls. Hover over status for activity details.

**Stop** ends the run. **Reset progress** clears progress without deleting your text. **Pin on top** keeps the window visible above other apps. The Start/Pause and Stop controls remain in the bottom-right corner.

## Settings

| Control | Default | Behavior |
| --- | --- | --- |
| Pace | 1.0× | 0.5× to 2.0×; scales delays between simulated keys. |
| Rhythm | 50% | 0% to 100%; adds short speed variations and occasional pauses. |
| Mistake chance | 1% | 0% to 12%; applies to letters, with adjacent QWERTY-key substitutions where available. |
| Ready delay | 2.5 seconds | 0 to 10 seconds to focus the destination. |
| Global hotkey | Ctrl+Shift+Space | Starts, pauses, or resumes the run. |
| Pin on top | Off | Keeps the app above other windows. |

To change the hotkey, click the **pencil** beside the hotkey field, hold Ctrl, Alt, Shift, or Windows, and press another key. Escape cancels recording. Hover over the pencil to read hotkey registration feedback. Hotkey availability depends on whether Windows or another app already uses that combination.

Settings apply to the current app session; version 0.1 does not persist them between launches. Each new Start begins typing from the start of the input text. Pause/Resume continues the current run.

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

A separate native implementation is available in [`native/`](native/README.md). It has a smaller window and produces an approximately **368 KB** standalone executable without .NET or the Windows App SDK. The original WinUI edition remains available independently.

### WinUI edition

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
| `MainWindow.xaml.cs` | Typing rhythm, corrections, progress, global hotkey, title-bar colors, and pin behavior. |
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
4. Configure trusted signing as described in [SIGNING.md](SIGNING.md). Publishing builds both editions, signs and timestamps their EXEs, verifies the signatures, then generates checksums and uploads all four files. A manual run can select an edition; `publish=false` produces unsigned test artifacts without changing the release.

The current v0.1 release is unsigned. A signing account and validated publisher identity must be configured before a signed rebuild can replace it. See [SIGNING.md](SIGNING.md) for the complete setup and verification steps.

The Pages workflow deploys `docs/` when it changes on `main`. Configure **Settings → Pages → Source → GitHub Actions** for the repository.

## Feedback

[Open an issue](https://github.com/santa512/AutoTyper/issues) with the app version, Windows version, destination editor, and steps to reproduce. Use a short, non-sensitive sample of input text where necessary.

See [CHANGELOG.md](CHANGELOG.md) for version history.
