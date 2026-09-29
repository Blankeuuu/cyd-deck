using System.ComponentModel;
using System.Runtime.InteropServices;
using StreamDeck.Shortcuts;

namespace StreamDeck.WindowsAgent.Runtime;

internal sealed class WindowsHotkeySender : IHotkeySender
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFExtendedKey = 0x0001;
    private const uint KeyEventFKeyUp = 0x0002;

    internal static int NativeInputSize => Marshal.SizeOf<Input>();

    public Task SendAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();

        if (keys.Count == 0)
        {
            throw new ArgumentException("At least one hotkey key is required.", nameof(keys));
        }

        var virtualKeys = keys.Select(ResolveVirtualKey).ToArray();
        var inputs = new Input[virtualKeys.Length * 2];
        for (var index = 0; index < virtualKeys.Length; index++)
        {
            inputs[index] = CreateKeyboardInput(virtualKeys[index], keyUp: false);
            inputs[virtualKeys.Length + index] = CreateKeyboardInput(virtualKeys[virtualKeys.Length - index - 1], keyUp: true);
        }

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            var releases = virtualKeys.Reverse().Select(key => CreateKeyboardInput(key, keyUp: true)).ToArray();
            _ = SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
            throw new Win32Exception(errorCode, "Windows did not accept the configured hotkey.");
        }

        return Task.CompletedTask;
    }

    internal static ushort ResolveVirtualKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Hotkey key cannot be empty.", nameof(key));
        }

        var normalized = key.Trim().ToUpperInvariant();
        if (normalized.Length == 1 && ((normalized[0] >= 'A' && normalized[0] <= 'Z') || (normalized[0] >= '0' && normalized[0] <= '9')))
        {
            return normalized[0];
        }

        if (normalized.Length >= 2 && normalized[0] == 'F'
            && int.TryParse(normalized.AsSpan(1), out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            return (ushort)(0x6F + functionNumber);
        }

        return normalized switch
        {
            "CTRL" or "CONTROL" => 0x11,
            "SHIFT" => 0x10,
            "ALT" => 0x12,
            "WIN" or "WINDOWS" or "LWIN" => 0x5B,
            "RWIN" => 0x5C,
            "ENTER" or "RETURN" => 0x0D,
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "BACKSPACE" or "BKSP" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "PRINTSCREEN" or "PRTSC" => 0x2C,
            "CAPSLOCK" => 0x14,
            "NUMLOCK" => 0x90,
            "SCROLLLOCK" => 0x91,
            _ => throw new ArgumentException($"Unsupported hotkey key: {key}", nameof(key))
        };
    }

    private static Input CreateKeyboardInput(ushort virtualKey, bool keyUp)
    {
        var flags = keyUp ? KeyEventFKeyUp : 0;
        if (IsExtendedKey(virtualKey))
        {
            flags |= KeyEventFExtendedKey;
        }

        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = flags
                }
            }
        };
    }

    private static bool IsExtendedKey(ushort virtualKey)
    {
        return virtualKey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x90 or 0x91;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, [In] Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
