# Auto Typer

**v0.2.1 · Windows 10/11 · x64 · Portable**

Type text into any app, capture screenshots, scan QR codes, extract English text offline, and translate clipboard text.

**[Download page](https://santa512.github.io/AutoTyper/#downloads)** · [Release notes](https://github.com/santa512/AutoTyper/releases/tag/v0.2.1) · [Full guide](docs/guide.md)

## Download

| Version | Best for | Download | Verify |
| --- | --- | --- | --- |
| **Auto Typer — recommended** | All features, including screen tools, translation and tray mode | [AutoTyper.exe](https://github.com/santa512/AutoTyper/releases/download/v0.2.1/AutoTyper.exe) | [SHA-256](https://github.com/santa512/AutoTyper/releases/download/v0.2.1/AutoTyper.exe.sha256) |
| **Compact — lighter version** | A smaller, simpler app for automatic typing | [AutoTyperCompact.exe](https://github.com/santa512/AutoTyper/releases/download/v0.2.1/AutoTyperCompact.exe) | [SHA-256](https://github.com/santa512/AutoTyper/releases/download/v0.2.1/AutoTyperCompact.exe.sha256) |

Download the EXE and double-click it; no installer is needed. The recommended app bundles its runtimes and offline OCR, so its first launch can take a moment. Keep its filename as `AutoTyper.exe`.

**Upgrading?** Choose **Exit** from the old app's tray menu first. Closing a tray-enabled window only hides it. Saved settings and share history are preserved.

## Quick start

1. Enter text or use **Paste from clipboard**.
2. Adjust pace and delay in **Settings → Auto Typer** if needed.
3. Press **Start**, then focus the destination app during the 3-second countdown.
4. Use **Ctrl+Shift+Space** to pause or resume. **Stop** ends the run.

The input highlights your current character. Switching to another foreground window stops typing.

## Main features

- **Screen tools:** drag a region to save/copy a screenshot, scan a QR code, or extract English text offline.
- **Instant Share:** upload a screenshot to your Cloudflare R2 bucket and keep a history of its links. Shared images expire after 14 days using your bucket's lifecycle rule.
- **Clipboard translation:** Microsoft Translator shows the original and translation together, with English as the default target language.
- **Global hotkeys and tray mode:** use tools from other apps; the translation hotkey brings Auto Typer forward. Change shortcuts in **Settings → Hotkeys**.
- **Settings backups:** export/import preferences and all API keys using a password-encrypted backup. Imported settings apply when you press **Save**.

Sharing and translation require your own API settings and stay disabled until configured. QR scanning and English OCR run locally. [Setup and troubleshooting →](docs/guide.md)

## Default hotkeys

| Action | Shortcut |
| --- | --- |
| Start / pause typing | Ctrl+Shift+Space |
| Screenshot | Ctrl+Shift+S |
| Instant Share | Ctrl+Shift+I |
| Scan QR code | Ctrl+Shift+Q |
| Extract English text | Ctrl+Shift+E |
| Translate clipboard text | Ctrl+Shift+T |

These shortcuts describe the recommended app. See the [Compact guide](native/README.md) for the lighter version.

## Build from source

On Windows with the .NET 10 SDK:

```powershell
git clone https://github.com/santa512/AutoTyper.git
cd AutoTyper
dotnet build AutoTyper.csproj -p:Platform=x64
```

[Full build and test instructions](docs/guide.md#build-from-source) · [Changelog](CHANGELOG.md) · [Report an issue](https://github.com/santa512/AutoTyper/issues)
