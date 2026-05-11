// Guard against the cbSize-mismatch regression that broke Warp tab switching:
// when INPUTUNION only contains KEYBDINPUT (24 bytes), Marshal.SizeOf<INPUT>()
// returns 32 on x64 / 20 on x86 — but the real Win32 INPUT struct is 40 / 28
// because the union must be sized for MOUSEINPUT. Passing the wrong cbSize
// causes SendInput to return 0 with ERROR_INVALID_PARAMETER (87) and zero
// keystrokes are injected.

using System.Runtime.InteropServices;

namespace CopilotBooster.Tests.Services;

public class Win32InputLayoutTests
{
    [Fact]
    public void INPUT_StructSize_MatchesWin32LayoutForCurrentArchitecture()
    {
        int expected = IntPtr.Size == 8 ? 40 : 28;
        int actual = Marshal.SizeOf<Win32Input.INPUT>();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void INPUTUNION_Size_MatchesMOUSEINPUT()
    {
        // The union must be at least as large as MOUSEINPUT (the largest member).
        int unionSize = Marshal.SizeOf<Win32Input.INPUTUNION>();
        int mouseInputSize = Marshal.SizeOf<Win32Input.MOUSEINPUT>();

        Assert.Equal(mouseInputSize, unionSize);
    }

    [Fact]
    public void KeyDown_ProducesKeyboardInput_WithVirtualKey()
    {
        var input = Win32Input.KeyDown(0x22); // VK_NEXT (PageDown)

        Assert.Equal(Win32Input.INPUT_KEYBOARD, input.type);
        Assert.Equal((ushort)0x22, input.U.ki.wVk);
        Assert.Equal(0u, input.U.ki.dwFlags);
    }

    [Fact]
    public void KeyUp_ProducesKeyboardInput_WithKeyupFlag()
    {
        var input = Win32Input.KeyUp(0x11); // VK_CONTROL

        Assert.Equal(Win32Input.INPUT_KEYBOARD, input.type);
        Assert.Equal((ushort)0x11, input.U.ki.wVk);
        Assert.Equal(Win32Input.KEYEVENTF_KEYUP, input.U.ki.dwFlags);
    }
}
