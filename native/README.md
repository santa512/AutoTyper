# Auto Typer Compact

**Version 0.2 · C++17 · Win32 · Windows x64**

A separate, standalone native implementation of Auto Typer. It uses Windows controls, Rich Edit, and `SendInput` directly. The original C# / WinUI project remains in the parent directory.

## Run

Double-click `dist/AutoTyperCompact.exe`.

The Release executable built with LLVM-MinGW is approximately **368 KB**, compared with roughly 92 MB for the self-contained WinUI edition. It needs no installer, extraction step, .NET runtime, Windows App SDK, or adjacent DLLs. Its imports are Windows system libraries, including the Universal CRT included with Windows 10 and Windows 11.

Requires Windows 10 version 1809 or later, or Windows 11, on an x64 computer. Native dark-mode title-bar customization depends on the Windows version; unsupported DWM attributes are ignored.

## Included

- A compact, resizable, DPI-aware window, initially 680 × 500 logical pixels including its title bar.
- App theme follows Windows light/dark preference, with a matching title bar where supported.
- One multiline input, with a read-only lock and current-character selection during typing. The selection remains visible when another app has focus.
- A single row for ready delay, global hotkey, and circular progress/status.
- Pace, rhythm variation, punctuation pauses, and optional adjacent-key typos with delayed corrections.
- Start/Pause/Resume, Stop, Reset progress, and Pin on top.
- Embedded keyboard icon, manifest, and version metadata.
- Event-driven scheduling on the UI message loop; no blocking sleeps or worker threads.

## Use

1. Enter the text to send, or click **Paste from clipboard** to replace it with plain clipboard text. Empty, unavailable, non-text, or oversized clipboard contents preserve the input. The button is disabled during a run.
2. Press **Start**, then focus the destination text field during the countdown. Alternatively, focus the destination first and press **Ctrl+Shift+Space**.
3. Use that hotkey to pause or resume without bringing Auto Typer forward.
4. Hover over the small status indicator for details and the full character count.

The ready delay defaults to **2.5 seconds**, pace to **1.0×**, rhythm to **50%**, and mistake chance to **1%**. Choose **0%** mistakes for exact typing without intentional typos. The compact edition adjusts mistake chance in whole percentages, from 0% to 12%.

To change the global hotkey, click its field, press a combination using Ctrl, Alt, and/or Shift, then click **Set**. If it is unavailable, the previous registered combination is restored when possible. Windows-key combinations are not supported by this edition's native hotkey control. Settings are session-only and locked during a run. Stop, then start a new run to apply different settings.

Typing waits while any Ctrl, Alt, Shift, or Windows modifier is held. The destination foreground window is captured at the end of the countdown. Switching foreground windows stops output before the next scheduled key; an intentional pause permits switching away, but return to the original target before resuming. Starting after stopping always begins at the start of the input.

Use a plain text editor and keep its caret and selection unchanged. Editors may transform input, and Enter/Tab preserve the destination's normal behavior. Complex grapheme clusters and editor-specific cursor movement can affect delayed corrections. Applications with higher privileges or protected input may reject simulated keystrokes. The app does not read destination text or physical keystrokes.

## Build with Visual Studio / CMake

Install Visual Studio's **Desktop development with C++** workload and a Windows SDK. From this directory:

```powershell
cmake -S . -B build -A x64
cmake --build build --config Release
ctest --test-dir build -C Release --output-on-failure
```

The app is `build/Release/AutoTyperCompact.exe`. The MSVC build uses the static C++ runtime (`/MT` for Release). Exact binary size varies by compiler.

## Build with portable LLVM-MinGW

Download an x86_64 UCRT toolchain from the [official LLVM-MinGW releases](https://github.com/mstorsjo/llvm-mingw/releases). Extract it, then run:

```powershell
.\build.ps1 -ToolchainRoot 'C:\tools\llvm-mingw-20261104-ucrt-x86_64'
```

The helper compiles the icon/manifest/version resources, builds and runs the engine tests, builds a disposable integration-test destination, and creates:

- `dist/AutoTyperCompact.exe`
- `dist/AutoTyperCompact.exe.sha256`

The toolchain is needed only to build, not to run the app. No download or installation is performed by `build.ps1`.

## Verification

`tests/engine_tests.cpp` simulates text insertion, arrow navigation, and Backspace against an in-memory editor. It checks exact final output, caret restoration, monotonic progress, zero/forced/random mistakes, delayed corrections across 50 random seeds, surrogate pairs, Unicode text, timing bounds, empty input, and rejection of invalid options.

`build/input_target.exe` (or `build/Release/input_target.exe` with CMake) is a disposable editor for checking native keyboard output without touching existing documents. It stores nothing and saves no files. Launch both apps, focus the test editor, and use Auto Typer's global hotkey. Close the test target when finished.

## Source layout

| File | Purpose |
| --- | --- |
| `src/main.cpp` | Win32 window, controls, timer scheduling, global hotkey, theme, and keyboard output. |
| `src/TypingEngine.h`, `src/TypingEngine.cpp` | UI-independent rhythm and correction state machine. |
| `resources/` | Independent copies of the icon, manifest, and native version resource. |
| `tests/engine_tests.cpp` | Typing-engine verification. |
| `tests/input_target.cpp` | Disposable GUI destination for manual integration testing. |
| `CMakeLists.txt` | Standalone CMake / Visual Studio project. |
| `build.ps1` | Portable LLVM-MinGW build helper. |

This project can be copied out of the repository and built on its own. It does not reference the parent C# project or its build artifacts.
