using System.Runtime.InteropServices;

namespace AutoTyper;

internal static class NativeKeyboard
{
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;
    private const uint ExtendedKey = 0x0001;

    internal static void SendCharacter(char character)
    {
        switch (character)
        {
            case '\n':
                SendKey(0x0D);
                break;
            case '\t':
                SendKey(0x09);
                break;
            default:
                if (!TrySendPhysicalCharacter(character)) SendPair(0, character, Unicode);
                break;
        }
    }

    internal static void SendKey(ushort virtualKey)
    {
        uint flags = virtualKey is 0x25 or 0x27 ? ExtendedKey : 0;
        uint scan = MapVirtualKeyEx(virtualKey, 0, TargetLayout());
        SendPair(0, (ushort)scan, flags | 0x0008);
    }

    private static IntPtr TargetLayout()
    {
        uint thread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        return GetKeyboardLayout(thread);
    }

    private static bool TrySendPhysicalCharacter(char character)
    {
        IntPtr layout = TargetLayout();
        short mapping = VkKeyScanEx(character, layout);
        // Avoid synthesizing Ctrl/Alt shortcuts for characters requiring AltGr.
        if (mapping == -1 || ((mapping >> 8) & ~1) != 0) return false;
        ushort key = (ushort)(mapping & 0xff);
        uint scan = MapVirtualKeyEx(key, 0, layout);
        if (scan == 0) return false;
        bool shift = (mapping & 0x100) != 0;
        if (char.IsLetter(character) && (GetKeyState(0x14) & 1) != 0) shift = !shift;
        ushort shiftScan = (ushort)MapVirtualKeyEx(0xA0, 0, layout);
        var inputs = new List<Input>();
        if (shift) inputs.Add(CreateInput(0, shiftScan, 0x0008));
        inputs.Add(CreateInput(0, (ushort)scan, 0x0008));
        inputs.Add(CreateInput(0, (ushort)scan, 0x0008 | KeyUp));
        if (shift) inputs.Add(CreateInput(0, shiftScan, 0x0008 | KeyUp));
        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        if (sent != inputs.Count)
        {
            int error = Marshal.GetLastWin32Error();
            // Release only keys whose down events were inserted without their up events.
            var release = new List<Input>();
            int keyDownIndex = shift ? 1 : 0;
            if (sent > keyDownIndex && sent <= keyDownIndex + 1)
                release.Add(CreateInput(0, (ushort)scan, 0x0008 | KeyUp));
            if (shift && sent > 0 && sent < inputs.Count)
                release.Add(CreateInput(0, shiftScan, 0x0008 | KeyUp));
            if (release.Count > 0) SendInput((uint)release.Count, release.ToArray(), Marshal.SizeOf<Input>());
            throw new InvalidOperationException($"Windows could not send typing input (error {error}). Check the target app's permissions.");
        }
        return true;
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScanEx(char character, IntPtr layout);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint code, uint type, IntPtr layout);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);

    private static void SendPair(ushort virtualKey, ushort scanCode, uint flags)
    {
        Input[] inputs =
        [
            CreateInput(virtualKey, scanCode, flags),
            CreateInput(virtualKey, scanCode, flags | KeyUp)
        ];
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            // A partial insertion must not leave our simulated key held down.
            if (sent == 1)
            {
                SendInput(1, [inputs[1]], Marshal.SizeOf<Input>());
            }

            throw new InvalidOperationException(
                $"Windows could not send typing input (error {error}). Check the target app's permissions.");
        }
    }

    private static Input CreateInput(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = 1, // INPUT_KEYBOARD
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = scanCode,
                Flags = flags
            }
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    // INPUT's union must include MOUSEINPUT's size and pointer alignment on both
    // 32-bit and 64-bit Windows, even though we only send keyboard input.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
}
