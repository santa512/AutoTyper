using System.Runtime.InteropServices;

namespace TypingPractice;

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
                SendPair(0, character, Unicode);
                break;
        }
    }

    internal static void SendKey(ushort virtualKey)
    {
        uint flags = virtualKey is 0x25 or 0x27 ? ExtendedKey : 0;
        SendPair(virtualKey, 0, flags);
    }

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
