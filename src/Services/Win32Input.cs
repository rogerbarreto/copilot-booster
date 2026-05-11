using System.Runtime.InteropServices;

namespace CopilotBooster.Services;

/// <summary>
/// Canonical Win32 INPUT layout for SendInput. The union MUST be sized for
/// MOUSEINPUT (the largest member) — otherwise Marshal.SizeOf returns the
/// wrong cbSize and SendInput rejects the call with ERROR_INVALID_PARAMETER (87).
///
/// On x64: sizeof(INPUT) = 40 (4 type + 4 pad + 32 union).
/// On x86: sizeof(INPUT) = 28 (4 type + 24 union).
/// </summary>
internal static partial class Win32Input
{
    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint INPUT_HARDWARE = 2;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    public static INPUT KeyDown(ushort virtualKey) => new()
    {
        type = INPUT_KEYBOARD,
        U = new INPUTUNION { ki = new KEYBDINPUT { wVk = virtualKey } }
    };

    public static INPUT KeyUp(ushort virtualKey) => new()
    {
        type = INPUT_KEYBOARD,
        U = new INPUTUNION { ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = KEYEVENTF_KEYUP } }
    };
}
