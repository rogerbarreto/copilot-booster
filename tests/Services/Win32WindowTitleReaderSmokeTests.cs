// Smoke tests that exercise the real Win32 P/Invoke surface of Win32WindowTitleReader.
// These guard against LibraryImport entry-point regressions like the one observed when
// the source-generated P/Invoke for GetWindowTextLength was missing EntryPoint = "GetWindowTextLengthW",
// causing System.EntryPointNotFoundException at runtime.

using System.Diagnostics;

namespace CopilotBooster.Tests.Services;

public class Win32WindowTitleReaderSmokeTests
{
    [Fact]
    public void FindMainWindowHandle_ForCurrentProcess_DoesNotThrow()
    {
        var reader = new Win32WindowTitleReader();
        int pid = Process.GetCurrentProcess().Id;

        // Exercises the full Win32 P/Invoke chain: EnumWindows + IsWindowVisible
        // + GetWindowThreadProcessId + GetWindowTextLength. Test-host process may
        // or may not have visible windows — return value is allowed to be Zero.
        // The test passes iff no EntryPointNotFoundException (or other native
        // marshalling exception) is thrown.
        var hwnd = reader.FindMainWindowHandle(pid);

        Assert.True(hwnd == IntPtr.Zero || hwnd != IntPtr.Zero);
    }

    [Fact]
    public void FindMainWindowHandle_ForUnknownPid_ReturnsZeroWithoutThrowing()
    {
        var reader = new Win32WindowTitleReader();

        // PID guaranteed not to match any window owner. Still walks all top-level
        // windows, calling the same P/Invokes — this is the live regression test
        // for the GetWindowTextLength entry-point bug.
        var hwnd = reader.FindMainWindowHandle(int.MaxValue - 1);

        Assert.Equal(IntPtr.Zero, hwnd);
    }

    [Fact]
    public void ReadTitle_OnZeroHandle_ReturnsEmpty()
    {
        var reader = new Win32WindowTitleReader();

        var title = reader.ReadTitle(IntPtr.Zero);

        Assert.Equal("", title);
    }
}
