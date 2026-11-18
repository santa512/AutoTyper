// Disposable local destination for manual SendInput integration testing.
#include <windows.h>

LRESULT CALLBACK TargetProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    static HWND editor;
    if (message == WM_CREATE) {
        editor = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"", WS_CHILD | WS_VISIBLE | WS_TABSTOP |
            ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN | WS_VSCROLL, 12, 12, 540, 250,
            window, reinterpret_cast<HMENU>(100), GetModuleHandleW(nullptr), nullptr);
        SendMessageW(editor, WM_SETFONT, reinterpret_cast<WPARAM>(GetStockObject(DEFAULT_GUI_FONT)), TRUE);
        SetFocus(editor); return 0;
    }
    if (message == WM_SIZE) { MoveWindow(editor, 12, 12, LOWORD(lParam) - 24, HIWORD(lParam) - 24, TRUE); return 0; }
    if (message == WM_SETFOCUS) { SetFocus(editor); return 0; }
    if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(window, message, wParam, lParam);
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int show) {
    WNDCLASSW cls{}; cls.lpfnWndProc = TargetProc; cls.hInstance = instance;
    cls.hCursor = LoadCursorW(nullptr, IDC_IBEAM); cls.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
    cls.lpszClassName = L"AutoTyperTestTarget";
    RegisterClassW(&cls);
    HWND window = CreateWindowW(cls.lpszClassName, L"Auto Typer Test Target", WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, 600, 350, nullptr, nullptr, instance, nullptr);
    ShowWindow(window, show);
    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    return 0;
}
