#include <windows.h>
#include <commctrl.h>
#include <richedit.h>
#include <dwmapi.h>
#include <uxtheme.h>
#include <algorithm>
#include <cmath>
#include <cwchar>
#include <iterator>
#include <memory>
#include <string>
#include <vector>
#include "TypingEngine.h"

namespace {
constexpr int IconId = 101, TimerId = 1, HotkeyId = 0x4154;
enum ControlId { Source = 200, Start, Stop, Reset, Pin, Delay, Hotkey, ApplyHotkey,
                 Pace, Rhythm, Mistakes, Status, PaceLabel, RhythmLabel, MistakeLabel, ClipboardPaste };

struct App {
    HWND window = nullptr, source = nullptr, start = nullptr, stop = nullptr, reset = nullptr, pin = nullptr;
    HWND delay = nullptr, hotkey = nullptr, apply = nullptr, status = nullptr, heading = nullptr, sourceLabel = nullptr;
    HWND delayLabel = nullptr, hotkeyLabel = nullptr, pace = nullptr, rhythm = nullptr, mistakes = nullptr;
    HWND paceLabel = nullptr, rhythmLabel = nullptr, mistakeLabel = nullptr, tooltip = nullptr;
    HWND clipboard = nullptr;
    HMODULE richEdit = nullptr;
    HFONT font = nullptr, titleFont = nullptr;
    HBRUSH backgroundBrush = nullptr, fieldBrush = nullptr;
    COLORREF background = RGB(32, 32, 32), field = RGB(48, 48, 48), foreground = RGB(240, 240, 240);
    COLORREF muted = RGB(175, 175, 175), accent = RGB(110, 190, 245);
    UINT dpi = 96, hotkeyModifiers = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;
    UINT hotkeyKey = VK_SPACE;
    bool running = false, paused = false, countingDown = false, pinned = false, registered = false;
    bool dark = true;
    HWND target = nullptr;
    std::unique_ptr<compact::TypingEngine> engine;
    std::optional<compact::Step> pending;
    std::vector<LONG> textOffsets;
    std::wstring statusText = L"Ready", activity;
    std::size_t progress = 0, total = 0;
    double remaining = 0;
    ULONGLONG lastTick = 0;

    ~App() {
        if (font) DeleteObject(font);
        if (titleFont) DeleteObject(titleFont);
        if (backgroundBrush) DeleteObject(backgroundBrush);
        if (fieldBrush) DeleteObject(fieldBrush);
        if (richEdit) FreeLibrary(richEdit);
    }
    int px(int value) const { return MulDiv(value, static_cast<int>(dpi), 96); }
    HWND control(const wchar_t* cls, const wchar_t* text, DWORD style, int id = 0) {
        HWND handle = CreateWindowExW(0, cls, text, WS_CHILD | WS_VISIBLE | style,
            0, 0, 0, 0, window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)), GetModuleHandleW(nullptr), nullptr);
        SendMessageW(handle, WM_SETFONT, reinterpret_cast<WPARAM>(font), TRUE);
        return handle;
    }
    HWND label(const wchar_t* text, int id = 0) { return control(L"STATIC", text, SS_LEFT, id); }
    HWND button(const wchar_t* text, int id) { return control(L"BUTTON", text, WS_TABSTOP | BS_OWNERDRAW, id); }
    void place(HWND handle, int x, int y, int width, int height) {
        MoveWindow(handle, px(x), px(y), px(std::max(1, width)), px(height), TRUE);
    }
    void setStatus(std::wstring value, std::wstring detail = {}) {
        statusText = std::move(value);
        activity = std::move(detail);
        std::wstring accessible = statusText;
        if (total) accessible += L" | " + std::to_wstring(progress) + L"/" + std::to_wstring(total) + L" characters";
        SetWindowTextW(status, accessible.c_str());
        InvalidateRect(status, nullptr, FALSE);
    }
    void updateLabels() {
        const int p = static_cast<int>(SendMessageW(pace, TBM_GETPOS, 0, 0));
        const int r = static_cast<int>(SendMessageW(rhythm, TBM_GETPOS, 0, 0));
        const int m = static_cast<int>(SendMessageW(mistakes, TBM_GETPOS, 0, 0));
        wchar_t value[80];
        swprintf_s(value, L"Pace  %.1fx", p / 10.0); SetWindowTextW(paceLabel, value);
        swprintf_s(value, L"Rhythm  %d%%", r); SetWindowTextW(rhythmLabel, value);
        swprintf_s(value, L"Mistakes  %d%%", m); SetWindowTextW(mistakeLabel, value);
    }
    void layout() {
        RECT rect{}; GetClientRect(window, &rect);
        const int w = MulDiv(rect.right, 96, static_cast<int>(dpi));
        const int h = MulDiv(rect.bottom, 96, static_cast<int>(dpi));
        place(heading, 16, 10, w - 132, 28);
        place(pin, w - 104, 10, 88, 28);
        place(sourceLabel, 16, 46, w - 206, 18);
        place(clipboard, w - 174, 40, 158, 24);
        const int editorHeight = std::max(100, h - 228);
        place(source, 16, 68, w - 32, editorHeight);
        RECT textRect{}; GetClientRect(source, &textRect);
        InflateRect(&textRect, -px(7), -px(5));
        SendMessageW(source, EM_SETRECTNP, 0, reinterpret_cast<LPARAM>(&textRect));
        const int barY = 68 + editorHeight + 8;
        place(delayLabel, 16, barY + 5, 48, 22);
        place(delay, 66, barY, 48, 28);
        place(hotkeyLabel, 124, barY + 5, 44, 22);
        place(hotkey, 170, barY, 142, 28);
        place(apply, 318, barY, 36, 28);
        place(status, 364, barY, w - 380, 28);
        const int column = (w - 48) / 3;
        const int slidersY = barY + 40;
        place(paceLabel, 16, slidersY, column, 18);
        place(rhythmLabel, 24 + column, slidersY, column, 18);
        place(mistakeLabel, 32 + 2 * column, slidersY, column, 18);
        place(pace, 12, slidersY + 20, column + 8, 24);
        place(rhythm, 20 + column, slidersY + 20, column + 8, 24);
        place(mistakes, 28 + 2 * column, slidersY + 20, column + 8, 24);
        place(reset, w - 274, h - 42, 102, 28);
        place(stop, w - 164, h - 42, 68, 28);
        place(start, w - 88, h - 42, 72, 28);
    }
    void updateTheme() {
        DWORD light = 1, bytes = sizeof(light);
        RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
                     L"AppsUseLightTheme", RRF_RT_REG_DWORD, nullptr, &light, &bytes);
        dark = light == 0;
        background = dark ? RGB(32, 32, 32) : RGB(245, 245, 245);
        field = dark ? RGB(48, 48, 48) : RGB(255, 255, 255);
        foreground = dark ? RGB(240, 240, 240) : RGB(24, 24, 24);
        muted = dark ? RGB(175, 175, 175) : RGB(95, 95, 95);
        accent = dark ? RGB(110, 190, 245) : RGB(0, 103, 192);
        if (backgroundBrush) DeleteObject(backgroundBrush);
        if (fieldBrush) DeleteObject(fieldBrush);
        backgroundBrush = CreateSolidBrush(background);
        fieldBrush = CreateSolidBrush(field);
        const BOOL useDark = dark;
        DwmSetWindowAttribute(window, 20, &useDark, sizeof(useDark));
        DwmSetWindowAttribute(window, 35, &background, sizeof(background));
        DwmSetWindowAttribute(window, 36, &foreground, sizeof(foreground));
        if (source) {
            SendMessageW(source, EM_SETBKGNDCOLOR, 0, field);
            CHARFORMAT2W format{}; format.cbSize = sizeof(format);
            format.dwMask = CFM_COLOR; format.crTextColor = foreground;
            SendMessageW(source, EM_SETCHARFORMAT, SCF_ALL, reinterpret_cast<LPARAM>(&format));
        }
        for (HWND handle : {delay, hotkey, pace, rhythm, mistakes}) {
            if (handle) SetWindowTheme(handle, dark ? L"DarkMode_Explorer" : L"Explorer", nullptr);
        }
        RedrawWindow(window, nullptr, nullptr, RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_ERASE);
    }
    void updateFonts() {
        HFONT previous = font, previousTitle = titleFont;
        font = CreateFontW(-MulDiv(9, static_cast<int>(dpi), 72), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                          DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                          DEFAULT_PITCH, L"Segoe UI");
        titleFont = CreateFontW(-MulDiv(13, static_cast<int>(dpi), 72), 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
                               DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                               DEFAULT_PITCH, L"Segoe UI");
        for (HWND child = GetWindow(window, GW_CHILD); child; child = GetWindow(child, GW_HWNDNEXT)) {
            SendMessageW(child, WM_SETFONT, reinterpret_cast<WPARAM>(child == heading ? titleFont : font), TRUE);
        }
        if (previous) DeleteObject(previous);
        if (previousTitle) DeleteObject(previousTitle);
    }
    bool createControls() {
        richEdit = LoadLibraryW(L"Msftedit.dll");
        if (!richEdit) return false;
        updateFonts();
        heading = label(L"Auto Typer Compact  v0.1");
        SendMessageW(heading, WM_SETFONT, reinterpret_cast<WPARAM>(titleFont), TRUE);
        pin = button(L"Pin on top", Pin);
        clipboard = button(L"Paste from clipboard", ClipboardPaste);
        sourceLabel = label(L"Text to type");
        source = control(MSFTEDIT_CLASS, L"", WS_TABSTOP | WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL | ES_NOHIDESEL, Source);
        SendMessageW(source, EM_SETTEXTMODE, TM_PLAINTEXT, 0);
        SendMessageW(source, EM_EXLIMITTEXT, 0, 500000);
        SendMessageW(source, EM_SETEVENTMASK, 0, ENM_CHANGE);
        SetWindowTextW(source, L"The best way to improve your typing is to practice at a comfortable pace.");
        delayLabel = label(L"Delay (s)");
        delay = control(L"EDIT", L"2.5", WS_TABSTOP | ES_AUTOHSCROLL | WS_BORDER, Delay);
        SendMessageW(delay, EM_SETLIMITTEXT, 6, 0);
        hotkeyLabel = label(L"Hotkey");
        hotkey = control(HOTKEY_CLASSW, L"", WS_TABSTOP | WS_BORDER, Hotkey);
        SendMessageW(hotkey, HKM_SETHOTKEY, MAKEWORD(VK_SPACE, HOTKEYF_CONTROL | HOTKEYF_SHIFT), 0);
        apply = button(L"Set", ApplyHotkey);
        status = control(L"STATIC", L"Typing progress", SS_OWNERDRAW, Status);
        paceLabel = label(L"Pace", PaceLabel);
        pace = control(TRACKBAR_CLASSW, L"Pace", WS_TABSTOP | TBS_HORZ | TBS_NOTICKS, Pace);
        rhythmLabel = label(L"Rhythm", RhythmLabel);
        rhythm = control(TRACKBAR_CLASSW, L"Rhythm", WS_TABSTOP | TBS_HORZ | TBS_NOTICKS, Rhythm);
        mistakeLabel = label(L"Mistakes", MistakeLabel);
        mistakes = control(TRACKBAR_CLASSW, L"Mistake chance", WS_TABSTOP | TBS_HORZ | TBS_NOTICKS, Mistakes);
        SendMessageW(pace, TBM_SETRANGE, TRUE, MAKELONG(5, 20)); SendMessageW(pace, TBM_SETPOS, TRUE, 10);
        SendMessageW(rhythm, TBM_SETRANGE, TRUE, MAKELONG(0, 100)); SendMessageW(rhythm, TBM_SETPOS, TRUE, 50);
        SendMessageW(mistakes, TBM_SETRANGE, TRUE, MAKELONG(0, 12)); SendMessageW(mistakes, TBM_SETPOS, TRUE, 1);
        reset = button(L"Reset progress", Reset); stop = button(L"Stop", Stop); start = button(L"Start", Start);
        EnableWindow(stop, FALSE);
        tooltip = CreateWindowExW(WS_EX_TOPMOST, TOOLTIPS_CLASSW, nullptr, WS_POPUP | TTS_ALWAYSTIP,
            CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, window, nullptr, GetModuleHandleW(nullptr), nullptr);
        TOOLINFOW info{}; info.cbSize = sizeof(info); info.uFlags = TTF_IDISHWND | TTF_SUBCLASS;
        info.hwnd = window; info.uId = reinterpret_cast<UINT_PTR>(status); info.lpszText = LPSTR_TEXTCALLBACKW;
        SendMessageW(tooltip, TTM_ADDTOOLW, 0, reinterpret_cast<LPARAM>(&info));
        SendMessageW(tooltip, TTM_SETMAXTIPWIDTH, 0, px(360));
        registered = RegisterHotKey(window, HotkeyId, hotkeyModifiers, hotkeyKey) != FALSE;
        if (!registered) setStatus(L"Hotkey unavailable", L"Choose a different hotkey and click Set.");
        updateLabels(); updateTheme(); layout();
        return source && delay && hotkey && start && status;
    }
    std::wstring readSource() {
        GETTEXTLENGTHEX lengthInfo{GTL_PRECISE | GTL_NUMCHARS, 1200};
        const auto length = SendMessageW(source, EM_GETTEXTLENGTHEX, reinterpret_cast<WPARAM>(&lengthInfo), 0);
        std::vector<wchar_t> text(static_cast<std::size_t>(std::max<LRESULT>(length, 0)) + 1);
        GETTEXTEX get{}; get.cb = static_cast<DWORD>(text.size() * sizeof(wchar_t)); get.flags = GT_RAWTEXT; get.codepage = 1200;
        const auto copied = SendMessageW(source, EM_GETTEXTEX, reinterpret_cast<WPARAM>(&get), reinterpret_cast<LPARAM>(text.data()));
        std::wstring normalized;
        textOffsets.clear();
        for (LONG i = 0; i < copied; ++i) {
            textOffsets.push_back(i);
            normalized.push_back(text[static_cast<std::size_t>(i)] == L'\r' ? L'\n' : text[static_cast<std::size_t>(i)]);
            if (text[static_cast<std::size_t>(i)] == L'\r' && i + 1 < copied && text[static_cast<std::size_t>(i + 1)] == L'\n') ++i;
        }
        textOffsets.push_back(static_cast<LONG>(copied));
        return normalized;
    }
    void pasteClipboard() {
        if (running) return;
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) {
            setStatus(L"Clipboard has no text", L"Copy some text, then try again."); return;
        }
        std::wstring text;
        {
            if (!OpenClipboard(window)) {
                setStatus(L"Clipboard is busy", L"Another app is using the clipboard. Try again."); return;
            }
            struct ClipboardLock { ~ClipboardLock() { CloseClipboard(); } } clipboardLock;
            HANDLE handle = GetClipboardData(CF_UNICODETEXT);
            if (!handle) { setStatus(L"Could not read clipboard"); return; }
            const auto capacity = GlobalSize(handle) / sizeof(wchar_t);
            const auto* data = static_cast<const wchar_t*>(GlobalLock(handle));
            if (!data) { setStatus(L"Could not read clipboard"); return; }
            struct MemoryLock { HANDLE handle; ~MemoryLock() { GlobalUnlock(handle); } } memoryLock{handle};
            std::size_t length = 0;
            while (length < capacity && length <= 500000 && data[length]) ++length;
            if (length > 500000) {
                setStatus(L"Clipboard text too long", L"The compact input supports up to 500,000 characters."); return;
            }
            if (length == capacity) { setStatus(L"Invalid clipboard text"); return; }
            if (length == 0) { setStatus(L"Clipboard is empty"); return; }
            text.assign(data, length);
        }
        // Only load text after releasing the clipboard; never start a run here.
        if (!SetWindowTextW(source, text.c_str())) { setStatus(L"Could not load clipboard text"); return; }
        SendMessageW(source, EM_SETSEL, 0, 0);
        setStatus(L"Clipboard text loaded");
    }
    void highlight(std::size_t offset, std::size_t length = 1) {
        if (textOffsets.empty()) return;
        const auto index = std::min(offset, textOffsets.size() - 1);
        CHARRANGE range{textOffsets[index], textOffsets[std::min(index + length, textOffsets.size() - 1)]};
        SendMessageW(source, EM_EXSETSEL, 0, reinterpret_cast<LPARAM>(&range));
        SendMessageW(source, EM_SCROLLCARET, 0, 0);
    }
    void enableOptions(bool enabled) {
        for (HWND handle : {delay, hotkey, apply, pace, rhythm, mistakes, reset, clipboard}) EnableWindow(handle, enabled);
    }
    void finish(const wchar_t* text, const wchar_t* detail = L"") {
        KillTimer(window, TimerId);
        running = paused = countingDown = false;
        engine.reset(); pending.reset(); target = nullptr;
        SendMessageW(source, EM_SETREADONLY, FALSE, 0);
        highlight(progress, 0);
        enableOptions(true);
        EnableWindow(stop, FALSE); SetWindowTextW(start, L"Start");
        setStatus(text, detail);
    }
    void startPause() {
        if (running) {
            paused = !paused;
            SetWindowTextW(start, paused ? L"Resume" : L"Pause");
            setStatus(paused ? L"Paused" : countingDown ? L"Get ready" : L"Typing");
            lastTick = GetTickCount64();
            return;
        }
        wchar_t delayText[32]; GetWindowTextW(delay, delayText, static_cast<int>(std::size(delayText)));
        wchar_t* end = nullptr;
        const double seconds = std::wcstod(delayText, &end);
        while (end && *end == L' ') ++end;
        if (end == delayText || (end && *end) || !std::isfinite(seconds) || seconds < 0 || seconds > 10) {
            setStatus(L"Invalid delay", L"Enter a delay between 0 and 10 seconds."); return;
        }
        auto text = readSource();
        if (text.empty()) { setStatus(L"Enter some text first"); return; }
        compact::Options options;
        options.pace = SendMessageW(pace, TBM_GETPOS, 0, 0) / 10.0;
        options.rhythm = SendMessageW(rhythm, TBM_GETPOS, 0, 0) / 100.0;
        options.mistakeChance = SendMessageW(mistakes, TBM_GETPOS, 0, 0) / 100.0;
        total = text.size(); progress = 0;
        engine = std::make_unique<compact::TypingEngine>(std::move(text), options, std::random_device{}());
        pending.reset(); target = nullptr; running = countingDown = true; paused = false;
        remaining = seconds * 1000;
        SendMessageW(source, EM_SETREADONLY, TRUE, 0); highlight(0);
        enableOptions(false); EnableWindow(stop, TRUE); SetWindowTextW(start, L"Pause");
        setStatus(L"Get ready", L"Focus the destination text field during the countdown.");
        lastTick = GetTickCount64();
        if (!SetTimer(window, TimerId, 15, nullptr)) finish(L"Could not start timer");
    }
    bool modifiersHeld() const {
        return GetAsyncKeyState(VK_CONTROL) < 0 || GetAsyncKeyState(VK_SHIFT) < 0 || GetAsyncKeyState(VK_MENU) < 0 ||
               GetAsyncKeyState(VK_LWIN) < 0 || GetAsyncKeyState(VK_RWIN) < 0;
    }
    bool send(const compact::Step& step) {
        if (step.action == compact::Action::Wait) return true;
        std::vector<INPUT> input;
        auto pair = [&](WORD key, WORD scan, DWORD flags) {
            INPUT down{}; down.type = INPUT_KEYBOARD; down.ki.wVk = key; down.ki.wScan = scan; down.ki.dwFlags = flags;
            input.push_back(down); down.ki.dwFlags |= KEYEVENTF_KEYUP; input.push_back(down);
        };
        using compact::Action;
        switch (step.action) {
        case Action::Left: pair(VK_LEFT, 0, KEYEVENTF_EXTENDEDKEY); break;
        case Action::Right: pair(VK_RIGHT, 0, KEYEVENTF_EXTENDEDKEY); break;
        case Action::Backspace: pair(VK_BACK, 0, 0); break;
        case Action::Text:
            for (wchar_t character : step.text) {
                if (character == L'\n') pair(VK_RETURN, 0, 0);
                else if (character == L'\t') pair(VK_TAB, 0, 0);
                else pair(0, static_cast<WORD>(character), KEYEVENTF_UNICODE);
            }
            break;
        default: break;
        }
        if (GetForegroundWindow() != target) { finish(L"Target lost focus", L"Start a new run after focusing the destination."); return false; }
        const UINT sent = SendInput(static_cast<UINT>(input.size()), input.data(), sizeof(INPUT));
        if (sent != input.size()) {
            if (sent % 2 == 1) SendInput(1, &input[sent], sizeof(INPUT));
            finish(L"Input was blocked", L"The destination may run with higher privileges or reject simulated input.");
            return false;
        }
        return true;
    }
    void tick() {
        const auto now = GetTickCount64();
        const double elapsed = static_cast<double>(now - lastTick); lastTick = now;
        if (!running || paused || modifiersHeld()) return;
        if (!countingDown && GetForegroundWindow() != target) {
            finish(L"Target lost focus", L"Start a new run after focusing the destination."); return;
        }
        remaining -= elapsed;
        if (countingDown) {
            if (remaining > 0) {
                wchar_t text[48]; swprintf_s(text, L"Ready in %.1fs", remaining / 1000.0);
                setStatus(text, L"Focus the destination text field."); return;
            }
            target = GetForegroundWindow();
            if (!target || target == window) { finish(L"Focus another app", L"Choose the destination during the ready delay, then start again."); return; }
            countingDown = false; remaining = 0;
        }
        if (pending && remaining <= 0) {
            if (!send(*pending)) return;
            progress = pending->progress; pending.reset();
        }
        if (!pending) {
            pending = engine->next();
            if (!pending) { progress = total; finish(L"Complete", L"All corrections made."); return; }
            remaining = pending->delayMs;
            highlight(pending->highlightOffset, std::max<std::size_t>(pending->text.size(), 1));
            setStatus(L"Typing", pending->activity);
        }
    }
    void changeHotkey() {
        const WORD value = static_cast<WORD>(SendMessageW(hotkey, HKM_GETHOTKEY, 0, 0));
        const BYTE flags = HIBYTE(value); const UINT key = LOBYTE(value);
        UINT modifiers = MOD_NOREPEAT;
        if (flags & HOTKEYF_CONTROL) modifiers |= MOD_CONTROL;
        if (flags & HOTKEYF_SHIFT) modifiers |= MOD_SHIFT;
        if (flags & HOTKEYF_ALT) modifiers |= MOD_ALT;
        if (!key || modifiers == MOD_NOREPEAT) { setStatus(L"Use Ctrl, Alt or Shift", L"Choose a modifier plus another key, then click Set."); return; }
        if (registered && !UnregisterHotKey(window, HotkeyId)) { setStatus(L"Hotkey change failed"); return; }
        registered = RegisterHotKey(window, HotkeyId, modifiers, key) != FALSE;
        if (registered) { hotkeyModifiers = modifiers; hotkeyKey = key; setStatus(L"Hotkey updated"); }
        else {
            registered = RegisterHotKey(window, HotkeyId, hotkeyModifiers, hotkeyKey) != FALSE;
            BYTE oldFlags = 0;
            if (hotkeyModifiers & MOD_CONTROL) oldFlags |= HOTKEYF_CONTROL;
            if (hotkeyModifiers & MOD_SHIFT) oldFlags |= HOTKEYF_SHIFT;
            if (hotkeyModifiers & MOD_ALT) oldFlags |= HOTKEYF_ALT;
            SendMessageW(hotkey, HKM_SETHOTKEY, MAKEWORD(hotkeyKey, oldFlags), 0);
            setStatus(L"Hotkey unavailable", registered ? L"The previous hotkey is still active." : L"Choose a different hotkey.");
        }
    }
    void draw(const DRAWITEMSTRUCT& item) {
        HDC dc = item.hDC; RECT rect = item.rcItem;
        if (item.CtlID == Status) {
            FillRect(dc, &rect, fieldBrush);
            const int diameter = px(14), x = px(8), y = (rect.bottom - diameter) / 2;
            HPEN track = CreatePen(PS_SOLID, px(2), dark ? RGB(85, 85, 85) : RGB(195, 195, 195));
            HGDIOBJ oldPen = SelectObject(dc, track), oldBrush = SelectObject(dc, GetStockObject(NULL_BRUSH));
            Ellipse(dc, x, y, x + diameter, y + diameter);
            const double fraction = total ? static_cast<double>(progress) / total : 0;
            HPEN arc = CreatePen(PS_SOLID, px(2), accent); SelectObject(dc, arc);
            if (fraction >= 1) Ellipse(dc, x, y, x + diameter, y + diameter);
            else if (fraction > 0) {
                SetArcDirection(dc, AD_CLOCKWISE);
                const double angle = fraction * 6.283185307179586 - 1.5707963267948966;
                Arc(dc, x, y, x + diameter, y + diameter, x + diameter / 2, y,
                    x + diameter / 2 + static_cast<int>(std::cos(angle) * diameter),
                    y + diameter / 2 + static_cast<int>(std::sin(angle) * diameter));
            }
            SelectObject(dc, oldPen); SelectObject(dc, oldBrush); DeleteObject(track); DeleteObject(arc);
            wchar_t count[64]; swprintf_s(count, L"  %d%%", static_cast<int>(fraction * 100));
            const std::wstring text = statusText + (total ? count : L"");
            rect.left = px(30); rect.right -= px(5);
            SetBkMode(dc, TRANSPARENT); SetTextColor(dc, foreground); SelectObject(dc, font);
            DrawTextW(dc, text.c_str(), -1, &rect, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
            return;
        }
        const bool disabled = (item.itemState & ODS_DISABLED) != 0;
        const bool selected = (item.itemState & ODS_SELECTED) != 0;
        const bool primary = item.CtlID == Start || (item.CtlID == Pin && pinned);
        COLORREF fill = primary && !disabled ? accent : field;
        if (selected) fill = dark ? RGB(65, 90, 110) : RGB(190, 215, 230);
        HBRUSH brush = CreateSolidBrush(fill); FillRect(dc, &rect, brush); DeleteObject(brush);
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, disabled ? muted : primary ? (dark ? RGB(10, 20, 28) : RGB(255, 255, 255)) : foreground);
        SelectObject(dc, font);
        wchar_t text[80]; GetWindowTextW(item.hwndItem, text, static_cast<int>(std::size(text)));
        DrawTextW(dc, text, -1, &rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        if (item.itemState & ODS_FOCUS) { InflateRect(&rect, -px(3), -px(3)); DrawFocusRect(dc, &rect); }
    }
};

LRESULT CALLBACK windowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    auto* app = reinterpret_cast<App*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        app = static_cast<App*>(reinterpret_cast<CREATESTRUCTW*>(lParam)->lpCreateParams);
        app->window = window; SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(app));
    }
    if (!app) return DefWindowProcW(window, message, wParam, lParam);
    switch (message) {
    case WM_CREATE:
        app->dpi = GetDpiForWindow(window);
        return app->createControls() ? 0 : -1;
    case WM_SIZE: if (app->source) app->layout(); return 0;
    case WM_GETMINMAXINFO: {
        auto* info = reinterpret_cast<MINMAXINFO*>(lParam);
        info->ptMinTrackSize = {app->px(620), app->px(430)}; return 0;
    }
    case WM_DPICHANGED: {
        app->dpi = HIWORD(wParam); app->updateFonts();
        const auto* rect = reinterpret_cast<RECT*>(lParam);
        SetWindowPos(window, nullptr, rect->left, rect->top, rect->right - rect->left, rect->bottom - rect->top, SWP_NOZORDER | SWP_NOACTIVATE);
        app->layout(); return 0;
    }
    case WM_SETTINGCHANGE: if (app->source) app->updateTheme(); return 0;
    case WM_ERASEBKGND: {
        RECT rect{}; GetClientRect(window, &rect);
        if (app->backgroundBrush) FillRect(reinterpret_cast<HDC>(wParam), &rect, app->backgroundBrush);
        return 1;
    }
    case WM_CTLCOLORSTATIC: case WM_CTLCOLOREDIT: case WM_CTLCOLORBTN: {
        HDC dc = reinterpret_cast<HDC>(wParam);
        SetTextColor(dc, app->foreground); SetBkMode(dc, TRANSPARENT);
        const bool field = reinterpret_cast<HWND>(lParam) == app->delay;
        SetBkColor(dc, field ? app->field : app->background);
        return reinterpret_cast<LRESULT>(field ? app->fieldBrush : app->backgroundBrush);
    }
    case WM_DRAWITEM: app->draw(*reinterpret_cast<DRAWITEMSTRUCT*>(lParam)); return TRUE;
    case WM_HSCROLL: app->updateLabels(); return 0;
    case WM_COMMAND:
        switch (LOWORD(wParam)) {
        case Start: app->startPause(); break;
        case Stop: if (app->running) app->finish(L"Stopped"); break;
        case Reset:
            if (!app->running) { app->progress = app->total = 0; app->highlight(0, 0); app->setStatus(L"Ready"); }
            break;
        case Pin:
            app->pinned = !app->pinned;
            SetWindowPos(window, app->pinned ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            SetWindowTextW(app->pin, app->pinned ? L"Pinned on top" : L"Pin on top"); break;
        case ApplyHotkey: if (!app->running) app->changeHotkey(); break;
        case ClipboardPaste: app->pasteClipboard(); break;
        case Source:
            if (HIWORD(wParam) == EN_CHANGE && !app->running && app->status) {
                app->progress = app->total = 0; app->setStatus(L"Ready");
            }
            break;
        }
        return 0;
    case WM_NOTIFY: {
        auto* header = reinterpret_cast<NMHDR*>(lParam);
        if (header->code == TTN_GETDISPINFOW) {
            auto* info = reinterpret_cast<NMTTDISPINFOW*>(lParam);
            static std::wstring detail;
            detail = app->statusText;
            if (app->total) detail += L" | " + std::to_wstring(app->progress) + L"/" + std::to_wstring(app->total) + L" characters";
            if (!app->activity.empty()) detail += L"\n" + app->activity;
            info->lpszText = const_cast<wchar_t*>(detail.c_str());
            return 0;
        }
        break;
    }
    case WM_HOTKEY: if (wParam == HotkeyId) app->startPause(); return 0;
    case WM_TIMER: if (wParam == TimerId) app->tick(); return 0;
    case WM_CLOSE:
        KillTimer(window, TimerId);
        if (app->registered) UnregisterHotKey(window, HotkeyId);
        DestroyWindow(window); return 0;
    case WM_DESTROY: PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}
} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int show) {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    INITCOMMONCONTROLSEX common{sizeof(common), ICC_BAR_CLASSES | ICC_HOTKEY_CLASS};
    InitCommonControlsEx(&common);
    App app;
    WNDCLASSEXW cls{}; cls.cbSize = sizeof(cls); cls.lpfnWndProc = windowProc; cls.hInstance = instance;
    cls.hCursor = LoadCursorW(nullptr, IDC_ARROW); cls.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(IconId));
    cls.hIconSm = cls.hIcon; cls.lpszClassName = L"AutoTyperCompactWindow";
    if (!RegisterClassExW(&cls)) return 1;
    const UINT dpi = GetDpiForSystem();
    HWND window = CreateWindowExW(0, cls.lpszClassName, L"Auto Typer Compact", WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN,
        CW_USEDEFAULT, CW_USEDEFAULT, MulDiv(680, static_cast<int>(dpi), 96), MulDiv(500, static_cast<int>(dpi), 96),
        nullptr, nullptr, instance, &app);
    if (!window) { MessageBoxW(nullptr, L"Could not create the Auto Typer window.", L"Auto Typer Compact", MB_ICONERROR); return 1; }
    ShowWindow(window, show); UpdateWindow(window);
    MSG message{};
    int result;
    while ((result = GetMessageW(&message, nullptr, 0, 0)) > 0) {
        if (!IsDialogMessageW(window, &message)) { TranslateMessage(&message); DispatchMessageW(&message); }
    }
    return result < 0 ? 1 : static_cast<int>(message.wParam);
}
