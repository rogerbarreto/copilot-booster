/*
 * CONVERSION PATH TO NON-LOCALONLY TESTS (deferred, out of scope for R2):
 *
 * These tests currently depend on Roger's live Warp sessions. To make them deterministic:
 * 1. Spawn warp.exe with a known tab config (`~/.warp/tab_configs/<name>.toml`) defining 3+ tabs.
 * 2. Wait for Warp to fully initialize; hook EVENT_OBJECT_NAMECHANGE to detect when all tabs are loaded.
 * 3. Run the WarpPaneFocuser probe against this controlled instance.
 * 4. Snapshot the original active tab before each test; restore via LiveWarpScenario.Restore() after.
 * 5. Kill the spawned warp.exe at test teardown.
 *
 * This approach eliminates dependency on Roger's live terminals and allows tests to run in CI.
 * The WindowEventHookCollection serialization pattern (already in use here) will handle the
 * hook-based title detection.
 */

using System.Diagnostics;

namespace CopilotBooster.IntegrationTests.Integration;

[Collection(WindowEventHookCollection.Name)]
public sealed class WarpPaneFocusLiveTests : IDisposable
{
    private readonly LiveWarpScenario _scenario;

    public WarpPaneFocusLiveTests()
    {
        this._scenario = LiveWarpScenario.Detect();
    }

    [LocalOnlyStaFact]
    [Trait("Category", "LocalOnly")]
    public void FocusKnownPane_LandsOnExpectedTab()
    {
        if (!this._scenario.IsAvailable)
        {
            return;
        }

        var titleReader = new Win32WindowTitleReader();
        var originalTitle = this._scenario.OriginalTitle;
        var allTitles = this.CaptureAllTitles(titleReader);

        this._scenario.RestoreToOriginal(titleReader);

        if (allTitles.Count < 2)
        {
            return;
        }

        var expectedTitle = allTitles[1];

        var keys = new Win32KeyboardSender();
        var clock = new SystemPaneFocusClock();
        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            WindowFocusService.TryFocusWindowHandle
        );

        var result = focuser.TryFocusPane(this._scenario.WarpProcessId, expectedTitle);

        Assert.True(result);

        var finalTitle = titleReader.ReadTitle(titleReader.FindMainWindowHandle(this._scenario.WarpProcessId));
        Assert.Equal(expectedTitle, finalTitle, StringComparer.OrdinalIgnoreCase);
    }

    [LocalOnlyStaFact]
    [Trait("Category", "LocalOnly")]
    public void FocusUnknownPane_RestoresOriginal()
    {
        if (!this._scenario.IsAvailable)
        {
            return;
        }

        var titleReader = new Win32WindowTitleReader();
        var originalTitle = this._scenario.OriginalTitle;
        var fakeTitle = $"ZZNotARealSession-{Guid.NewGuid():N}";

        var keys = new Win32KeyboardSender();
        var clock = new SystemPaneFocusClock();
        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            WindowFocusService.TryFocusWindowHandle
        );

        var result = focuser.TryFocusPane(this._scenario.WarpProcessId, fakeTitle);

        Assert.False(result);

        var finalTitle = titleReader.ReadTitle(titleReader.FindMainWindowHandle(this._scenario.WarpProcessId));
        Assert.Equal(originalTitle, finalTitle, StringComparer.OrdinalIgnoreCase);
    }

    [LocalOnlyStaFact]
    [Trait("Category", "LocalOnly")]
    public void FocusAlreadyOnTarget_NoTabSwitch()
    {
        if (!this._scenario.IsAvailable)
        {
            return;
        }

        var titleReader = new Win32WindowTitleReader();
        var hwnd = titleReader.FindMainWindowHandle(this._scenario.WarpProcessId);
        var currentTitle = titleReader.ReadTitle(hwnd);

        var keys = new Win32KeyboardSender();
        var clock = new SystemPaneFocusClock();
        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            WindowFocusService.TryFocusWindowHandle
        );

        var result = focuser.TryFocusPane(this._scenario.WarpProcessId, currentTitle);

        Assert.True(result);

        var finalTitle = titleReader.ReadTitle(hwnd);
        Assert.Equal(currentTitle, finalTitle, StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (this._scenario.IsAvailable)
        {
            var titleReader = new Win32WindowTitleReader();
            this._scenario.RestoreToOriginal(titleReader);
        }
    }

    private List<string> CaptureAllTitles(Win32WindowTitleReader titleReader)
    {
        var titles = new List<string>();
        var hwnd = titleReader.FindMainWindowHandle(this._scenario.WarpProcessId);
        var originalTitle = titleReader.ReadTitle(hwnd);
        titles.Add(originalTitle);

        var keys = new Win32KeyboardSender();
        var clock = new SystemPaneFocusClock();

        for (var i = 0; i < 20; i++)
        {
            keys.SendNextTab();
            clock.Sleep(150);

            var title = titleReader.ReadTitle(hwnd);
            if (string.Equals(title, originalTitle, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            titles.Add(title);
        }

        return titles;
    }
}

internal sealed class LiveWarpScenario
{
    public bool IsAvailable { get; }
    public int WarpProcessId { get; }
    public string OriginalTitle { get; }

    private LiveWarpScenario(bool isAvailable, int warpProcessId, string originalTitle)
    {
        this.IsAvailable = isAvailable;
        this.WarpProcessId = warpProcessId;
        this.OriginalTitle = originalTitle;
    }

    public static LiveWarpScenario Detect()
    {
        var warpProcesses = Process.GetProcessesByName("warp");
        if (warpProcesses.Length == 0)
        {
            return new LiveWarpScenario(false, 0, "");
        }

        var copilotProcesses = Process.GetProcessesByName("copilot");
        var warpWithCopilot = warpProcesses.FirstOrDefault(w =>
            copilotProcesses.Any(c => IsDescendantOf(c.Id, w.Id))
        );

        if (warpWithCopilot == null)
        {
            return new LiveWarpScenario(false, 0, "");
        }

        var titleReader = new Win32WindowTitleReader();
        var hwnd = titleReader.FindMainWindowHandle(warpWithCopilot.Id);
        if (hwnd == IntPtr.Zero)
        {
            return new LiveWarpScenario(false, 0, "");
        }

        var originalTitle = titleReader.ReadTitle(hwnd);
        return new LiveWarpScenario(true, warpWithCopilot.Id, originalTitle);
    }

    public void RestoreToOriginal(Win32WindowTitleReader titleReader)
    {
        var hwnd = titleReader.FindMainWindowHandle(this.WarpProcessId);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var keys = new Win32KeyboardSender();
        var clock = new SystemPaneFocusClock();

        for (var i = 0; i < 30; i++)
        {
            var currentTitle = titleReader.ReadTitle(hwnd);
            if (string.Equals(currentTitle, this.OriginalTitle, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            keys.SendNextTab();
            clock.Sleep(150);
        }
    }

    private static bool IsDescendantOf(int childPid, int ancestorPid)
    {
        try
        {
            var current = Process.GetProcessById(childPid);
            while (current != null)
            {
                var parentPid = GetParentProcessIdViaCim(current);
                if (parentPid == ancestorPid)
                {
                    return true;
                }

                if (parentPid == 0)
                {
                    break;
                }

                current = Process.GetProcessById(parentPid);
            }
        }
        catch
        {
            // Process may have exited
        }

        return false;
    }

    private static int GetParentProcessIdViaCim(Process process)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {process.Id}");
            using var results = searcher.Get();
            foreach (var result in results)
            {
                return Convert.ToInt32(result["ParentProcessId"]);
            }
        }
        catch
        {
            // Query failed
        }

        return 0;
    }
}

internal sealed class Win32WindowTitleReader : IWindowTitleReader
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public IntPtr FindMainWindowHandle(int processId)
    {
        var handles = new List<IntPtr>();
        EnumWindows((hwnd, lParam) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == processId && IsWindowVisible(hwnd))
            {
                var title = this.ReadTitle(hwnd);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    handles.Add(hwnd);
                }
            }

            return true;
        }, IntPtr.Zero);

        return handles.FirstOrDefault();
    }

    public string ReadTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length == 0)
        {
            return "";
        }

        var sb = new System.Text.StringBuilder(length + 1);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}

internal sealed class Win32KeyboardSender : IKeyboardSender
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    public void SendNextTab()
    {
        const byte VK_CONTROL = 0x11;
        const byte VK_TAB = 0x09;
        const uint KEYEVENTF_KEYUP = 0x0002;

        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event(VK_TAB, 0, 0, 0);
        keybd_event(VK_TAB, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
    }
}

internal sealed class SystemPaneFocusClock : IPaneFocusClock
{
    public void Sleep(int millis)
    {
        Thread.Sleep(millis);
    }
}
