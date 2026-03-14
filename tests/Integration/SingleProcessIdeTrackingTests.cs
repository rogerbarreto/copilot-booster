using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// E2E tests for single-process IDE tracking (VS Code pattern) using IdeSimVSCode.exe.
/// The simulator has a host process that owns ALL windows — launcher instances exit immediately.
/// Tests validate that each session tracks its own window independently.
/// </summary>
public sealed class SingleProcessIdeTrackingTests : IDisposable
{
    private readonly List<Process> _startedProcesses = [];
    private readonly List<string> _tempDirs = [];
    private readonly HashSet<IntPtr> _detectedHwnds = [];

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;

    private static readonly string s_simExe = Path.Combine(
        Path.GetDirectoryName(typeof(SingleProcessIdeTrackingTests).Assembly.Location)!,
        "TestTools", "IdeSimVSCode.exe");

    public void Dispose()
    {
        foreach (var hwnd in this._detectedHwnds)
        {
            _ = SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        foreach (var proc in this._startedProcesses)
        {
            try { if (!proc.HasExited) { proc.Kill(); } } catch { }
            proc.Dispose();
        }

        foreach (var dir in this._tempDirs)
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private string CreateTempDir(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ide-sim-{name}");
        Directory.CreateDirectory(dir);
        this._tempDirs.Add(dir);
        return dir;
    }

    private static LauncherSettings CreateTestSettings()
    {
        var s = LauncherSettings.CreateDefault();
        s.SuppressSave = true;
        return s;
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
        grid.Columns.Add("LastModified", "");
        grid.Columns.Add("Context", "");
        grid.Columns.Add("Active", "Active");
        grid.Columns.Add("GitHub", "GitHub");
        return grid;
    }

    private static void AddRow(DataGridView grid, string sessionId)
    {
        var idx = grid.Rows.Add("", sessionId, "", "", "", "");
        grid.Rows[idx].Tag = sessionId;
    }

    private static string Cell(DataGridView g, int row) =>
        g.Rows[row].Cells[5].Value?.ToString() ?? "";

    private static void Refresh(ActiveStatusTracker t, SessionGridVisuals v, List<NamedSession> s)
    {
        var snap = t.IncrementalRefresh(s);
        v.UpdateGridIncremental(snap);
    }

    /// <summary>
    /// Open 3 IDE windows (single-process host) for different sessions.
    /// Close them one by one. Each close must only clear its own session.
    /// </summary>
    [StaFact]
    public void E2E_ThreeSessions_CloseOneByOne()
    {
        if (!File.Exists(s_simExe))
        {
            Assert.Fail($"IdeSimVSCode.exe not found at {s_simExe}. Build it first.");
        }

        const string S1 = "sim-vsc-1";
        const string S2 = "sim-vsc-2";
        const string S3 = "sim-vsc-3";

        var dir1 = this.CreateTempDir(S1);
        var dir2 = this.CreateTempDir(S2);
        var dir3 = this.CreateTempDir(S3);

        using var hooks = new WindowEventHookService();
        var tracker = new ActiveStatusTracker();
        var grid = CreateGrid();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, S1);
        AddRow(grid, S2);
        AddRow(grid, S3);

        var sessions = new List<NamedSession>
        {
            new() { Id = S1, Summary = "S1" },
            new() { Id = S2, Summary = "S2" },
            new() { Id = S3, Summary = "S3" }
        };

        hooks.WindowCreated += hwnd => { tracker.OnWindowCreated(hwnd); };
        hooks.WindowDestroyed += hwnd => { tracker.OnWindowDestroyed(hwnd); };
        hooks.ForegroundChanged += hwnd => { tracker.OnWindowCreated(hwnd); };
        hooks.Start();

        // Unique ID to isolate the simulator's mutex/pipe from other test runs
        var runId = Guid.NewGuid().ToString("N")[..8];

        // ── Open Session 1 (this starts the host process) ──
        var proc1 = Process.Start(new ProcessStartInfo(s_simExe, $"--id {runId} \"{dir1}\"") { UseShellExecute = true })!;
        this._startedProcesses.Add(proc1);
        tracker.TrackProcess(S1, new ActiveProcess("IDE Code", proc1.Id, dir1));
        WaitAndPump(3000);

        // ── Open Session 2 (launcher exits, host creates window) ──
        var proc2 = Process.Start(new ProcessStartInfo(s_simExe, $"--id {runId} \"{dir2}\"") { UseShellExecute = true })!;
        this._startedProcesses.Add(proc2);
        tracker.TrackProcess(S2, new ActiveProcess("IDE Code", proc2.Id, dir2));
        WaitAndPump(3000);

        // ── Open Session 3 ──
        var proc3 = Process.Start(new ProcessStartInfo(s_simExe, $"--id {runId} \"{dir3}\"") { UseShellExecute = true })!;
        this._startedProcesses.Add(proc3);
        tracker.TrackProcess(S3, new ActiveProcess("IDE Code", proc3.Id, dir3));
        WaitAndPump(3000);

        // ── Assert: All 3 show IDE ──
        Refresh(tracker, visuals, sessions);
        Assert.Contains("IDE Code", Cell(grid, 0));
        Assert.Contains("IDE Code", Cell(grid, 1));
        Assert.Contains("IDE Code", Cell(grid, 2));

        // ── Close Session 2's window via WM_CLOSE (find by folder name in title) ──
        var folder2 = Path.GetFileName(dir2);
        var hwnd2 = WindowFocusService.FindWindowHandleByTitle(folder2, "IDE Code Simulator");
        Assert.NotEqual(IntPtr.Zero, hwnd2);
        _ = SendMessage(hwnd2, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        WaitAndPump(2000);

        // ── Assert: S2 cleared, S1 & S3 still tracked ──
        Refresh(tracker, visuals, sessions);
        Assert.Contains("IDE Code", Cell(grid, 0));
        Assert.Equal("", Cell(grid, 1));
        Assert.Contains("IDE Code", Cell(grid, 2));

        // ── Close Session 1 ──
        var folder1 = Path.GetFileName(dir1);
        var hwnd1 = WindowFocusService.FindWindowHandleByTitle(folder1, "IDE Code Simulator");
        Assert.NotEqual(IntPtr.Zero, hwnd1);
        _ = SendMessage(hwnd1, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        WaitAndPump(2000);

        Refresh(tracker, visuals, sessions);
        Assert.Equal("", Cell(grid, 0));
        Assert.Equal("", Cell(grid, 1));
        Assert.Contains("IDE Code", Cell(grid, 2));

        // ── Close Session 3 (host closes, all done) ──
        var folder3 = Path.GetFileName(dir3);
        var hwnd3 = WindowFocusService.FindWindowHandleByTitle(folder3, "IDE Code Simulator");
        Assert.NotEqual(IntPtr.Zero, hwnd3);
        _ = SendMessage(hwnd3, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        WaitAndPump(2000);

        Refresh(tracker, visuals, sessions);
        Assert.Equal("", Cell(grid, 0));
        Assert.Equal("", Cell(grid, 1));
        Assert.Equal("", Cell(grid, 2));
    }

    private static void WaitAndPump(int ms)
    {
        var deadline = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }
}
