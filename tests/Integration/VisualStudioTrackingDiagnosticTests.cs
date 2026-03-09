using System.Diagnostics;

namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Diagnostic integration test using real Visual Studio to understand the IDE tracking lifecycle.
/// Launches devenv.exe the same way MainForm.ContextMenu does and logs every event.
/// </summary>
public sealed class VisualStudioTrackingDiagnosticTests(ITestOutputHelper output) : IDisposable
{
    private Process? _vsProcess;

    private const string DevenvPath = @"C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\devenv.exe";

    public void Dispose()
    {
        // Don't kill VS — user may want to keep it open
    }

    [StaFact]
    public void Diagnostic_VisualStudio_TrackFullLifecycle()
    {
        if (!File.Exists(DevenvPath))
        {
            Console.Error.WriteLine($"SKIP: Visual Studio not found at {DevenvPath}");
            return;
        }

        const string SessionId = "diag-vs-test";
        var workDir = @"S:\repo\community\copilot-booster\copilot-booster.sln";

        using var hookService = new WindowEventHookService();
        var tracker = new ActiveStatusTracker();

        var sessions = new List<NamedSession>
        {
            new() { Id = SessionId, Summary = "VS Diagnostic Test" }
        };

        // Collect all events for analysis
        var createdWindows = new List<(IntPtr Hwnd, int Pid, string Title)>();
        var titleChanges = new List<(IntPtr Hwnd, string Title)>();
        var foregroundChanges = new List<(IntPtr Hwnd, int Pid, string Title)>();
        var destroyedWindows = new List<IntPtr>();
        string? capturedSessionId = null;

        hookService.WindowCreated += hwnd =>
        {
            int pid = WindowFocusService.GetWindowProcessId(hwnd);
            string title = WindowFocusService.GetWindowTitle(hwnd);
            createdWindows.Add((hwnd, pid, title));

            var sid = tracker.OnWindowCreated(hwnd);
            if (sid != null)
            {
                capturedSessionId = sid;
                Console.Error.WriteLine($"[WindowCreated] CAPTURED! HWND={hwnd}, PID={pid}, Title='{title}', Session={sid}");
            }
        };

        hookService.WindowTitleChanged += (hwnd, title) =>
        {
            titleChanges.Add((hwnd, title));
            tracker.OnWindowTitleChanged(hwnd, title, null);
        };

        hookService.ForegroundChanged += hwnd =>
        {
            int pid = WindowFocusService.GetWindowProcessId(hwnd);
            string title = WindowFocusService.GetWindowTitle(hwnd);
            foregroundChanges.Add((hwnd, pid, title));

            var sid = tracker.OnWindowCreated(hwnd);
            if (sid != null)
            {
                capturedSessionId = sid;
                Console.Error.WriteLine($"[ForegroundChanged] CAPTURED! HWND={hwnd}, PID={pid}, Title='{title}', Session={sid}");
            }
        };

        hookService.WindowDestroyed += hwnd =>
        {
            destroyedWindows.Add(hwnd);
            tracker.OnWindowDestroyed(hwnd);
        };

        hookService.Start();

        // Launch VS the EXACT same way as MainForm.ContextMenu.OnOpenInIde
        Console.Error.WriteLine($"Launching VS: {DevenvPath} \"{workDir}\"");
        this._vsProcess = SessionInteractionManager.OpenInIde(DevenvPath, workDir);
        Assert.NotNull(this._vsProcess);

        int launcherPid = this._vsProcess.Id;
        Console.Error.WriteLine($"Launcher PID: {launcherPid}");

        // Track with launcher PID — same as MainForm
        tracker.TrackProcess(SessionId, new ActiveProcess("Visual Studio", launcherPid, workDir));

        // Phase 1: Immediately check (launcher still alive)
        var text1 = tracker.BuildActiveText(SessionId);
        Console.Error.WriteLine($"[Phase 1 - Immediate] BuildActiveText: '{text1}'");

        // Phase 2: Wait a few seconds for launcher to exit and VS to load
        Console.Error.WriteLine("Waiting 8 seconds for VS to load...");
        var deadline = Environment.TickCount64 + 8000;
        while (Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(50);
        }

        // Check launcher status
        bool launcherExited;
        try { launcherExited = this._vsProcess.HasExited; }
        catch { launcherExited = true; }
        Console.Error.WriteLine($"Launcher exited: {launcherExited}");

        // Phase 3: Check what OnWindowCreated/ForegroundChanged captured
        Console.Error.WriteLine($"capturedSessionId: {capturedSessionId ?? "null"}");
        Console.Error.WriteLine($"Total WindowCreated events: {createdWindows.Count}");
        Console.Error.WriteLine($"Total ForegroundChanged events: {foregroundChanges.Count}");

        // Log foreground events that happened during VS launch
        foreach (var (hwnd, pid, title) in foregroundChanges)
        {
            if (pid == launcherPid || title.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"  FG: HWND={hwnd}, PID={pid}, Title='{title}' {(pid == launcherPid ? "** LAUNCHER PID **" : "")}");
            }
        }

        // Log created windows that happened during VS launch
        foreach (var (hwnd, pid, title) in createdWindows)
        {
            if (pid == launcherPid || title.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"  Created: HWND={hwnd}, PID={pid}, Title='{title}' {(pid == launcherPid ? "** LAUNCHER PID **" : "")}");
            }
        }

        // Phase 4: Check BuildActiveText
        var text2 = tracker.BuildActiveText(SessionId);
        Console.Error.WriteLine($"[Phase 4 - After wait] BuildActiveText: '{text2}'");

        // Phase 5: Try manual FindWindowHandleByPid with launcher PID
        var hwndByLauncherPid = WindowFocusService.FindWindowHandleByPid(launcherPid);
        Console.Error.WriteLine($"FindWindowHandleByPid(launcherPid={launcherPid}): {hwndByLauncherPid}");

        // Phase 6: Try to find VS window by title
        var folderName = Path.GetFileName(workDir.TrimEnd('\\'));
        var hwndByTitle = WindowFocusService.FindWindowHandleByTitle(folderName, "Visual Studio");
        Console.Error.WriteLine($"FindWindowHandleByTitle('{folderName}', 'Visual Studio'): {hwndByTitle}");

        // Also try without secondary substring
        var hwndByTitle2 = WindowFocusService.FindWindowHandleByTitle("Visual Studio", null);
        Console.Error.WriteLine($"FindWindowHandleByTitle('Visual Studio', null): {hwndByTitle2}");

        // Phase 7: Find what process owns the VS window
        if (hwndByTitle2 != IntPtr.Zero)
        {
            int vsPid = WindowFocusService.GetWindowProcessId(hwndByTitle2);
            string vsTitle = WindowFocusService.GetWindowTitle(hwndByTitle2);
            Console.Error.WriteLine($"Real VS window: PID={vsPid}, Title='{vsTitle}'");
            Console.Error.WriteLine($"Launcher PID ({launcherPid}) == Real VS PID ({vsPid}): {launcherPid == vsPid}");
        }

        // The test always passes — it's diagnostic. Read the output.
        Console.Error.WriteLine("\n=== DIAGNOSIS COMPLETE ===");
        Console.Error.WriteLine($"If '{text2}' is empty, the IDE tracking was lost.");
        Console.Error.WriteLine("Check the output above to understand which events fired and why.");
    }
}
