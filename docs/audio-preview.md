# Local voice preview — v0.3 (main x64)

Open the left **Audio** panel, select your microphone and headphones, and click **Start preview**. Use headphones to avoid speaker feedback. Preview is off whenever the app starts.

- **Depth:** subtly lowers pitch by 0–3 semitones while compensating formants to retain a natural vocal tone. Start around the 1-semitone default.
- **Body:** adds restrained warmth to the lower voice range. The default is 45%.
- **Preview volume:** controls headphone playback only; it does not change Windows microphone gain.
- **Compare:** switches between the processed and original voice at the same preview volume and with matched buffering.
- **Stop preview:** releases the microphone and playback devices. Collapsing the audio panel or exiting also stops it. Tray mode keeps an explicitly started preview active until stopped or exited.

Audio is processed locally using Signalsmith Stretch/Linear and Windows WASAPI through NAudio. It is kept in short memory buffers; no audio is recorded, uploaded or sent to an API. No cloud service, API key, GPU, model download or virtual audio driver is required. Audio preferences are saved in `%LOCALAPPDATA%\AutoTyper\audio.json`.

This version is a headphone preview. It does not replace the microphone used by calls or other apps. Device and processing buffers introduce a delay; the panel reports an estimated stream delay while running. Final sound quality depends on the microphone and voice, so compare the result at modest settings before increasing depth or body.

If no microphone is listed, connect it and refresh the device list. If access is denied, allow desktop microphone access in Windows settings. A device disconnect stops the preview and leaves the other app tools available.

## Development

The native processor is built automatically before the main app. Build tools require PowerShell 7 and Visual Studio C++/CMake, or the workspace's LLVM-MinGW toolchain. Signalsmith sources and MIT licenses are pinned under `audio/native/vendor/`; NAudio is pinned in the project. Runtime audio processing needs no network access.

Run `dotnet run --project tests/Audio.Tests -c Release` after building the main app to check synthetic pitch, silence, original comparison, sample counts, limiting, buffer bounds and settings validation. `tests/Audio.UiFixture` opens the real window using default preferences and no global shortcut registration, so it can be inspected alongside the normal app.

v0.3 development targets the recommended x64 app. ARM64 and Compact stay on the published 0.2.2 release.
