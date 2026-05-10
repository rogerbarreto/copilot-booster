using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CopilotBooster.Services;

[ExcludeFromCodeCoverage]
internal sealed partial class Win32KeyboardSender : IKeyboardSender
{
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_TAB = 0x09;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public void SendCtrlTab()
    {
        var inputs = new INPUT[4];

        // Press Ctrl
        inputs[0] = new INPUT
        {
            Type = 1, // INPUT_KEYBOARD
            Union = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT
                {
                    wVk = VK_CONTROL,
                    wScan = 0,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = 0
                }
            }
        };

        // Press Tab
        inputs[1] = new INPUT
        {
            Type = 1,
            Union = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT
                {
                    wVk = VK_TAB,
                    wScan = 0,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = 0
                }
            }
        };

        // Release Tab
        inputs[2] = new INPUT
        {
            Type = 1,
            Union = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT
                {
                    wVk = VK_TAB,
                    wScan = 0,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = 0
                }
            }
        };

        // Release Ctrl
        inputs[3] = new INPUT
        {
            Type = 1,
            Union = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT
                {
                    wVk = VK_CONTROL,
                    wScan = 0,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = 0
                }
            }
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }
}
