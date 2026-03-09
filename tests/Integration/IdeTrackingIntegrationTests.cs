using System.Diagnostics;

namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// E2E integration tests for IDE tracking lifecycle.
/// Uses notepad.exe as a stand-in for an IDE — it's a separate process with its own window,
/// so WINEVENT_SKIPOWNPROCESS doesn't filter its events.
/// Tests the full scenario matrix: open, detect, close, reopen across multiple sessions.
/// </summary>
public sealed class IdeTrackingIntegrationTests : IDisposable
{
    private readonly List<Process> _startedProcesses = [];
    private readonly HashSet<IntPtr> _detectedHwnds = [];

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;

    public void Dispose()
    {
        foreach (var hwnd in this._detectedHwnds)
        {
            _ = SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        foreach (var proc in this._startedProcesses)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill();
                }
            }
            catch { }
            proc.Dispose();
        }
    }

    private static LauncherSettings CreateTestSettings()
    {
        var settings = LauncherSettings.CreateDefault();
        settings.SuppressSave = true;
        return settings;
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        grid.Columns.Add("Status", "");
        grid.Columns.Add("Name", "Name");
        grid.Columns.Add("CWD", "CWD");
        grid.Columns.Add("LastModified", "LastModified");
        grid.Columns.Add("Context", "Context");
        grid.Columns.Add("Active", "Active");
        return grid;
    }

    private static void AddRow(DataGridView grid, string sessionId)
    {
        var rowIndex = grid.Rows.Add("", sessionId, "", "", "", "");
        grid.Rows[rowIndex].Tag = sessionId;
    }

    /// <summary>
    /// Launches mspaint.exe as a stand-in for an IDE.
    /// mspaint is a reliable win32 process where the PID matches the window owner,
    /// unlike notepad on Windows 11 which is a packaged app with PID redirection.
    /// </summary>
    private Process LaunchIde()
    {
        var proc = Process.Start(new ProcessStartInfo("mspaint.exe")
        {
            UseShellExecute = false
        })!;
        this._startedProcesses.Add(proc);
        return proc;
    }

    /// <summary>
    /// Wires WindowEventHookService events to ActiveStatusTracker the same way MainForm does.
    /// Returns a set that collects affected session IDs for incremental refresh.
    /// </summary>
    private HashSet<string> WireHooks(
        WindowEventHookService hookService,
        ActiveStatusTracker tracker)
    {
        var dirtySessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        hookService.WindowCreated += hwnd =>
        {
            var sid = tracker.OnWindowCreated(hwnd);
            if (sid != null)
            {
                dirtySessionIds.Add(sid);
                this._detectedHwnds.Add(hwnd);
            }
        };

        hookService.WindowDestroyed += hwnd =>
        {
            var affected = tracker.OnWindowDestroyed(hwnd);
            foreach (var id in affected)
            {
                dirtySessionIds.Add(id);
            }
        };

        // ForegroundChanged: when an IDE window gains focus, try to capture its HWND
        // by matching the owning PID against tracked processes.
        hookService.ForegroundChanged += hwnd =>
        {
            var sid = tracker.OnWindowCreated(hwnd);
            if (sid != null)
            {
                dirtySessionIds.Add(sid);
                this._detectedHwnds.Add(hwnd);
            }
        };

        return dirtySessionIds;
    }

    /// <summary>
    /// Launches notepad and tracks it for the given session, then waits for the HWND
    /// to be captured either via hook events or manual PID scan (same as RefreshActiveStatusAsync).
    /// </summary>
    private Process LaunchAndTrackIde(
        string sessionId,
        string ideName,
        ActiveStatusTracker tracker,
        HashSet<string> dirtySessionIds)
    {
        return this.LaunchAndTrackIde(sessionId, ideName, tracker, dirtySessionIds, null);
    }

    private Process LaunchAndTrackIde(
        string sessionId,
        string ideName,
        ActiveStatusTracker tracker,
        HashSet<string> dirtySessionIds,
        string? exePath)
    {
        Process proc;
        if (exePath != null)
        {
            proc = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false })!;
            this._startedProcesses.Add(proc);
        }
        else
        {
            proc = this.LaunchIde();
        }

        tracker.TrackProcess(sessionId, new ActiveProcess(ideName, proc.Id, null));

        // Try event-driven capture first (ForegroundChanged or WindowCreated)
        PumpUntil(() => dirtySessionIds.Contains(sessionId), 3000);

        if (!dirtySessionIds.Contains(sessionId))
        {
            // Fallback: manually scan by PID (what FullRefresh does)
            PumpUntil(() =>
            {
                var hwnd = WindowFocusService.FindWindowHandleByPid(proc.Id);
                if (hwnd != IntPtr.Zero)
                {
                    tracker.OnWindowCreated(hwnd);
                    dirtySessionIds.Add(sessionId);
                    this._detectedHwnds.Add(hwnd);
                    return true;
                }
                return false;
            }, 7000);
        }

        return proc;
    }

    private static string GetActiveCell(DataGridView grid, int rowIndex) =>
        grid.Rows[rowIndex].Cells[5].Value?.ToString() ?? "";

    private static void RefreshGrid(
        ActiveStatusTracker tracker,
        SessionGridVisuals visuals,
        List<NamedSession> sessions)
    {
        var snapshot = tracker.IncrementalRefresh(sessions);
        visuals.UpdateGridIncremental(snapshot);
    }

    /// <summary>
    /// Full scenario test covering the complete lifecycle matrix:
    /// 1. Both sessions empty (no IDE)
    /// 2. Open IDE for session 1 → detected for session 1, empty for session 2
    /// 3. Close IDE for session 1 → empty for both
    /// 4. Open IDE for session 2 → empty for session 1, detected for session 2
    /// 5. Open IDE for session 1 → detected for both
    /// 6. Close both → empty for both
    /// </summary>
    [StaFact]
    public void E2E_FullIdeLifecycleMatrix()
    {
        const string Session1 = "e2e-ide-session-1";
        const string Session2 = "e2e-ide-session-2";

        using var hookService = new WindowEventHookService();
        var tracker = new ActiveStatusTracker();
        var grid = CreateGrid();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, Session1);
        AddRow(grid, Session2);

        var sessions = new List<NamedSession>
        {
            new() { Id = Session1, Summary = "Session 1" },
            new() { Id = Session2, Summary = "Session 2" }
        };

        var dirtySessionIds = this.WireHooks(hookService, tracker);
        hookService.Start();

        // ── Step 1: Both sessions empty ──
        RefreshGrid(tracker, visuals, sessions);
        Assert.Equal("", GetActiveCell(grid, 0));
        Assert.Equal("", GetActiveCell(grid, 1));

        // ── Step 2: Open IDE for session 1 ──
        var ide1 = this.LaunchAndTrackIde(Session1, "VS Code", tracker, dirtySessionIds);

        Assert.True(dirtySessionIds.Contains(Session1),
            "IDE 1 HWND was not captured via OnWindowCreated");

        RefreshGrid(tracker, visuals, sessions);
        Assert.Contains("VS Code", GetActiveCell(grid, 0));
        Assert.Equal("", GetActiveCell(grid, 1));

        // ── Step 3: Close IDE for session 1 ──
        dirtySessionIds.Clear();
        ide1.Kill();

        PumpUntil(() => dirtySessionIds.Contains(Session1), 10000);
        Assert.True(dirtySessionIds.Contains(Session1),
            "WindowDestroyed did not fire for IDE 1");

        RefreshGrid(tracker, visuals, sessions);
        Assert.Equal("", GetActiveCell(grid, 0));
        Assert.Equal("", GetActiveCell(grid, 1));

        // ── Step 4: Open IDE for session 2 ──
        dirtySessionIds.Clear();
        var ide2 = this.LaunchAndTrackIde(Session2, "VS Code Insiders", tracker, dirtySessionIds);

        Assert.True(dirtySessionIds.Contains(Session2),
            "IDE 2 HWND was not captured via OnWindowCreated");

        RefreshGrid(tracker, visuals, sessions);
        Assert.Equal("", GetActiveCell(grid, 0));
        Assert.Contains("VS Code Insiders", GetActiveCell(grid, 1));

        // ── Step 5: Open IDE for session 1 (both sessions now have IDEs) ──
        dirtySessionIds.Clear();
        var ide1b = this.LaunchAndTrackIde(Session1, "Visual Studio", tracker, dirtySessionIds);

        Assert.True(dirtySessionIds.Contains(Session1),
            "IDE 1b HWND was not captured via OnWindowCreated");

        RefreshGrid(tracker, visuals, sessions);
        Assert.Contains("Visual Studio", GetActiveCell(grid, 0));
        Assert.Contains("VS Code Insiders", GetActiveCell(grid, 1));

        // ── Step 5b: Verify tracking SURVIVES a FullRefresh ──
        // This simulates the 45-second timer that runs FullRefresh,
        // which aggressively cleans up dead entries.
        // The IDE windows are still alive — they must not be removed.
        Thread.Sleep(2000); // Give the OS time to settle
        var fullSnapshot = tracker.FullRefresh(sessions);
        visuals.UpdateGridIncremental(fullSnapshot);
        Assert.Contains("Visual Studio", GetActiveCell(grid, 0));
        Assert.Contains("VS Code Insiders", GetActiveCell(grid, 1));

        // ── Step 6: Close both ──
        dirtySessionIds.Clear();
        ide2.Kill();

        PumpUntil(() => dirtySessionIds.Contains(Session2), 10000);

        RefreshGrid(tracker, visuals, sessions);
        Assert.Contains("Visual Studio", GetActiveCell(grid, 0));
        Assert.Equal("", GetActiveCell(grid, 1));

        dirtySessionIds.Clear();
        ide1b.Kill();

        PumpUntil(() => dirtySessionIds.Contains(Session1), 10000);

        RefreshGrid(tracker, visuals, sessions);
        Assert.Equal("", GetActiveCell(grid, 0));
        Assert.Equal("", GetActiveCell(grid, 1));
    }

    /// <summary>
    /// Reproduces the Visual Studio launcher bug:
    /// 1. Track a "launcher" PID that exits immediately
    /// 2. A real IDE window exists under a different PID (not tracked)
    /// 3. FullRefresh runs → launcher PID dead, no HWND captured → entry removed
    /// 4. The IDE disappears from the grid even though its window is still open
    ///
    /// The real IDE window exists but was never associated because its PID differs from the launcher PID.
    /// </summary>
    [StaFact]
    public void E2E_LauncherPattern_IdeSurvivesFullRefreshAfterLauncherExits()
    {
        const string SessionId = "e2e-launcher-pattern";
        var workDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        using var hookService = new WindowEventHookService();
        var tracker = new ActiveStatusTracker();
        var grid = CreateGrid();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, SessionId);

        var sessions = new List<NamedSession>
        {
            new() { Id = SessionId, Summary = "Launcher Pattern Test" }
        };

        var dirtySessionIds = this.WireHooks(hookService, tracker);
        hookService.Start();

        // Start a short-lived "launcher" process (simulates VS launcher that exits immediately)
        var launcher = Process.Start(new ProcessStartInfo("cmd.exe", "/c echo launcher")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        this._startedProcesses.Add(launcher);

        // Start the "real IDE" under a DIFFERENT PID (simulates the real devenv.exe)
        var realIde = this.LaunchIde();

        // Track with the LAUNCHER PID — this is what MainForm does
        tracker.TrackProcess(SessionId, new ActiveProcess("Visual Studio", launcher.Id, workDir));

        // IDE shows initially because launcher PID is alive
        var snapshot1 = tracker.IncrementalRefresh(sessions);
        visuals.UpdateGridIncremental(snapshot1);
        Assert.Contains("Visual Studio", GetActiveCell(grid, 0));

        // Wait for launcher to exit
        launcher.WaitForExit(5000);
        Assert.True(launcher.HasExited, "Launcher should have exited");

        // Give the real IDE time to be fully visible
        PumpUntil(() => false, 2000);

        // Launcher PID is dead. Real IDE has different PID.
        // IncrementalRefresh (no Win32 calls) — the PID check fails, IDE disappears
        var snapshot2 = tracker.IncrementalRefresh(sessions);
        visuals.UpdateGridIncremental(snapshot2);

        // THIS IS THE BUG: After launcher exits, the IDE should still be tracked
        // but it disappears because the real IDE PID was never associated.
        Assert.Contains("Visual Studio", GetActiveCell(grid, 0));
    }

    /// <summary>
    /// E2E using IdeSimVS simulator: mimics real VS opening a folder.
    /// The simulator creates a splash window (HWND #1), destroys it,
    /// creates the main window (HWND #2) with a generic title, then updates
    /// the title — all under the same PID. Window titles are random and
    /// unrelated to the folder path, so no title-based matching can work.
    /// Verifies: open → tracked through splash transition → close → removed.
    /// </summary>
    [StaFact]
    public void E2E_VsSimulator_SplashTransition_OpenAndClose()
    {
        const string Session1 = "e2e-vs-sim-1";
        const string Session2 = "e2e-vs-sim-2";

        var simExe = Path.Combine(
            Path.GetDirectoryName(typeof(IdeTrackingIntegrationTests).Assembly.Location)!,
            "TestTools", "IdeSimVS.exe");

        if (!File.Exists(simExe))
        {
            Assert.Fail($"IdeSimVS.exe not found at {simExe}");
        }

        using var hookService = new WindowEventHookService();
        var tracker = new ActiveStatusTracker();
        var grid = CreateGrid();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, Session1);
        AddRow(grid, Session2);

        var sessions = new List<NamedSession>
        {
            new() { Id = Session1, Summary = "VS Sim 1" },
            new() { Id = Session2, Summary = "VS Sim 2" }
        };

        var dirtySessionIds = this.WireHooks(hookService, tracker);
        hookService.Start();

        // ── Open VS sim for Session 1 ──
        var proc1 = this.LaunchAndTrackIde(Session1, "Visual Studio", tracker, dirtySessionIds, simExe);
        Assert.True(dirtySessionIds.Contains(Session1), "VS Sim 1 not captured");

        RefreshGrid(tracker, visuals, sessions);
        Assert.Contains("Visual Studio", GetActiveCell(grid, 0));
        Assert.Equal("", GetActiveCell(grid, 1));

        // Wait for splash → main transition to complete
        PumpUntil(() => false, 3000);

        // Verify still tracked after splash destroyed
        RefreshGrid(tracker, visuals, sessions);
        Assert.Contains("Visual Studio", GetActiveCell(grid, 0));

        // ── Open VS sim for Session 2 ──
        dirtySessionIds.Clear();
        var proc2 = this.LaunchAndTrackIde(Session2, "Visual Studio", tracker, dirtySessionIds, simExe);
        Assert.True(dirtySessionIds.Contains(Session2), "VS Sim 2 not captured");
        PumpUntil(() => false, 3000);

        RefreshGrid(tracker, visuals, sessions);
        Assert.Contains("Visual Studio", GetActiveCell(grid, 0));
        Assert.Contains("Visual Studio", GetActiveCell(grid, 1));

        // ── Close Session 1 — Session 2 must remain ──
        dirtySessionIds.Clear();
        proc1.Kill();
        PumpUntil(() => dirtySessionIds.Contains(Session1), 10000);

        RefreshGrid(tracker, visuals, sessions);
        Assert.Equal("", GetActiveCell(grid, 0));
        Assert.Contains("Visual Studio", GetActiveCell(grid, 1));

        // ── Close Session 2 ──
        dirtySessionIds.Clear();
        proc2.Kill();
        PumpUntil(() => dirtySessionIds.Contains(Session2), 10000);

        RefreshGrid(tracker, visuals, sessions);
        Assert.Equal("", GetActiveCell(grid, 0));
        Assert.Equal("", GetActiveCell(grid, 1));
    }

    private static void PumpUntil(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }
}
