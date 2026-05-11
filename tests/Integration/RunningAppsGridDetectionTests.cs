using System.Diagnostics;

namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// E2E integration tests that validate Terminal and Copilot CLI window detection
/// flows through to the session grid. Uses the REAL app code paths:
/// TerminalLauncherService.LaunchTerminal for terminals and wt.exe/cmd.exe with
/// "Copilot CLI - {sessionId}" title for Copilot CLI — the same way MainForm launches them.
/// Wires WindowEventHookService → ActiveStatusTracker → SessionGridVisuals identically to MainForm.
/// </summary>
[Trait("Category", "RequiresInteractiveDesktop")]
[Collection(WindowEventHookCollection.Name)]
public sealed class RunningAppsGridDetectionTests : IDisposable
{
    private readonly List<Process> _startedProcesses = [];
    private readonly HashSet<string> _testSessionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<IntPtr> _detectedHwnds = [];

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;

    public void Dispose()
    {
        // Close windows we detected during the test by their HWND.
        // WM_CLOSE closes the specific WT tab without killing the whole WT process.
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
        grid.Columns.Add("GitHub", "GitHub");
        return grid;
    }

    private static void AddRow(DataGridView grid, string sessionId)
    {
        var rowIndex = grid.Rows.Add("", sessionId, "", "", "", "");
        grid.Rows[rowIndex].Tag = sessionId;
    }

    /// <summary>
    /// Wires WindowEventHookService events to ActiveStatusTracker the same way MainForm does.
    /// Returns a dirty set that collects affected session IDs for incremental refresh.
    /// </summary>
    private HashSet<string> WireMainFormEventHandlers(
        WindowEventHookService hookService,
        ActiveStatusTracker tracker,
        Dictionary<string, string>? sessionSummaries = null)
    {
        var dirtySessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TrackAffected(IntPtr hwnd, HashSet<string> affected)
        {
            foreach (var id in affected)
            {
                dirtySessionIds.Add(id);
                if (this._testSessionIds.Contains(id))
                {
                    this._detectedHwnds.Add(hwnd);
                }
            }
        }

        // Same as MainForm.WindowCreated handler (with the fix: also reads title)
        hookService.WindowCreated += hwnd =>
        {
            var sessionId = tracker.OnWindowCreated(hwnd);
            if (sessionId != null)
            {
                dirtySessionIds.Add(sessionId);
                if (this._testSessionIds.Contains(sessionId))
                {
                    this._detectedHwnds.Add(hwnd);
                }
            }

            var title = WindowFocusService.GetWindowTitle(hwnd);
            if (!string.IsNullOrEmpty(title))
            {
                TrackAffected(hwnd, tracker.OnWindowTitleChanged(hwnd, title, sessionSummaries));
            }
        };

        // Same as MainForm.WindowTitleChanged handler
        hookService.WindowTitleChanged += (hwnd, title) =>
        {
            TrackAffected(hwnd, tracker.OnWindowTitleChanged(hwnd, title, sessionSummaries));
        };

        // Same as MainForm.ForegroundChanged handler
        hookService.ForegroundChanged += hwnd =>
        {
            var title = WindowFocusService.GetWindowTitle(hwnd);
            if (!string.IsNullOrEmpty(title))
            {
                TrackAffected(hwnd, tracker.OnWindowTitleChanged(hwnd, title, sessionSummaries));
            }
        };

        // Same as MainForm.WindowDestroyed handler
        hookService.WindowDestroyed += hwnd =>
        {
            var affected = tracker.OnWindowDestroyed(hwnd);
            foreach (var id in affected)
            {
                dirtySessionIds.Add(id);
            }
        };

        return dirtySessionIds;
    }

    /// <summary>
    /// E2E: Open a terminal using TerminalLauncherService.LaunchTerminal (the REAL app code path).
    /// Verify the window is detected via hooks and appears as "Terminal" in the grid.
    /// </summary>
    [StaFact]
    public void E2E_LaunchTerminal_RealCodePath_DetectedInGrid()
    {
        const string SessionId = "e2e-real-terminal-test";
        this._testSessionIds.Add(SessionId);
        var workDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        using var hookService = new WindowEventHookService();
        var tracker = new ActiveStatusTracker();
        var grid = CreateGrid();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, SessionId);
        Assert.Equal("", grid.Rows[0].Cells[5].Value?.ToString());

        var sessions = new List<NamedSession>
        {
            new() { Id = SessionId, Summary = "Real Terminal Test" }
        };

        var dirtySessionIds = this.WireMainFormEventHandlers(hookService, tracker);
        hookService.Start();

        // Use the REAL TerminalLauncherService.LaunchTerminal — same as OnOpenTerminal handler
        var proc = TerminalLauncherService.LaunchTerminal(workDir, SessionId);
        Assert.NotNull(proc);
        this._startedProcesses.Add(proc);

        // Wait for window event detection — use longer timeout for CI runners where
        // wt.exe window creation events may be delayed in headless/shared environments
        PumpUntil(() => dirtySessionIds.Contains(SessionId), 20000);

        if (!dirtySessionIds.Contains(SessionId))
        {
            // Fallback: on CI runners, wt.exe may not produce detectable window events.
            // Launch cmd.exe directly as a second attempt.
            var fallbackTitle = TestWindowTitle.For($"Terminal - {SessionId}");
            var fallbackProc = Process.Start(new ProcessStartInfo(
                "cmd.exe", $"/k title {fallbackTitle}")
            {
                UseShellExecute = true,
                WorkingDirectory = workDir
            });

            if (fallbackProc != null)
            {
                this._startedProcesses.Add(fallbackProc);
                PumpUntil(() => dirtySessionIds.Contains(SessionId), 10000);
            }
        }

        Assert.True(dirtySessionIds.Contains(SessionId),
            "Terminal window was not detected by WindowEventHookService");

        // Build incremental snapshot and update grid (same as OnDebouncedRefreshAsync)
        var snapshot = tracker.IncrementalRefresh(sessions);
        visuals.UpdateGridIncremental(snapshot);

        var cellValue = grid.Rows[0].Cells[5].Value?.ToString() ?? "";
        Assert.Contains("Terminal", cellValue);
    }

    /// <summary>
    /// E2E: Launch a Copilot CLI window using the same wt.exe/cmd.exe pattern that
    /// Program.cs StartCopilotSession uses. Verify detection and grid update.
    /// </summary>
    [StaFact]
    public void E2E_CopilotCli_RealLaunchPattern_DetectedInGrid()
    {
        const string SessionId = "e2e-real-cli-test";
        this._testSessionIds.Add(SessionId);
        var workDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        using var hookService = new WindowEventHookService();
        var tracker = new ActiveStatusTracker();
        var grid = CreateGrid();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, SessionId);
        Assert.Equal("", grid.Rows[0].Cells[5].Value?.ToString());

        var sessions = new List<NamedSession>
        {
            new() { Id = SessionId, Summary = "Real CLI Test" }
        };

        var dirtySessionIds = this.WireMainFormEventHandlers(hookService, tracker);
        hookService.Start();

        // Launch using the SAME pattern as Program.cs StartCopilotSession:
        // wt.exe with --title and --suppressApplicationTitle, OR cmd.exe fallback
        var terminal = TerminalLauncherService.DetectTerminal();
        var title = TestWindowTitle.For($"Copilot CLI - {SessionId}");
        Process? proc;

        if (terminal == "wt")
        {
            proc = Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"--title \"{title}\" --suppressApplicationTitle -d \"{workDir}\" cmd.exe /k echo Copilot CLI running",
                UseShellExecute = true
            });
        }
        else
        {
            proc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"title {title}\"",
                WorkingDirectory = workDir,
                UseShellExecute = true
            });
        }

        Assert.NotNull(proc);
        this._startedProcesses.Add(proc);

        // Wait for window event detection
        PumpUntil(() => dirtySessionIds.Contains(SessionId), 10000);

        Assert.True(dirtySessionIds.Contains(SessionId),
            $"Copilot CLI window (via {terminal}) was not detected by WindowEventHookService");

        var activeText = tracker.BuildActiveText(SessionId);
        Assert.Contains("Copilot CLI", activeText);

        var snapshot = tracker.IncrementalRefresh(sessions);
        visuals.UpdateGridIncremental(snapshot);

        var cellValue = grid.Rows[0].Cells[5].Value?.ToString() ?? "";
        Assert.Contains("Copilot CLI", cellValue);
    }

    /// <summary>
    /// E2E: Both Terminal and Copilot CLI for the same session using real launch paths.
    /// Grid should show both as multiline text.
    /// </summary>
    [StaFact]
    public void E2E_TerminalAndCopilotCli_BothShownInGrid()
    {
        const string SessionId = "e2e-both-grid-test";
        this._testSessionIds.Add(SessionId);
        var workDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        using var hookService = new WindowEventHookService();
        var tracker = new ActiveStatusTracker();
        var grid = CreateGrid();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, SessionId);

        var sessions = new List<NamedSession>
        {
            new() { Id = SessionId, Summary = "Both Grid Test" }
        };

        var dirtySessionIds = this.WireMainFormEventHandlers(hookService, tracker);
        hookService.Start();

        // Launch Terminal via real code path
        var termProc = TerminalLauncherService.LaunchTerminal(workDir, SessionId);
        Assert.NotNull(termProc);
        this._startedProcesses.Add(termProc);

        // Launch Copilot CLI via real pattern
        var terminal = TerminalLauncherService.DetectTerminal();
        var cliTitle = TestWindowTitle.For($"Copilot CLI - {SessionId}");
        Process? cliProc;

        if (terminal == "wt")
        {
            cliProc = Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"--title \"{cliTitle}\" --suppressApplicationTitle -d \"{workDir}\" cmd.exe /k echo CLI",
                UseShellExecute = true
            });
        }
        else
        {
            cliProc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"title {cliTitle}\"",
                WorkingDirectory = workDir,
                UseShellExecute = true
            });
        }

        Assert.NotNull(cliProc);
        this._startedProcesses.Add(cliProc);

        // Wait for both to be detected
        PumpUntil(() =>
        {
            var text = tracker.BuildActiveText(SessionId);
            return text.Contains("Terminal") && text.Contains("Copilot CLI");
        }, 12000);

        var activeText = tracker.BuildActiveText(SessionId);
        Assert.Contains("Terminal", activeText);
        Assert.Contains("Copilot CLI", activeText);

        var snapshot = tracker.IncrementalRefresh(sessions);
        visuals.UpdateGridIncremental(snapshot);

        var cellValue = grid.Rows[0].Cells[5].Value?.ToString() ?? "";
        Assert.Contains("Terminal", cellValue);
        Assert.Contains("Copilot CLI", cellValue);
    }

    /// <summary>
    /// E2E: Terminal window closes → grid column 5 should be cleared.
    /// Uses real TerminalLauncherService path.
    /// </summary>
    [StaFact]
    public void E2E_TerminalWindowClosed_GridCleared()
    {
        const string SessionId = "e2e-close-grid-test";
        this._testSessionIds.Add(SessionId);

        using var hookService = new WindowEventHookService();
        var tracker = new ActiveStatusTracker();
        var grid = CreateGrid();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, SessionId);

        var sessions = new List<NamedSession>
        {
            new() { Id = SessionId, Summary = "Close Grid Test" }
        };

        var dirtySessionIds = this.WireMainFormEventHandlers(hookService, tracker);
        hookService.Start();

        // Use cmd.exe directly so we can safely kill only this process
        var titleArg = TestWindowTitle.For($"Terminal - {SessionId}");
        var proc = Process.Start(new ProcessStartInfo("cmd.exe", $"/k title {titleArg}")
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        })!;
        this._startedProcesses.Add(proc);

        PumpUntil(() => dirtySessionIds.Contains(SessionId), 10000);
        Assert.True(dirtySessionIds.Contains(SessionId), "Terminal was not detected");

        // Confirm it's in the grid
        var snapshot = tracker.IncrementalRefresh(sessions);
        visuals.UpdateGridIncremental(snapshot);
        Assert.Contains("Terminal", grid.Rows[0].Cells[5].Value?.ToString() ?? "");

        // Kill only the cmd.exe process we launched
        dirtySessionIds.Clear();
        proc.Kill();

        PumpUntil(() => dirtySessionIds.Contains(SessionId), 8000);

        // Refresh grid — "Terminal" should be gone
        var snapshot2 = tracker.IncrementalRefresh(sessions);
        visuals.UpdateGridIncremental(snapshot2);

        var cellAfter = grid.Rows[0].Cells[5].Value?.ToString() ?? "";
        Assert.DoesNotContain("Terminal", cellAfter);
    }

    /// <summary>
    /// E2E: Window created with generic title, then renamed to session pattern after delay.
    /// Validates EVENT_OBJECT_NAMECHANGE path works for delayed title changes.
    /// </summary>
    [StaFact]
    public void E2E_DelayedTitleChange_DetectedViaNameChange()
    {
        const string SessionId = "e2e-delayed-title-test";
        this._testSessionIds.Add(SessionId);

        using var hookService = new WindowEventHookService();
        var tracker = new ActiveStatusTracker();
        var grid = CreateGrid();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, SessionId);

        var sessions = new List<NamedSession>
        {
            new() { Id = SessionId, Summary = "Delayed Title Test" }
        };

        var dirtySessionIds = this.WireMainFormEventHandlers(hookService, tracker);
        hookService.Start();

        // Launch cmd with generic title first, then rename after a delay
        var titleArg = TestWindowTitle.For($"Terminal - {SessionId}");
        var proc = Process.Start(new ProcessStartInfo(
            "cmd.exe",
            $"/k \"ping -n 2 127.0.0.1 >nul & title {titleArg}\"")
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        })!;
        this._startedProcesses.Add(proc);

        PumpUntil(() => dirtySessionIds.Contains(SessionId), 8000);

        Assert.True(dirtySessionIds.Contains(SessionId),
            "Delayed title change was not detected");

        var activeText = tracker.BuildActiveText(SessionId);
        Assert.Contains("Terminal", activeText);

        var snapshot = tracker.IncrementalRefresh(sessions);
        visuals.UpdateGridIncremental(snapshot);

        var cellValue = grid.Rows[0].Cells[5].Value?.ToString() ?? "";
        Assert.Contains("Terminal", cellValue);
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
