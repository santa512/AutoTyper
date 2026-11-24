# Live captions (main x64)

Click **CC** in the main window to open the caption overlay and start listening automatically.

## Accurate and Fast modes

The mode picker in the caption toolbar chooses the local speech engine. Both run entirely on this PC; no audio is uploaded. The choice is remembered.

- **Accurate** (default) uses [Whisper](https://github.com/ggml-org/whisper.cpp) `base.en`. Captions are punctuated and capitalized, and hold up much better with accents, fast speech, background music and names. Speech is split at natural pauses; while someone is talking the overlay shows a dim preview ending in "…", then replaces it with the final phrase. Expect each phrase about 1–2 seconds after it ends; on slower CPUs the preview refreshes less often. Stopping or switching mode takes effect immediately. First use downloads about 60 MB from the official [whisper.cpp model repository](https://huggingface.co/ggerganov/whisper.cpp).
- **Fast** uses the [Vosk](https://alphacephei.com/vosk/models) small English model. Words appear as they are spoken, with lower accuracy and no punctuation. First use downloads about 41 MB.

Each model's pinned SHA-256 is verified before use, and models are stored in `%LOCALAPPDATA%\AutoTyper\Speech`. Later starts work offline. Switching mode restarts capture and keeps pending captions.

System audio is the default source. **Include mic** also captures the microphone selected in the Audio panel (or the default microphone if none is selected). Captions identify each source as **[System]** or **[Mic]**. These labels identify the audio input, not individual people speaking within the same input. Listening to your own microphone through the system output can cause the same speech to appear under both sources.

The compact toolbar contains the target language, caption mode, Include mic, translate icon and pin. The overlay is pinned above other windows initially. Close it to stop capture and clear its temporary transcript. Changing Include mic restarts capture with the selected sources while keeping pending captions. A retry icon appears if capture fails. Loading progress and errors appear only while relevant; normal operation has no heading or descriptive footer. Caption capture operates independently of the voice preview.

## Translate only new captions

Configure **Microsoft Translator** in **Settings → Translation**, then choose the target language in the overlay and click its **translate icon**. The button stays disabled until the API is configured and new text is available. Its tooltip shows pending segments.

The app finalizes the current phrase and sends only segments added since the last successful translation. It displays those source-labeled captions and their translation in the main input box, replacing the previous input content. Captions keep arriving while the request runs; newer segments remain pending for the next click. Failed requests retain their text for retry. Large backlogs are split at segment boundaries into requests of at most 20,000 characters.

Speech recognition runs locally in English in both modes. No audio is uploaded or recorded to disk. Explicit translation sends the new caption text to Microsoft Translator. Recognition accuracy varies with speech and source audio quality.

The [Groq summarize icon](ai-summary.md) beside Translate opens an expandable summary panel while keeping live captions visible. Summary context is independent of the translation cursor.

## Development checks

`dotnet run --project tests/Captions.Tests -c Release -r win-x64` checks incremental translation state, source labels, retry behavior, request limits, phrase splitting, Whisper text cleanup and native engine loading for both modes. Pass a Whisper `.bin` model and a mono 16 kHz, 16-bit PCM WAV after `--` to exercise Accurate recognition. Optionally pass an extracted Vosk model folder and a mono 16 kHz, 16-bit PCM WAV after `--` to exercise real speech recognition. Add `--loopback` after those paths to play that sample and verify default-system audio capture on an interactive Windows audio device. This hardware check is optional in CI.

The packaged EXE's `--caption-self-test` verifies both bundled native speech engines without starting capture, downloading a model, opening a window or contacting a translation service.
