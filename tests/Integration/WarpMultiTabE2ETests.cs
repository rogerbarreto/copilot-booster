using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// E2E coverage for Warp multi-tab focusing with spawned warp.exe process.
///
/// ⚠️ CRITICAL: Warp is a SINGLE-INSTANCE application. Process.Start("warp.exe") when Warp is
/// already running signals the existing instance to open a new window, then the launcher exits.
/// The returned Process object is useless for cleanup.
///
/// To avoid disrupting Roger's live Warp sessions and prevent window leaks:
///   1. PRE-FLIGHT: If any warp.exe is running before the test, SKIP (cannot spawn safely).
///   2. SPAWN: Snapshot PIDs before/after Process.Start, track ONLY the NEW warp PID.
///   3. CLEANUP: Send WM_CLOSE to tracked HWND (graceful), then Kill ONLY tracked PID if still alive.
///   4. KEYSTROKE TARGETING: Always SetForegroundWindow on the tracked HWND before sending keys.
///
/// This test:
///   1. Spawns a fresh warp.exe instance (ONLY if no warp.exe is running).
///   2. Opens 2-3 additional tabs via Ctrl+Shift+T (Warp's NewTab binding on Windows).
///   3. Sets each tab's title deterministically by sending echo commands.
///   4. Verifies WarpPaneFocuser.TryFocusPane can switch between tabs by title.
///   5. Cleans up the spawned warp.exe process in Dispose() (WM_CLOSE + Kill tracked PID only).
/// </summary>
[Collection(WindowEventHookCollection.Name)]
public sealed class WarpMultiTabE2ETests : IDisposable
{
    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_T = 0x54;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_NEXT = 0x22;
    private const byte VK_PRIOR = 0x21;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint WM_CLOSE = 0x0010;

    private readonly HashSet<int> _preExistingWarpPids = [];
    private readonly HashSet<int> _trackedWarpPids = [];
    private readonly HashSet<IntPtr> _trackedHwnds = [];

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern short VkKeyScanA(char ch);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public void Dispose()
    {
        foreach (var hwnd in this._trackedHwnds)
        {
            try
            {
                SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // Window may have already closed
            }
        }

        Thread.Sleep(2_000);

        foreach (var pid in this._trackedWarpPids)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2_000);
                }
            }
            catch
            {
                // Process may have already exited
            }
        }
    }

    [LocalOnlyStaFact]
    [Trait("Category", "LocalOnly")]
    public void SpawnedWarp_FocusKnownTab_LandsOnExpectedTitle()
    {
        var scenario = this.SetupSpawnedWarpWithMultipleTabs();
        if (scenario == null)
        {
            return;
        }

        var titleReader = new WarpWindowTitleReader();
        var keys = new WarpKeyboardSender();
        var clock = new WarpPaneFocusClock();
        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            WindowFocusService.TryFocusWindowHandle
        );

        var expectedTitle = scenario.Tab2Title;
        var result = focuser.TryFocusPane(scenario.WarpPid, expectedTitle);

        Assert.True(result, $"TryFocusPane should return true for known tab '{expectedTitle}'");

        var finalTitle = titleReader.ReadTitle(scenario.WarpHwnd);
        Assert.Equal(expectedTitle, finalTitle, StringComparer.OrdinalIgnoreCase);
    }

    [LocalOnlyStaFact]
    [Trait("Category", "LocalOnly")]
    public void SpawnedWarp_FocusSecondKnownTab_LandsOnExpectedTitle()
    {
        var scenario = this.SetupSpawnedWarpWithMultipleTabs();
        if (scenario == null)
        {
            return;
        }

        var titleReader = new WarpWindowTitleReader();
        var keys = new WarpKeyboardSender();
        var clock = new WarpPaneFocusClock();
        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            WindowFocusService.TryFocusWindowHandle
        );

        var expectedTitle = scenario.Tab3Title;
        var result = focuser.TryFocusPane(scenario.WarpPid, expectedTitle);

        Assert.True(result, $"TryFocusPane should return true for known tab '{expectedTitle}'");

        var finalTitle = titleReader.ReadTitle(scenario.WarpHwnd);
        Assert.Equal(expectedTitle, finalTitle, StringComparer.OrdinalIgnoreCase);
    }

    [LocalOnlyStaFact]
    [Trait("Category", "LocalOnly")]
    public void SpawnedWarp_FocusUnknownTab_ReturnsFalseAndRestoresOriginal()
    {
        var scenario = this.SetupSpawnedWarpWithMultipleTabs();
        if (scenario == null)
        {
            return;
        }

        var titleReader = new WarpWindowTitleReader();
        var originalTitle = titleReader.ReadTitle(scenario.WarpHwnd);
        var fakeTitle = $"ZZNotARealTab-{Guid.NewGuid():N}";

        var keys = new WarpKeyboardSender();
        var clock = new WarpPaneFocusClock();
        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            WindowFocusService.TryFocusWindowHandle
        );

        var result = focuser.TryFocusPane(scenario.WarpPid, fakeTitle);

        Assert.False(result, "TryFocusPane should return false for unknown tab");

        var finalTitle = titleReader.ReadTitle(scenario.WarpHwnd);
        Assert.Equal(originalTitle, finalTitle, StringComparer.OrdinalIgnoreCase);
    }

    private SpawnedWarpScenario? SetupSpawnedWarpWithMultipleTabs()
    {
        var existingWarpProcesses = Process.GetProcessesByName("warp");
        if (existingWarpProcesses.Length > 0)
        {
            return null;
        }

        var warpExePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Warp",
            "Warp.exe"
        );

        if (!File.Exists(warpExePath))
        {
            return null;
        }

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = warpExePath,
            UseShellExecute = true
        });

        var newWarpPid = 0;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 10_000)
        {
            var currentWarpProcesses = Process.GetProcessesByName("warp");
            var newPids = currentWarpProcesses
                .Where(p => !this._preExistingWarpPids.Contains(p.Id))
                .Select(p => p.Id)
                .ToList();

            if (newPids.Count > 0)
            {
                newWarpPid = newPids[0];
                this._trackedWarpPids.Add(newWarpPid);
                break;
            }

            Thread.Sleep(500);
        }

        if (newWarpPid == 0)
        {
            return null;
        }

        var hwnd = WaitForMainWindow(newWarpPid, timeoutMs: 15_000);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        this._trackedHwnds.Add(hwnd);
        Thread.Sleep(3_000);

        var titleReader = new WarpWindowTitleReader();
        var originalTitle = titleReader.ReadTitle(hwnd);

        if (!FocusWindow(hwnd))
        {
            return null;
        }

        Thread.Sleep(500);

        var tab2Title = $"TAB-2-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var tab3Title = $"TAB-3-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        this.OpenNewTabAndSetTitle(hwnd, tab2Title);
        this.OpenNewTabAndSetTitle(hwnd, tab3Title);

        for (var i = 0; i < 10; i++)
        {
            SendCtrlPageUp();
            Thread.Sleep(200);
            var title = titleReader.ReadTitle(hwnd);
            if (string.Equals(title, originalTitle, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return new SpawnedWarpScenario(newWarpPid, hwnd, originalTitle, tab2Title, tab3Title);
    }

    private void OpenNewTabAndSetTitle(IntPtr hwnd, string title)
    {
        if (!FocusWindow(hwnd))
        {
            return;
        }

        Thread.Sleep(300);

        SendCtrlShiftT();
        Thread.Sleep(2_000);

        var command = $"echo \"{title}\"";
        TypeText(command);
        Thread.Sleep(100);

        keybd_event(VK_RETURN, 0, 0, 0);
        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, 0);

        Thread.Sleep(1_500);
    }

    private static bool FocusWindow(IntPtr hwnd)
    {
        var currentThread = GetCurrentThreadId();
        var windowThread = GetWindowThreadProcessId(hwnd, IntPtr.Zero);

        if (currentThread != windowThread)
        {
            AttachThreadInput(currentThread, windowThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(currentThread, windowThread, false);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }

        Thread.Sleep(100);

        return GetForegroundWindow() == hwnd;
    }

    private static void SendCtrlShiftT()
    {
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event(VK_SHIFT, 0, 0, 0);
        keybd_event(VK_T, 0, 0, 0);
        keybd_event(VK_T, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
    }

    private static void SendCtrlPageUp()
    {
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event(VK_PRIOR, 0, 0, 0);
        keybd_event(VK_PRIOR, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
    }

    private static void TypeText(string text)
    {
        foreach (var c in text)
        {
            var vk = VkKeyScanA(c);
            var keyCode = (byte)(vk & 0xFF);
            var shiftState = (vk >> 8) & 0xFF;

            if ((shiftState & 1) != 0)
            {
                keybd_event(VK_SHIFT, 0, 0, 0);
            }

            keybd_event(keyCode, 0, 0, 0);
            keybd_event(keyCode, 0, KEYEVENTF_KEYUP, 0);

            if ((shiftState & 1) != 0)
            {
                keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, 0);
            }

            Thread.Sleep(10);
        }
    }

    private static IntPtr WaitForMainWindow(int processId, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            var hwnd = FindMainWindowHandleForProcess(processId);
            if (hwnd != IntPtr.Zero)
            {
                return hwnd;
            }

            Thread.Sleep(500);
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindMainWindowHandleForProcess(int processId)
    {
        var handles = new List<IntPtr>();
        EnumWindows((hwnd, lParam) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == processId && IsWindowVisible(hwnd))
            {
                var length = GetWindowTextLength(hwnd);
                if (length > 0)
                {
                    handles.Add(hwnd);
                }
            }

            return true;
        }, IntPtr.Zero);

        return handles.FirstOrDefault();
    }

    private sealed class SpawnedWarpScenario
    {
        public int WarpPid { get; }
        public IntPtr WarpHwnd { get; }
        public string OriginalTitle { get; }
        public string Tab2Title { get; }
        public string Tab3Title { get; }

        public SpawnedWarpScenario(int warpPid, IntPtr warpHwnd, string originalTitle, string tab2Title, string tab3Title)
        {
            this.WarpPid = warpPid;
            this.WarpHwnd = warpHwnd;
            this.OriginalTitle = originalTitle;
            this.Tab2Title = tab2Title;
            this.Tab3Title = tab3Title;
        }
    }

    private sealed class WarpWindowTitleReader : IWindowTitleReader
    {
        public IntPtr FindMainWindowHandle(int processId)
        {
            var handles = new List<IntPtr>();
            EnumWindows((hwnd, lParam) =>
            {
                _ = GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == processId && IsWindowVisible(hwnd))
                {
                    var length = GetWindowTextLength(hwnd);
                    if (length > 0)
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

            var sb = new StringBuilder(length + 1);
            _ = GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }

    private sealed class WarpKeyboardSender : IKeyboardSender
    {
        public void SendNextTab()
        {
            keybd_event(VK_CONTROL, 0, 0, 0);
            keybd_event(VK_NEXT, 0, 0, 0);
            keybd_event(VK_NEXT, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        }
    }

    private sealed class WarpPaneFocusClock : IPaneFocusClock
    {
        public void Sleep(int millis)
        {
            Thread.Sleep(millis);
        }
    }
}
