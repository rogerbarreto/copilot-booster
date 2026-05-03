using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Live E2E coverage for externally-started Copilot CLI sessions hosted in Windows Terminal tabs.
///
/// This test reproduces Roger's exact v0.21.1 repro:
///   1. wt.exe is already running with TWO real copilot.exe tabs (no PowerShell wrapper).
///   2. CopilotBooster starts AFTER wt is fully up, ingests both sessions, renders the grid.
///   3. Clicking each session's "Copilot CLI" link MUST focus its own tab — verified by:
///        a) the WT-selected pane's UIA name contains the session label, and
///        b) the WT window title contains the session label.
///
/// Tab labels are set deterministically by sending `/rename &lt;label&gt;` to copilot's TUI
/// via SendKeys, AFTER copilot reaches its prompt. Copilot owns the title; we don't fake it.
/// </summary>
[Collection(WindowEventHookCollection.Name)]
public sealed class WindowsTerminalMultiPaneE2ETests : IDisposable
{
    private const uint WM_CLOSE = 0x0010;

    private readonly List<int> _copilotPids = [];
    private readonly List<int> _pwshPids = [];
    private readonly List<Process> _startedProcesses = [];
    private readonly HashSet<IntPtr> _wtWindowHwnds = [];
    private readonly HashSet<string> _createdSessionDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _createdSessionIds = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_D = 0x44;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const string SessionCleanupSentinel = ".it-cleanup-copilot-booster";

    public void Dispose()
    {
        this.CleanupProcessesAndWindows();
        this.CleanupCreatedSessionDirs();
    }

    [LocalOnlyFact]
    public async Task TwoRealCopilotTabs_LinkClickFocusesCorrectTabAsync()
    {
        await RunOnStaThreadAsync(this.ExecutePreExistingWtRealCopilotE2EAsync).ConfigureAwait(false);
    }

    private async Task ExecutePreExistingWtRealCopilotE2EAsync()
    {
        SkipIfPreflightFails();
        Directory.CreateDirectory(Program.SessionStateDir);
        Directory.CreateDirectory(Program.AppDataDir);

        // Orphan sweep — earlier IT runs may have crashed before Dispose, leaving
        // session dirs behind that pollute Roger's real booster UI. Any dir that still
        // contains our sentinel file is a known leftover and safe to delete.
        SweepOrphanItSessionDirs();

        var sessionA = new RealCopilotSession("RunTests-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var sessionB = new RealCopilotSession("RunTest2-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        foreach (var s in new[] { sessionA, sessionB })
        {
            // copilot --resume requires a session-state/<id>/ that already contains a valid
            // workspace.yaml AND an events.jsonl whose first event is `session.start`. The
            // template here mirrors the on-disk shape produced by a real copilot session
            // (verified against session 1a9f3df8-f4a0-4207-8e27-ddcd753a2386):
            //   * workspace.yaml — only id/cwd/summary_count/created_at/updated_at (no
            //     git_root, no summary, no name — copilot is strict about extras).
            //   * events.jsonl — opens with a session.start event whose data.sessionId
            //     matches the dir name. Without this header copilot refuses to resume.
            s.SessionId = Guid.NewGuid().ToString();
            s.SessionDir = Path.Combine(Program.SessionStateDir, s.SessionId);
            this._createdSessionDirs.Add(s.SessionDir);
            this._createdSessionIds.Add(s.SessionId);
            Directory.CreateDirectory(s.SessionDir);
            // Sentinel file lets the next test run's orphan sweep find and delete this
            // dir even if the current run crashes before Dispose.
            File.WriteAllText(Path.Combine(s.SessionDir, SessionCleanupSentinel), DateTime.UtcNow.ToString("O"));
            WriteMinimalWorkspaceYaml(Path.Combine(s.SessionDir, "workspace.yaml"), s.SessionId, Environment.CurrentDirectory);
            WriteSessionStartEvent(Path.Combine(s.SessionDir, "events.jsonl"), s.SessionId, Environment.CurrentDirectory);
            SessionNameOverrideService.Set(Program.SessionNameOverrideFile, s.SessionId, s.Label, resolvedFromUserMessage: true);
        }

        var paneGateway = new WindowsTerminalPaneGateway();
        var (wtProcess, wtHwnd) = StartWtAndTypeCopilotInTwoTabs(paneGateway, sessionA, sessionB);
        if (wtProcess != null)
        {
            this._startedProcesses.Add(wtProcess);
        }
        this._wtWindowHwnds.Add(wtHwnd);

        (sessionA.CopilotPid, sessionA.PwshPid) = WaitForCopilotPidByDenyUrl(sessionA.Marker, 30_000);
        (sessionB.CopilotPid, sessionB.PwshPid) = WaitForCopilotPidByDenyUrl(sessionB.Marker, 30_000);
        this._copilotPids.Add(sessionA.CopilotPid);
        this._copilotPids.Add(sessionB.CopilotPid);
        this._pwshPids.Add(sessionA.PwshPid);
        this._pwshPids.Add(sessionB.PwshPid);

        // Wait for copilot to fully load on BOTH tabs. Copilot sets the tab title to
        // "GitHub Copilot" once it's at the prompt — that's the deterministic ready signal.
        WaitUntil(
            () =>
            {
                var panes = paneGateway.EnumeratePanes(wtHwnd).Panes;
                return panes.Count >= 2
                    && panes.Take(2).All(p => p.Name.Contains("GitHub Copilot", StringComparison.OrdinalIgnoreCase));
            },
            45_000,
            "Both wt tabs did not produce the 'GitHub Copilot' title within 45s; copilot may not have started.");

        // Settle past splash so copilot is at the prompt and ready for input.
        Thread.Sleep(2_000);

        // Send /rename <label> to each tab to set deterministic, unique titles.
        // Because we launched with --resume <our-id>, copilot is bound to OUR session dir
        // (which already contains workspace.yaml) — /rename writes into it directly.
        RenameTab(paneGateway, wtHwnd, tabIndex: 0, sessionA.Label);
        RenameTab(paneGateway, wtHwnd, tabIndex: 1, sessionB.Label);

        // Boot the booster tracker AFTER wt is fully up.
        var tracker = new ActiveStatusTracker();
        tracker.HandleExternalSessionDiscovered(sessionA.SessionId, sessionA.CopilotPid);
        tracker.HandleExternalSessionDiscovered(sessionB.SessionId, sessionB.CopilotPid);

        sessionA.Host = tracker.GetCopilotHost(sessionA.SessionId);
        sessionB.Host = tracker.GetCopilotHost(sessionB.SessionId);
        Assert.NotNull(sessionA.Host);
        Assert.NotNull(sessionB.Host);
        Assert.Equal("Windows Terminal", sessionA.Host!.HostKindLabel);
        Assert.Equal("Windows Terminal", sessionB.Host!.HostKindLabel);
        Assert.NotEqual(sessionA.Host.PaneRuntimeId, sessionB.Host.PaneRuntimeId);

        var sessions = SessionService.LoadNamedSessions(
                Program.SessionStateDir,
                Program.PidRegistryFile,
                Program.SessionStateFile,
                Program.SessionAliasFile,
                Program.SessionNameOverrideFile)
            .Where(s => s.Id == sessionA.SessionId || s.Id == sessionB.SessionId)
            .ToList();
        Assert.Equal(2, sessions.Count);

        // Install the production-shape WinEvent hooks so the click→focus path runs
        // against the same state machine MainForm.cs:1001-1083 wires up. Without this
        // the IT tests FocusActiveProcess in isolation while production runs it racing
        // against ForegroundChanged + WindowTitleChanged callbacks that mutate
        // _activeTrackedWindows mid-flight. The hook callbacks dispatch on this STA
        // thread via WINEVENT_OUTOFCONTEXT and only fire when Application.DoEvents()
        // pumps the message queue, identical to MainForm's message-loop semantics.
        using var hookService = new WindowEventHookService();
        hookService.WindowCreated += hwnd =>
        {
            tracker.OnWindowCreated(hwnd);
            var title = WindowFocusService.GetWindowTitle(hwnd);
            if (!string.IsNullOrEmpty(title))
            {
                tracker.OnWindowTitleChanged(hwnd, title, ActiveStatusTracker.BuildSessionSummaryMap(sessions));
            }
        };
        hookService.WindowDestroyed += hwnd =>
        {
            tracker.HandleWindowDestroyed(hwnd);
            tracker.OnWindowDestroyed(hwnd);
        };
        hookService.WindowTitleChanged += (hwnd, title) =>
        {
            tracker.HandleWindowNameChanged(hwnd);
            tracker.OnWindowTitleChanged(hwnd, title, ActiveStatusTracker.BuildSessionSummaryMap(sessions));
        };
        hookService.ForegroundChanged += hwnd =>
        {
            tracker.OnWindowCreated(hwnd);
            var title = WindowFocusService.GetWindowTitle(hwnd);
            if (!string.IsNullOrEmpty(title))
            {
                tracker.OnWindowTitleChanged(hwnd, title, ActiveStatusTracker.BuildSessionSummaryMap(sessions));
            }
        };
        hookService.Start();

        var snapshot = tracker.IncrementalRefresh(sessions);
        using var gridHost = new Form
        {
            Width = 820,
            Height = 280,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(20, 20)
        };
        var grid = CreateGrid();
        grid.Dock = DockStyle.Fill;
        gridHost.Controls.Add(grid);
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());
        visuals.Populate(sessions, snapshot, searchQuery: null);
        gridHost.Show();
        Application.DoEvents();

        var prevSettings = Program._settings;
        Program._settings = CreateTestSettings();
        try
        {
            AssertClickFocusesCorrectTab(grid, paneGateway, wtHwnd, sessionA);
            AssertClickFocusesCorrectTab(grid, paneGateway, wtHwnd, sessionB);

            // Phase 3 — Stale-host recovery (v0.21.1 bug repro).
            //
            // Production scenario: WindowHandleCacheService rehydrates _copilotHosts from
            // disk before wt is up, OR a prior resolution captured a non-wt ancestor (e.g.
            // a transient pwsh window owned by ConPTY tooling). The cached CopilotHostInfo
            // therefore carries HostKindLabel != "Windows Terminal" even though copilot is
            // physically inside a real wt pane. When the user clicks the session link,
            // FocusCopilotHost evaluates IsWindowsTerminalHost(hostInfo) → false, falls
            // through to plain _focusWindowHandle(hostInfo.HostHwnd), and never selects
            // the correct wt pane.
            //
            // We simulate this by overwriting sessionA's resolved entry with a stale,
            // non-wt CopilotHostInfo via the SetCopilotHost test seam. HostHwnd points at
            // the live wt window so IsCopilotHostActive passes (window + pid alive), but
            // HostKindLabel/HostProcessName look like a Console host so the wt-pane fast
            // path is skipped. Going into phase 3 sessionB's tab is currently selected
            // (from the click above), so a click on sessionA's link must move wt off
            // sessionB's tab and onto sessionA's tab to pass.
            var staleSessionAHost = new CopilotHostInfo(
                HostHwnd: wtHwnd,
                HostPid: sessionA.PwshPid,
                CopilotPid: sessionA.CopilotPid,
                HostProcessName: "pwsh",
                HostKindLabel: "Console",
                ParentHostHwnd: IntPtr.Zero,
                PaneTitle: null,
                PaneRuntimeId: null,
                PaneRootProcessId: null);
            tracker.SetCopilotHost(sessionA.SessionId, staleSessionAHost);

            // Re-render the grid so the row for sessionA reflects the stale host info.
            var staleSnapshot = tracker.IncrementalRefresh(sessions);
            visuals.Populate(sessions, staleSnapshot, searchQuery: null);
            Application.DoEvents();

            AssertClickFocusesCorrectTab(grid, paneGateway, wtHwnd, sessionA);
        }
        finally
        {
            Program._settings = prevSettings;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static LauncherSettings CreateTestSettings()
    {
        var settings = LauncherSettings.CreateDefault();
        settings.SuppressSave = true;
        return settings;
    }

    private static TestDataGridView CreateGrid()
    {
        var grid = new TestDataGridView
        {
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            Width = 760,
            Height = 220
        };
        grid.Columns.Add("Status", string.Empty);
        grid.Columns.Add("Session", "Session");
        grid.Columns.Add("CWD", "CWD");
        grid.Columns.Add("Date", "Date");
        grid.Columns.Add("Context", "Context");
        grid.Columns.Add("RunningApps", "RunningApps");
        grid.Columns.Add("GitHub", "GitHub");
        return grid;
    }

    private static void ClickCopilotCliLink(TestDataGridView grid, string sessionId)
    {
        var row = Assert.Single(
            grid.Rows.Cast<DataGridViewRow>(),
            candidate => string.Equals(candidate.Tag as string, sessionId, StringComparison.OrdinalIgnoreCase));
        var cellBounds = grid.GetCellDisplayRectangle(5, row.Index, false);
        var activeText = row.Cells[5].Value?.ToString() ?? string.Empty;
        var font = row.Cells[5].InheritedStyle.Font ?? grid.Font;
        using var linkFont = new Font(font, FontStyle.Underline);
        var lineHeight = TextRenderer.MeasureText("X", linkFont).Height;
        var textSize = TextRenderer.MeasureText(activeText.Split('\n')[0], linkFont);
        int x = ((cellBounds.Width - textSize.Width) / 2) + (textSize.Width / 2);
        int y = ((cellBounds.Height - lineHeight) / 2) + (lineHeight / 2);
        grid.PerformCellMouseClick(5, row.Index, x, y);
        Application.DoEvents();
    }

    private static void SkipIfPreflightFails()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Live Windows Terminal multi-pane test is local-only and does not run on CI runners.");
        }

        if (!Environment.UserInteractive || Process.GetCurrentProcess().SessionId == 0 || GetForegroundWindow() == IntPtr.Zero)
        {
            Assert.Skip("Live Windows Terminal multi-pane test requires an interactive desktop session.");
        }

        var wtWhere = RunProcess("where.exe", "wt.exe", 10_000);
        if (wtWhere.ExitCode != 0)
        {
            Assert.Skip("wt.exe is not on PATH.");
        }

        var copilotHelp = RunProcess("copilot", "--help", 10_000);
        if (copilotHelp.TimedOut)
        {
            Assert.Skip("copilot --help timed out; cannot verify --deny-url support.");
        }

        if (copilotHelp.ExitCode != 0)
        {
            Assert.Skip($"copilot --help failed with exit code {copilotHelp.ExitCode}; cannot run live CLI test.");
        }

        var helpText = copilotHelp.Stdout + copilotHelp.Stderr;
        if (!helpText.Contains("--deny-url", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("copilot CLI on PATH does not advertise --deny-url support.");
        }
    }

    /// <summary>
    /// Launches wt.exe with the user's default profile (no command argument), opens a second tab
    /// via Ctrl+Shift+T, then types <c>copilot --deny-url=&lt;marker&gt;</c> into each tab. This is
    /// the only flow that mirrors a real user opening Windows Terminal and starting copilot from
    /// the shell prompt — it exercises the real default-shell → "GitHub Copilot" title transition.
    /// Launching with <c>wt new-tab copilot</c> (or similar) locks the tab title to the command
    /// name and never triggers copilot's ESC]0; title-set, so it does not represent reality.
    /// </summary>
    private static (Process? WtLauncher, IntPtr WtHwnd) StartWtAndTypeCopilotInTwoTabs(
        WindowsTerminalPaneGateway gateway,
        RealCopilotSession a,
        RealCopilotSession b)
    {
        // Windows Terminal uses a single "monarch" process that hosts all windows, so a new wt
        // window does NOT create a new WindowsTerminal.exe PID — it adds a new top-level HWND
        // to the existing process. Snapshot HWNDs (not PIDs) to detect the window we spawn.
        var existingWtHwnds = EnumerateWindowsTerminalHwnds().ToHashSet();

        // `-w new` forces a fresh wt window even if global glomming is enabled.
        var launcher = Process.Start(new ProcessStartInfo
        {
            FileName = "wt.exe",
            Arguments = "-w new",
            UseShellExecute = false,
            CreateNoWindow = false
        });

        IntPtr wtHwnd = IntPtr.Zero;
        WaitUntil(
            () =>
            {
                foreach (var hwnd in EnumerateWindowsTerminalHwnds())
                {
                    if (!existingWtHwnds.Contains(hwnd))
                    {
                        wtHwnd = hwnd;
                        return true;
                    }
                }
                return false;
            },
            20_000,
            "Newly-spawned wt.exe window did not appear within 20s.");

        // Wait for the default-profile shell in tab 0 to register a UIA pane.
        WaitUntil(
            () => gateway.EnumeratePanes(wtHwnd).Panes.Count >= 1,
            15_000,
            "wt.exe first tab's UIA pane did not appear within 15s.");
        Thread.Sleep(1_500);

        // Open a second tab via Ctrl+Shift+T (the wt-level chrome shortcut, handled before the cell).
        Assert.True(WindowFocusService.TryFocusWindowHandle(wtHwnd), "Could not foreground wt before Ctrl+Shift+T.");
        Thread.Sleep(250);
        SendKeys.SendWait("^+t");

        WaitUntil(
            () => gateway.EnumeratePanes(wtHwnd).Panes.Count >= 2,
            15_000,
            "wt.exe second tab did not appear within 15s after Ctrl+Shift+T.");
        Thread.Sleep(1_500);

        // Type the copilot launch command into each tab. `--resume "<sessionId>"` binds copilot
        // to the session-state/<id>/ dir we pre-created (with workspace.yaml already populated);
        // the GUID is quoted because copilot's CLI expects a quoted session id argument.
        // `--deny-url=<marker>` is purely a unique CLI arg used for WMI PID correlation. Once
        // copilot reaches its prompt it overrides the tab title to "GitHub Copilot" via ESC]0;.
        TypeIntoTab(gateway, wtHwnd, tabIndex: 0, $"copilot --resume \"{a.SessionId}\" --deny-url={a.Marker}");
        TypeIntoTab(gateway, wtHwnd, tabIndex: 1, $"copilot --resume \"{b.SessionId}\" --deny-url={b.Marker}");

        return (launcher, wtHwnd);
    }

    private static List<IntPtr> EnumerateWindowsTerminalHwnds()
    {
        var wtPids = Process.GetProcessesByName("WindowsTerminal").Select(p => (uint)p.Id).ToHashSet();
        var hwnds = new List<IntPtr>();
        if (wtPids.Count == 0)
        {
            return hwnds;
        }

        EnumWindows((hWnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                var threadId = GetWindowThreadProcessId(hWnd, out var pid);
                if (threadId == 0 || !wtPids.Contains(pid))
                {
                    return true;
                }

                var sb = new StringBuilder(256);
                if (GetClassName(hWnd, sb, sb.Capacity) > 0
                    && sb.ToString().Contains("CASCADIA", StringComparison.OrdinalIgnoreCase))
                {
                    hwnds.Add(hWnd);
                }
            }
            catch
            {
            }
            return true;
        }, IntPtr.Zero);

        return hwnds;
    }

    private static void TypeIntoTab(WindowsTerminalPaneGateway gateway, IntPtr wtHwnd, int tabIndex, string command)
    {
        var panes = gateway.EnumeratePanes(wtHwnd).Panes;
        Assert.True(panes.Count > tabIndex, $"WT only has {panes.Count} tabs; cannot type into tab {tabIndex}.");
        var target = panes[tabIndex];

        Assert.True(gateway.FocusPane(wtHwnd, target.RuntimeId!), $"Could not focus tab {tabIndex} via UIA before typing.");
        Thread.Sleep(250);
        Assert.True(WindowFocusService.TryFocusWindowHandle(wtHwnd), "Could not foreground wt before typing.");
        Thread.Sleep(250);

        SendKeys.SendWait(SendKeysEscape(command) + "{ENTER}");
        Thread.Sleep(400);
    }

    private static (int CopilotPid, int PwshPid) WaitForCopilotPidByDenyUrl(string marker, int timeoutMs)
    {
        (int copilot, int pwsh)? result = null;
        WaitUntil(
            () => { result = TryFindCopilotPidByDenyUrl(marker); return result.HasValue; },
            timeoutMs,
            $"Copilot process with --deny-url={marker} did not appear within {timeoutMs}ms.");
        return result!.Value;
    }

    private static (int CopilotPid, int PwshPid)? TryFindCopilotPidByDenyUrl(string marker)
    {
        // Capture both the copilot.exe PID and its parent (pwsh.exe / cmd.exe) at discovery
        // time. Disposal must kill BOTH — killing only copilot leaves pwsh prompting forever
        // inside the wt tab, which keeps the wt window alive after the test.
        var ps = "(Get-CimInstance Win32_Process -Filter \\\"Name='copilot.exe'\\\" | "
            + "Where-Object { $_.CommandLine -like '*" + marker + "*' } | "
            + "Select-Object -First 1 ProcessId, ParentProcessId) | "
            + "ForEach-Object { \\\"$($_.ProcessId),$($_.ParentProcessId)\\\" }";
        var result = RunProcess("powershell.exe", $"-NoLogo -NoProfile -Command \"{ps}\"", 5_000);
        var stdout = result.Stdout.Trim();
        if (string.IsNullOrEmpty(stdout))
        {
            return null;
        }

        var parts = stdout.Split(',', 2);
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var copilotPid) || copilotPid <= 0
            || !int.TryParse(parts[1], out var parentPid) || parentPid <= 0)
        {
            return null;
        }

        return (copilotPid, parentPid);
    }

    private static void RenameTab(WindowsTerminalPaneGateway gateway, IntPtr wtHwnd, int tabIndex, string newLabel)
    {
        var initial = gateway.EnumeratePanes(wtHwnd).Panes;
        Assert.True(initial.Count > tabIndex, $"WT only has {initial.Count} tabs; need at least {tabIndex + 1}.");
        var target = initial[tabIndex];

        Assert.True(gateway.FocusPane(wtHwnd, target.RuntimeId!), $"Could not focus tab {tabIndex} via UIA before /rename.");
        Thread.Sleep(250);
        Assert.True(WindowFocusService.TryFocusWindowHandle(wtHwnd), "Could not bring wt.exe to foreground for SendKeys.");
        Thread.Sleep(250);

        SendKeys.SendWait($"/rename {SendKeysEscape(newLabel)}{{ENTER}}");

        WaitUntil(
            () => gateway.EnumeratePanes(wtHwnd).Panes.Any(p => p.Name.Contains(newLabel, StringComparison.OrdinalIgnoreCase)),
            10_000,
            $"Tab title did not become '{newLabel}' after sending '/rename {newLabel}<Enter>'. Current tabs: "
            + string.Join(" | ", gateway.EnumeratePanes(wtHwnd).Panes.Select(p => $"'{p.Name}'")));
    }

    private static string SendKeysEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if ("+^%~(){}".IndexOf(c) >= 0) { sb.Append('{').Append(c).Append('}'); }
            else { sb.Append(c); }
        }
        return sb.ToString();
    }

    private static void AssertClickFocusesCorrectTab(TestDataGridView grid, WindowsTerminalPaneGateway gateway, IntPtr wtHwnd, RealCopilotSession session)
    {
        ClickCopilotCliLink(grid, session.SessionId);

        // Pump the message loop while wt processes focus + UIA Select. Hook callbacks
        // (WINEVENT_OUTOFCONTEXT) only dispatch when DoEvents runs, identical to
        // MainForm's message-loop semantics. Without this, ForegroundChanged events
        // queue up but never fire — the IT would silently skip the production race.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(50);
        }

        var enumeration = gateway.EnumeratePanes(wtHwnd);
        var selected = enumeration.Panes.FirstOrDefault(p => p.IsSelected);
        var details = string.Join(" | ", enumeration.Panes.Select(p => $"Name='{p.Name}' Selected={p.IsSelected}"));

        Assert.True(
            selected != null && selected.Name.Contains(session.Label, StringComparison.OrdinalIgnoreCase),
            $"After clicking 'Copilot CLI' on session '{session.Label}': selected tab is '{selected?.Name}', not the one matching '{session.Label}'. Tabs: {details}.");

        var windowTitle = WindowFocusService.GetWindowTitle(wtHwnd);
        Assert.True(
            windowTitle.Contains(session.Label, StringComparison.OrdinalIgnoreCase),
            $"After clicking 'Copilot CLI' on session '{session.Label}': WT window title is '{windowTitle}'; expected to contain '{session.Label}'. Tabs: {details}.");
    }

    private static void WriteMinimalWorkspaceYaml(string wsFile, string sessionId, string cwd)
    {
        // Mirrors the exact on-disk shape of a real copilot workspace.yaml
        // (session 1a9f3df8 captured 2026-05-03). Five fields, no git_root,
        // no summary, no name. copilot --resume rejects extras.
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var lines = new[]
        {
            $"id: {sessionId}",
            $"cwd: {cwd}",
            "summary_count: 0",
            $"created_at: {now}",
            $"updated_at: {now}",
        };
        File.WriteAllLines(wsFile, lines);
    }

    private static void WriteSessionStartEvent(string eventsJsonl, string sessionId, string cwd)
    {
        // Mirrors the first line of a real copilot events.jsonl (session 1a9f3df8).
        // Without a session.start header copilot --resume refuses to load the session.
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var payload = JsonSerializer.Serialize(new
        {
            type = "session.start",
            data = new
            {
                sessionId,
                version = 1,
                producer = "copilot-agent",
                copilotVersion = "1.0.40",
                startTime = now,
                context = new { cwd },
                alreadyInUse = false,
                remoteSteerable = false,
            },
            id = Guid.NewGuid().ToString(),
            timestamp = now,
            parentId = (string?)null,
        });
        File.WriteAllText(eventsJsonl, payload + Environment.NewLine, Encoding.UTF8);
    }

    private static ProcessRunResult RunProcess(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) { stdout.AppendLine(e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) { stderr.AppendLine(e.Data); } };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new ProcessRunResult(-1, stdout.ToString(), stderr.ToString(), TimedOut: true);
            }

            process.WaitForExit();
            return new ProcessRunResult(process.ExitCode, stdout.ToString(), stderr.ToString(), TimedOut: false);
        }
        catch (Exception ex)
        {
            return new ProcessRunResult(-1, string.Empty, ex.Message, TimedOut: false);
        }
    }

    private static void WaitUntil(Func<bool> condition, int timeoutMs, string failureMessage)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return;
            }

            Application.DoEvents();
            Thread.Sleep(50);
        }

        Assert.Fail(failureMessage);
    }

    private static Task RunOnStaThreadAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action().GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void CleanupProcessesAndWindows()
    {
        // Kill copilot.exe first (the leaf), then its parent shell. Killing the parent
        // pwsh.exe terminates the tab's process — but wt by default leaves zombie tabs
        // behind ("[process exited with code ...]. You can now close this terminal with
        // Ctrl+D, or press Enter to restart."). To actually close the window we send
        // Ctrl+D once per tab via keybd_event after all pwsh processes are gone.
        foreach (var pid in this._copilotPids.Distinct())
        {
            TryKillProcessTree(pid);
        }

        foreach (var pid in this._pwshPids.Distinct())
        {
            TryKillProcessTree(pid);
        }

        // Each wtHwnd we tracked hosts exactly two zombie tabs (one per session). Ctrl+D
        // on a zombie tab closes it; closing the last tab closes the wt window.
        foreach (var hwnd in this._wtWindowHwnds.Where(hwnd => hwnd != IntPtr.Zero).Distinct())
        {
            TryCloseZombieWtTabs(hwnd, tabCount: 2);
        }

        // Safety net for any window that didn't respond to Ctrl+D (e.g. user changed wt
        // closeOnExit profile setting). WM_CLOSE prompts wt's "close-all-tabs" dialog
        // when a tab still has a running process — but at this point all our processes
        // are dead, so wt accepts the close cleanly.
        foreach (var hwnd in this._wtWindowHwnds.Where(hwnd => hwnd != IntPtr.Zero).Distinct())
        {
            try { _ = SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); } catch { }
        }

        foreach (var process in this._startedProcesses)
        {
            try
            {
                if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                {
                    _ = SendMessage(process.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch { }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void TryKillProcessTree(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }

            process.Dispose();
        }
        catch
        {
            // Process already gone or insufficient privilege — best-effort cleanup.
        }
    }

    private static void TryCloseZombieWtTabs(IntPtr wtHwnd, int tabCount)
    {
        try
        {
            if (!IsWindow(wtHwnd))
            {
                return;
            }

            // Give wt a moment to render the zombie state after pwsh exits.
            Thread.Sleep(300);

            for (var i = 0; i < tabCount; i++)
            {
                if (!IsWindow(wtHwnd))
                {
                    return;
                }

                if (!WindowFocusService.TryFocusWindowHandle(wtHwnd))
                {
                    return;
                }

                Thread.Sleep(150);
                SendCtrlD();
                Thread.Sleep(250);
            }
        }
        catch
        {
            // Best-effort cleanup — never fail Dispose.
        }
    }

    private static void SendCtrlD()
    {
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_D, 0, 0, UIntPtr.Zero);
        keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private void CleanupCreatedSessionDirs()
    {
        // Remove name overrides first so even if dir deletion fails (file lock from
        // Roger's running booster), the booster UI won't keep the test label cached.
        foreach (var sessionId in this._createdSessionIds)
        {
            try
            {
                SessionNameOverrideService.Remove(Program.SessionNameOverrideFile, sessionId);
            }
            catch
            {
                // Best-effort — never fail Dispose.
            }
        }

        foreach (var sessionDir in this._createdSessionDirs)
        {
            TryDeleteSessionDir(sessionDir);
        }
    }

    private static void SweepOrphanItSessionDirs()
    {
        if (!Directory.Exists(Program.SessionStateDir))
        {
            return;
        }

        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateDirectories(Program.SessionStateDir);
        }
        catch
        {
            return;
        }

        foreach (var dir in candidates)
        {
            var sentinel = Path.Combine(dir, SessionCleanupSentinel);
            if (!File.Exists(sentinel))
            {
                continue;
            }

            // Try to recover the session id from the dir name (always a guid for our IT)
            // and clear its name override so a stale alias doesn't survive in booster's
            // session-names.json after we delete the dir.
            var sessionId = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(sessionId))
            {
                try { SessionNameOverrideService.Remove(Program.SessionNameOverrideFile, sessionId); } catch { }
            }

            TryDeleteSessionDir(dir);
        }
    }

    private static void TryDeleteSessionDir(string sessionDir)
    {
        if (!Directory.Exists(sessionDir))
        {
            return;
        }

        // Booster (when Roger has the real app running) periodically reads workspace.yaml
        // / events.jsonl. Those reads acquire transient handles that can race with our
        // delete. Retry with backoff to win against the booster's poll loop.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Directory.Delete(sessionDir, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch
            {
                Thread.Sleep(250);
            }
        }
    }

    private sealed record ProcessRunResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

    private sealed class TestDataGridView : DataGridView
    {
        internal void PerformCellMouseClick(int columnIndex, int rowIndex, int x, int y)
        {
            var mouseArgs = new MouseEventArgs(MouseButtons.Left, clicks: 1, x, y, delta: 0);
            this.OnCellMouseClick(new DataGridViewCellMouseEventArgs(columnIndex, rowIndex, x, y, mouseArgs));
        }
    }

    private sealed class RealCopilotSession
    {
        internal string Label { get; }
        internal string Marker { get; } = Guid.NewGuid().ToString("N");
        internal string SessionId { get; set; } = string.Empty;
        internal string SessionDir { get; set; } = string.Empty;
        internal int CopilotPid { get; set; }
        internal int PwshPid { get; set; }
        internal CopilotHostInfo? Host { get; set; }

        internal RealCopilotSession(string label) => this.Label = label;
    }
}
