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

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int SW_RESTORE = 9;
    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

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

        // Window MUST be foreground BEFORE UIA Select. Selecting a tab in a non-foreground
        // wt window only marks the tab visually; when the window later comes forward, it
        // restores whichever tab WAS active, not the freshly-selected one.
        ForceForeground(wtHwnd);
        Thread.Sleep(200);

        // CRITICAL: only call UIA Select when the desired tab is NOT already selected.
        // SelectionItemPattern.Select on an already-selected TabItem moves keyboard focus
        // FROM the TermControl ONTO the tab strip element — keystrokes then go nowhere
        // (the tab title isn't editable). Freshly-spawned single-tab wt windows always
        // have tab 0 already selected; selecting it again would steal focus from the
        // pane content. Same applies for a 2-tab wt where tab 1 just opened via
        // Ctrl+Shift+T and is already the active tab.
        if (!target.IsSelected)
        {
            Assert.True(gateway.FocusPane(wtHwnd, target.RuntimeId!), $"Could not focus tab {tabIndex} via UIA before typing.");
            Thread.Sleep(250);
            ForceForeground(wtHwnd);
            Thread.Sleep(250);
        }

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

        ForceForeground(wtHwnd);
        Thread.Sleep(200);
        if (!target.IsSelected)
        {
            Assert.True(gateway.FocusPane(wtHwnd, target.RuntimeId!), $"Could not focus tab {tabIndex} via UIA before /rename.");
            Thread.Sleep(250);
            ForceForeground(wtHwnd);
            Thread.Sleep(250);
        }

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

    [Fact]
    public async Task MultiWtWindows_HostBindingDoesNotScrambleAsync()
    {
        await RunOnStaThreadAsync(this.ExecuteMultiWtWindowsHostBindingAsync).ConfigureAwait(false);
    }

    private async Task ExecuteMultiWtWindowsHostBindingAsync()
    {
        SkipIfPreflightFails();
        Directory.CreateDirectory(Program.SessionStateDir);
        Directory.CreateDirectory(Program.AppDataDir);
        SweepOrphanItSessionDirs();

        // Three sessions across two wt windows:
        //   wt-A hosts sessions A1, A2 (two copilot tabs).
        //   wt-B hosts session B1 (single copilot tab in a SECOND wt window).
        //
        // Since the Sun Valley refactor, all wt windows share one WindowsTerminal.exe PID.
        // CopilotHostResolver historically called GetTopLevelWindow(wtPid) which returns
        // the FIRST visible top-level hwnd matching the pid (z-order), so two of the
        // three sessions get bound to the wrong wt window — their actual pane lives in
        // the OTHER wt window, but resolution returned this one's hwnd.
        var sessionA1 = new RealCopilotSession("WtA-Tab1-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var sessionA2 = new RealCopilotSession("WtA-Tab2-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var sessionB1 = new RealCopilotSession("WtB-Tab1-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var allSessions = new[] { sessionA1, sessionA2, sessionB1 };
        foreach (var s in allSessions)
        {
            s.SessionId = Guid.NewGuid().ToString();
            s.SessionDir = Path.Combine(Program.SessionStateDir, s.SessionId);
            this._createdSessionDirs.Add(s.SessionDir);
            this._createdSessionIds.Add(s.SessionId);
            Directory.CreateDirectory(s.SessionDir);
            File.WriteAllText(Path.Combine(s.SessionDir, SessionCleanupSentinel), DateTime.UtcNow.ToString("O"));
            WriteMinimalWorkspaceYaml(Path.Combine(s.SessionDir, "workspace.yaml"), s.SessionId, Environment.CurrentDirectory);
            WriteSessionStartEvent(Path.Combine(s.SessionDir, "events.jsonl"), s.SessionId, Environment.CurrentDirectory);
            SessionNameOverrideService.Set(Program.SessionNameOverrideFile, s.SessionId, s.Label, resolvedFromUserMessage: true);
        }

        var paneGateway = new WindowsTerminalPaneGateway();

        // Open BOTH wt windows up-front (each with their empty default tabs) BEFORE
        // typing any copilot commands. Spawning a second wt window while the first
        // window's copilots are mid-launch yanks foreground to the new window and
        // SendKeys.SendWait delivery races against the focus change — typed bytes
        // get split across the wrong cells.
        //
        // wt-A: spawn with `-w new`, open a SECOND tab via Ctrl+Shift+T, no copilot yet.
        var wtAHwnd = OpenFreshWtWithExtraTab(paneGateway, out var wtAProcess);
        if (wtAProcess != null)
        {
            this._startedProcesses.Add(wtAProcess);
        }
        this._wtWindowHwnds.Add(wtAHwnd);

        // wt-B: spawn with `-w new`, single empty tab.
        var wtBHwnd = OpenFreshWtWithSingleTab(paneGateway, out var wtBProcess);
        if (wtBProcess != null)
        {
            this._startedProcesses.Add(wtBProcess);
        }
        this._wtWindowHwnds.Add(wtBHwnd);
        Assert.NotEqual(wtAHwnd, wtBHwnd);

        // Both windows are now stable; type the copilot launch command into each tab
        // SERIALLY. TypeIntoTab refocuses both the UIA pane and the wt window before
        // each SendKeys block, so we don't depend on global focus state between calls.
        TypeIntoTab(paneGateway, wtAHwnd, tabIndex: 0, $"copilot --resume \"{sessionA1.SessionId}\" --deny-url={sessionA1.Marker}");
        TypeIntoTab(paneGateway, wtAHwnd, tabIndex: 1, $"copilot --resume \"{sessionA2.SessionId}\" --deny-url={sessionA2.Marker}");
        TypeIntoTab(paneGateway, wtBHwnd, tabIndex: 0, $"copilot --resume \"{sessionB1.SessionId}\" --deny-url={sessionB1.Marker}");

        (sessionA1.CopilotPid, sessionA1.PwshPid) = WaitForCopilotPidByDenyUrl(sessionA1.Marker, 30_000);
        (sessionA2.CopilotPid, sessionA2.PwshPid) = WaitForCopilotPidByDenyUrl(sessionA2.Marker, 30_000);
        (sessionB1.CopilotPid, sessionB1.PwshPid) = WaitForCopilotPidByDenyUrl(sessionB1.Marker, 30_000);
        foreach (var s in allSessions)
        {
            this._copilotPids.Add(s.CopilotPid);
            this._pwshPids.Add(s.PwshPid);
        }

        // Wait for copilot to fully load on all 3 panes (across both wt windows).
        WaitUntil(
            () =>
            {
                var aPanes = paneGateway.EnumeratePanes(wtAHwnd).Panes;
                var bPanes = paneGateway.EnumeratePanes(wtBHwnd).Panes;
                return aPanes.Count >= 2
                    && aPanes.Take(2).All(p => p.Name.Contains("GitHub Copilot", StringComparison.OrdinalIgnoreCase))
                    && bPanes.Count >= 1
                    && bPanes[0].Name.Contains("GitHub Copilot", StringComparison.OrdinalIgnoreCase);
            },
            45_000,
            "Copilot did not reach 'GitHub Copilot' title on all 3 panes within 45s.");

        Thread.Sleep(2_000);

        RenameTab(paneGateway, wtAHwnd, tabIndex: 0, sessionA1.Label);
        RenameTab(paneGateway, wtAHwnd, tabIndex: 1, sessionA2.Label);
        RenameTab(paneGateway, wtBHwnd, tabIndex: 0, sessionB1.Label);

        // Boot the booster tracker AFTER both wt windows are fully up.
        var tracker = new ActiveStatusTracker();
        foreach (var s in allSessions)
        {
            tracker.HandleExternalSessionDiscovered(s.SessionId, s.CopilotPid);
            s.Host = tracker.GetCopilotHost(s.SessionId);
            Assert.NotNull(s.Host);
        }

        // Ground truth: enumerate panes in BOTH wt windows and use the unique session
        // labels (set by RenameTab earlier and verified by its WaitUntil) to determine
        // which wt hwnd physically owns each session's pane. We can't use
        // PaneRootProcessId here — UIA isn't always exposing pane-content hwnds with
        // their own pid (depends on focus state, OpenConsole/ConPTY hosting), so the
        // labels are the only stable per-pane identifier we have.
        var wtAPanes = paneGateway.EnumeratePanes(wtAHwnd).Panes;
        var wtBPanes = paneGateway.EnumeratePanes(wtBHwnd).Panes;
        IntPtr GroundTruthWtHwndFor(RealCopilotSession s)
        {
            bool MatchesPane(WindowsTerminalPaneInfo pane) =>
                (pane.PaneRootProcessId.HasValue && pane.PaneRootProcessId.Value == s.PwshPid)
                || pane.ProcessId == s.CopilotPid
                || pane.Name.Contains(s.Label, StringComparison.OrdinalIgnoreCase);

            if (wtAPanes.Any(MatchesPane))
            {
                return wtAHwnd;
            }
            if (wtBPanes.Any(MatchesPane))
            {
                return wtBHwnd;
            }
            Assert.Fail(
                $"Could not locate a pane for session {s.Label} (copilotPid={s.CopilotPid}, pwshPid={s.PwshPid}) "
                + $"in either wt window. wt-A panes: [{string.Join("|", wtAPanes.Select(p => $"name='{p.Name}',pid={p.ProcessId},paneRoot={p.PaneRootProcessId}"))}] "
                + $"wt-B panes: [{string.Join("|", wtBPanes.Select(p => $"name='{p.Name}',pid={p.ProcessId},paneRoot={p.PaneRootProcessId}"))}]");
            return IntPtr.Zero;
        }

        var groundTruthA1 = GroundTruthWtHwndFor(sessionA1);
        var groundTruthA2 = GroundTruthWtHwndFor(sessionA2);
        var groundTruthB1 = GroundTruthWtHwndFor(sessionB1);
        Assert.Equal(wtAHwnd, groundTruthA1);
        Assert.Equal(wtAHwnd, groundTruthA2);
        Assert.Equal(wtBHwnd, groundTruthB1);

        // Each session's resolved host MUST point at the wt window that physically owns
        // its pane. With the bug present, the resolver picks whichever wt hwnd EnumWindows
        // hits first for the shared WindowsTerminal.exe pid → two of three sessions are
        // bound to the wrong window.
        var diagDetails = string.Join(
            " | ",
            allSessions.Select(s => $"{s.Label}: resolved={s.Host!.ParentHostHwnd}, expected={GroundTruthWtHwndFor(s)}"));
        Assert.Equal(groundTruthA1, sessionA1.Host!.ParentHostHwnd);
        Assert.Equal(groundTruthA2, sessionA2.Host!.ParentHostHwnd);
        Assert.Equal(groundTruthB1, sessionB1.Host!.ParentHostHwnd);

        // Sanity: pane runtime IDs must be distinct across sessions.
        Assert.NotEqual(sessionA1.Host!.PaneRuntimeId, sessionA2.Host!.PaneRuntimeId);
        Assert.NotEqual(sessionA2.Host!.PaneRuntimeId, sessionB1.Host!.PaneRuntimeId);
        Assert.NotEqual(sessionA1.Host!.PaneRuntimeId, sessionB1.Host!.PaneRuntimeId);

        // Click test: render the grid and click each session's link, asserting the
        // correct wt window's correct tab is selected. Uses the per-session ground truth
        // hwnd so we verify focus actually crosses between wt windows when needed.
        var sessions = SessionService.LoadNamedSessions(
                Program.SessionStateDir,
                Program.PidRegistryFile,
                Program.SessionStateFile,
                Program.SessionAliasFile,
                Program.SessionNameOverrideFile)
            .Where(s => allSessions.Any(a => a.SessionId == s.Id))
            .ToList();
        Assert.Equal(3, sessions.Count);

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
            AssertClickFocusesCorrectTab(grid, paneGateway, groundTruthA1, sessionA1);
            AssertClickFocusesCorrectTab(grid, paneGateway, groundTruthA2, sessionA2);
            AssertClickFocusesCorrectTab(grid, paneGateway, groundTruthB1, sessionB1);
            // Click back across the wt-A → wt-B boundary to ensure focus crosses windows
            // (the bug presents most clearly when one wt is foreground and the click
            // should bring the OTHER wt to the front and select its tab).
            AssertClickFocusesCorrectTab(grid, paneGateway, groundTruthA1, sessionA1);
        }
        finally
        {
            Program._settings = prevSettings;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    [Fact]
    public async Task MultiWtWindows_TitleMatchRebindsHostAfterFallbackAsync()
    {
        await RunOnStaThreadAsync(this.ExecuteMultiWtWindowsTitleMatchRebindAsync).ConfigureAwait(false);
    }

    /// <summary>
    /// Reproduces Roger's 2026-05-04 finding (image: _copilotHosts watch with two
    /// sessions sharing host hwnd 0x90768 while the visible "Run Tests" wt is hwnd
    /// 9441788). When real users name their wt tabs with arbitrary labels (no session
    /// GUID, no sessionSummary substring), CopilotHostResolver's pane-term match fails
    /// across all candidate wt hwnds → ResolveWindowsTerminalAcrossCandidates falls
    /// back to the FIRST candidate hwnd (essentially Z-order at discovery time). For
    /// 2-of-3 sessions in this scenario that's the wrong wt window.
    ///
    /// Booster's <see cref="ActiveStatusTracker.OnWindowTitleChanged"/> hook DOES
    /// observe the right wt hwnd later: when copilot CLI sets the wt tab title to
    /// "Copilot CLI - {sessionId}" or the session summary, the hook's title-match
    /// correctly attributes that hwnd to the session. But pre-fix, the match is only
    /// stored in <c>_activeTrackedWindows</c> — <c>_copilotHosts[sessionId].ParentHostHwnd</c>
    /// stays stale at the resolver's wrong fallback. Click-to-focus then targets the
    /// wrong wt window.
    ///
    /// Test shape (mirrors <see cref="MultiWtWindows_HostBindingDoesNotScrambleAsync"/>
    /// but DELIBERATELY blocks the resolver's pane match so we land in fallback):
    ///   - 2 wt windows (wt-A: 2 tabs, wt-B: 1 tab); 3 copilot sessions.
    ///   - Rename labels are unique (used for ground truth) but NOT registered as
    ///     SessionNameOverride and NOT used as workspace summary, so they aren't in
    ///     BuildWindowsTerminalPaneMatchTerms's term list. Resolver finds no pane match
    ///     and falls back to first-by-Z-order.
    ///   - After Discover, simulate the title-change hook firing with
    ///     "Copilot CLI - {sessionId}" pointing at each session's REAL wt hwnd
    ///     (this is what production sees once copilot has started up).
    ///   - Assert _copilotHosts[sessionId].ParentHostHwnd now equals the ground-truth
    ///     wt hwnd (post-fix). Pre-fix this assertion fails for whichever sessions
    ///     the resolver mis-bound.
    /// </summary>
    private async Task ExecuteMultiWtWindowsTitleMatchRebindAsync()
    {
        SkipIfPreflightFails();
        Directory.CreateDirectory(Program.SessionStateDir);
        Directory.CreateDirectory(Program.AppDataDir);
        SweepOrphanItSessionDirs();

        var sessionA1 = new RealCopilotSession("RebindA1-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var sessionA2 = new RealCopilotSession("RebindA2-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var sessionB1 = new RealCopilotSession("RebindB1-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var allSessions = new[] { sessionA1, sessionA2, sessionB1 };

        // Note: deliberately NOT calling SessionNameOverrideService.Set with the rename
        // label. We only want the label to drive UIA-pane-name-based ground truth lookup,
        // NOT to feed BuildWindowsTerminalPaneMatchTerms's override-name path. This is
        // what reproduces the production miss — real users don't name their tabs with
        // strings that match the session GUID or the sessionSummary booster knows about.
        foreach (var s in allSessions)
        {
            s.SessionId = Guid.NewGuid().ToString();
            s.SessionDir = Path.Combine(Program.SessionStateDir, s.SessionId);
            this._createdSessionDirs.Add(s.SessionDir);
            this._createdSessionIds.Add(s.SessionId);
            Directory.CreateDirectory(s.SessionDir);
            File.WriteAllText(Path.Combine(s.SessionDir, SessionCleanupSentinel), DateTime.UtcNow.ToString("O"));
            WriteMinimalWorkspaceYaml(Path.Combine(s.SessionDir, "workspace.yaml"), s.SessionId, Environment.CurrentDirectory);
            WriteSessionStartEvent(Path.Combine(s.SessionDir, "events.jsonl"), s.SessionId, Environment.CurrentDirectory);
        }

        var paneGateway = new WindowsTerminalPaneGateway();

        var wtAHwnd = OpenFreshWtWithExtraTab(paneGateway, out var wtAProcess);
        if (wtAProcess != null)
        {
            this._startedProcesses.Add(wtAProcess);
        }
        this._wtWindowHwnds.Add(wtAHwnd);

        var wtBHwnd = OpenFreshWtWithSingleTab(paneGateway, out var wtBProcess);
        if (wtBProcess != null)
        {
            this._startedProcesses.Add(wtBProcess);
        }
        this._wtWindowHwnds.Add(wtBHwnd);
        Assert.NotEqual(wtAHwnd, wtBHwnd);

        TypeIntoTab(paneGateway, wtAHwnd, tabIndex: 0, $"copilot --resume \"{sessionA1.SessionId}\" --deny-url={sessionA1.Marker}");
        TypeIntoTab(paneGateway, wtAHwnd, tabIndex: 1, $"copilot --resume \"{sessionA2.SessionId}\" --deny-url={sessionA2.Marker}");
        TypeIntoTab(paneGateway, wtBHwnd, tabIndex: 0, $"copilot --resume \"{sessionB1.SessionId}\" --deny-url={sessionB1.Marker}");

        (sessionA1.CopilotPid, sessionA1.PwshPid) = WaitForCopilotPidByDenyUrl(sessionA1.Marker, 30_000);
        (sessionA2.CopilotPid, sessionA2.PwshPid) = WaitForCopilotPidByDenyUrl(sessionA2.Marker, 30_000);
        (sessionB1.CopilotPid, sessionB1.PwshPid) = WaitForCopilotPidByDenyUrl(sessionB1.Marker, 30_000);
        foreach (var s in allSessions)
        {
            this._copilotPids.Add(s.CopilotPid);
            this._pwshPids.Add(s.PwshPid);
        }

        WaitUntil(
            () =>
            {
                var aPanes = paneGateway.EnumeratePanes(wtAHwnd).Panes;
                var bPanes = paneGateway.EnumeratePanes(wtBHwnd).Panes;
                return aPanes.Count >= 2
                    && aPanes.Take(2).All(p => p.Name.Contains("GitHub Copilot", StringComparison.OrdinalIgnoreCase))
                    && bPanes.Count >= 1
                    && bPanes[0].Name.Contains("GitHub Copilot", StringComparison.OrdinalIgnoreCase);
            },
            45_000,
            "Copilot did not reach 'GitHub Copilot' title on all 3 panes within 45s.");

        Thread.Sleep(2_000);

        // Apply rename labels for GROUND TRUTH only (used to determine which wt window
        // physically owns each session's pane). NOT registered as SessionNameOverride.
        RenameTab(paneGateway, wtAHwnd, tabIndex: 0, sessionA1.Label);
        RenameTab(paneGateway, wtAHwnd, tabIndex: 1, sessionA2.Label);
        RenameTab(paneGateway, wtBHwnd, tabIndex: 0, sessionB1.Label);

        var tracker = new ActiveStatusTracker();
        foreach (var s in allSessions)
        {
            tracker.HandleExternalSessionDiscovered(s.SessionId, s.CopilotPid);
            s.Host = tracker.GetCopilotHost(s.SessionId);
            Assert.NotNull(s.Host);
        }

        var wtAPanes = paneGateway.EnumeratePanes(wtAHwnd).Panes;
        var wtBPanes = paneGateway.EnumeratePanes(wtBHwnd).Panes;
        IntPtr GroundTruthWtHwndFor(RealCopilotSession s)
        {
            bool MatchesPane(WindowsTerminalPaneInfo pane) =>
                (pane.PaneRootProcessId.HasValue && pane.PaneRootProcessId.Value == s.PwshPid)
                || pane.ProcessId == s.CopilotPid
                || pane.Name.Contains(s.Label, StringComparison.OrdinalIgnoreCase);

            if (wtAPanes.Any(MatchesPane))
            {
                return wtAHwnd;
            }
            if (wtBPanes.Any(MatchesPane))
            {
                return wtBHwnd;
            }
            Assert.Fail(
                $"Could not locate a pane for session {s.Label} (copilotPid={s.CopilotPid}, pwshPid={s.PwshPid}) "
                + $"in either wt window. wt-A panes: [{string.Join("|", wtAPanes.Select(p => $"name='{p.Name}',pid={p.ProcessId},paneRoot={p.PaneRootProcessId}"))}] "
                + $"wt-B panes: [{string.Join("|", wtBPanes.Select(p => $"name='{p.Name}',pid={p.ProcessId},paneRoot={p.PaneRootProcessId}"))}]");
            return IntPtr.Zero;
        }

        var groundTruthA1 = GroundTruthWtHwndFor(sessionA1);
        var groundTruthA2 = GroundTruthWtHwndFor(sessionA2);
        var groundTruthB1 = GroundTruthWtHwndFor(sessionB1);
        Assert.Equal(wtAHwnd, groundTruthA1);
        Assert.Equal(wtAHwnd, groundTruthA2);
        Assert.Equal(wtBHwnd, groundTruthB1);

        // Now simulate the title-change hook firing as production sees it: copilot CLI
        // sets the wt tab title to "Copilot CLI - {sessionId}" once the session is up.
        // The booster's hook calls OnWindowTitleChanged with the wt window's hwnd and
        // that title — and BuildSessionSummaryMap is null/empty (the wt title is the
        // strong sessionId-prefixed form, not a session-summary lookup).
        tracker.OnWindowTitleChanged(groundTruthA1, $"Copilot CLI - {sessionA1.SessionId}", sessionSummaries: null);
        tracker.OnWindowTitleChanged(groundTruthA2, $"Copilot CLI - {sessionA2.SessionId}", sessionSummaries: null);
        tracker.OnWindowTitleChanged(groundTruthB1, $"Copilot CLI - {sessionB1.SessionId}", sessionSummaries: null);

        // After title-match identifies the right wt hwnd for each session, the host
        // tracking MUST reflect that — clicking the session's link in the booster grid
        // navigates to ParentHostHwnd, so any staleness here visibly focuses the wrong
        // wt window. This is the assertion that fails pre-fix for whichever sessions
        // the resolver mis-bound.
        var hostA1 = tracker.GetCopilotHost(sessionA1.SessionId);
        var hostA2 = tracker.GetCopilotHost(sessionA2.SessionId);
        var hostB1 = tracker.GetCopilotHost(sessionB1.SessionId);
        Assert.NotNull(hostA1);
        Assert.NotNull(hostA2);
        Assert.NotNull(hostB1);

        var diagDetails = string.Join(
            " | ",
            allSessions.Select(s => $"{s.Label}: hostHwnd={tracker.GetCopilotHost(s.SessionId)?.ParentHostHwnd}, expected={GroundTruthWtHwndFor(s)}"));
        Assert.True(groundTruthA1 == hostA1!.ParentHostHwnd, $"sessionA1 host should rebind to {groundTruthA1} after title-change. Details: {diagDetails}");
        Assert.True(groundTruthA2 == hostA2!.ParentHostHwnd, $"sessionA2 host should rebind to {groundTruthA2} after title-change. Details: {diagDetails}");
        Assert.True(groundTruthB1 == hostB1!.ParentHostHwnd, $"sessionB1 host should rebind to {groundTruthB1} after title-change. Details: {diagDetails}");

        await Task.CompletedTask.ConfigureAwait(false);
    }

    [Fact]
    public async Task MultiWtWindows_FullRefreshTitleScanRebindsCopilotHostAsync()
    {
        await RunOnStaThreadAsync(this.ExecuteMultiWtWindowsFullRefreshRebindAsync).ConfigureAwait(false);
    }

    /// <summary>
    /// Reproduces Roger's 2026-05-04 second-order finding: even after the
    /// <see cref="ActiveStatusTracker.OnWindowTitleChanged"/> rebind shipped (commit
    /// after <c>db79c81</c>), wt windows whose tabs were renamed BEFORE the booster
    /// (re)started never trigger an <c>EVENT_OBJECT_NAMECHANGE</c> for those tabs —
    /// the title hasn't CHANGED since the hook subscribed. The startup
    /// <see cref="ActiveStatusTracker.FullRefresh"/> call DOES find the right wt hwnd
    /// via title-scan (matching against <c>BuildSessionSummaryMap</c>) and stores it
    /// in <c>_activeTrackedWindows[sessionId]</c>, but pre-fix nothing propagates
    /// that ground truth back into <c>_copilotHosts[sessionId]</c>. Click-to-focus
    /// reads <c>_copilotHosts</c> and targets the resolver's wrong fallback hwnd.
    ///
    /// Test shape:
    ///   - 2 wt windows (wt-A: 2 tabs, wt-B: 1 tab); 3 copilot sessions.
    ///   - Each session has Summary set in its workspace.yaml to the label that
    ///     matches its tab title (so <c>BuildSessionSummaryMap</c> includes it).
    ///   - Resolver's pane-term match still misses (rename label not in pane term
    ///     list under any code path) → tracker falls back to first-by-Z-order.
    ///   - DO NOT fire <c>OnWindowTitleChanged</c> (the renames pre-date the tracker).
    ///   - Call <c>tracker.FullRefresh(sessions)</c>.
    ///   - Assert <c>_copilotHosts[sessionId].ParentHostHwnd</c> equals the
    ///     ground-truth wt hwnd. Pre-fix fails for misbound sessions; post-fix passes.
    /// </summary>
    private async Task ExecuteMultiWtWindowsFullRefreshRebindAsync()
    {
        SkipIfPreflightFails();
        Directory.CreateDirectory(Program.SessionStateDir);
        Directory.CreateDirectory(Program.AppDataDir);
        SweepOrphanItSessionDirs();

        var sessionA1 = new RealCopilotSession("FullRefreshA1-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var sessionA2 = new RealCopilotSession("FullRefreshA2-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var sessionB1 = new RealCopilotSession("FullRefreshB1-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var allSessions = new[] { sessionA1, sessionA2, sessionB1 };

        foreach (var s in allSessions)
        {
            s.SessionId = Guid.NewGuid().ToString();
            s.SessionDir = Path.Combine(Program.SessionStateDir, s.SessionId);
            this._createdSessionDirs.Add(s.SessionDir);
            this._createdSessionIds.Add(s.SessionId);
            Directory.CreateDirectory(s.SessionDir);
            File.WriteAllText(Path.Combine(s.SessionDir, SessionCleanupSentinel), DateTime.UtcNow.ToString("O"));
            // Use the same minimal workspace.yaml the other multi-wt tests use (extra
            // fields like `name:`/`summary:` cause `copilot --resume` to reject loading
            // the session). The session SUMMARY (which feeds BuildSessionSummaryMap and
            // is what FullRefresh's title-scan matches against) is provided via
            // SessionNameOverrideService — SessionService.LoadNamedSessions resolves
            // displaySummary as override.Name when no workspace summary is present.
            WriteMinimalWorkspaceYaml(Path.Combine(s.SessionDir, "workspace.yaml"), s.SessionId, Environment.CurrentDirectory);
            WriteSessionStartEvent(Path.Combine(s.SessionDir, "events.jsonl"), s.SessionId, Environment.CurrentDirectory);
            SessionNameOverrideService.Set(Program.SessionNameOverrideFile, s.SessionId, s.Label, resolvedFromUserMessage: true);
        }

        var paneGateway = new WindowsTerminalPaneGateway();

        var wtAHwnd = OpenFreshWtWithExtraTab(paneGateway, out var wtAProcess);
        if (wtAProcess != null)
        {
            this._startedProcesses.Add(wtAProcess);
        }
        this._wtWindowHwnds.Add(wtAHwnd);

        var wtBHwnd = OpenFreshWtWithSingleTab(paneGateway, out var wtBProcess);
        if (wtBProcess != null)
        {
            this._startedProcesses.Add(wtBProcess);
        }
        this._wtWindowHwnds.Add(wtBHwnd);
        Assert.NotEqual(wtAHwnd, wtBHwnd);

        TypeIntoTab(paneGateway, wtAHwnd, tabIndex: 0, $"copilot --resume \"{sessionA1.SessionId}\" --deny-url={sessionA1.Marker}");
        TypeIntoTab(paneGateway, wtAHwnd, tabIndex: 1, $"copilot --resume \"{sessionA2.SessionId}\" --deny-url={sessionA2.Marker}");
        TypeIntoTab(paneGateway, wtBHwnd, tabIndex: 0, $"copilot --resume \"{sessionB1.SessionId}\" --deny-url={sessionB1.Marker}");

        (sessionA1.CopilotPid, sessionA1.PwshPid) = WaitForCopilotPidByDenyUrl(sessionA1.Marker, 30_000);
        (sessionA2.CopilotPid, sessionA2.PwshPid) = WaitForCopilotPidByDenyUrl(sessionA2.Marker, 30_000);
        (sessionB1.CopilotPid, sessionB1.PwshPid) = WaitForCopilotPidByDenyUrl(sessionB1.Marker, 30_000);
        foreach (var s in allSessions)
        {
            this._copilotPids.Add(s.CopilotPid);
            this._pwshPids.Add(s.PwshPid);
        }

        WaitUntil(
            () =>
            {
                var aPanes = paneGateway.EnumeratePanes(wtAHwnd).Panes;
                var bPanes = paneGateway.EnumeratePanes(wtBHwnd).Panes;
                return aPanes.Count >= 2
                    && aPanes.Take(2).All(p => p.Name.Contains("GitHub Copilot", StringComparison.OrdinalIgnoreCase))
                    && bPanes.Count >= 1
                    && bPanes[0].Name.Contains("GitHub Copilot", StringComparison.OrdinalIgnoreCase);
            },
            45_000,
            "Copilot did not reach 'GitHub Copilot' title on all 3 panes within 45s.");

        Thread.Sleep(2_000);

        // Apply rename labels — these double as ground-truth pane labels AND match the
        // session summary written into workspace.yaml above. FullRefresh's title-scan
        // will use BuildSessionSummaryMap to attribute these tab titles to sessions.
        RenameTab(paneGateway, wtAHwnd, tabIndex: 0, sessionA1.Label);
        RenameTab(paneGateway, wtAHwnd, tabIndex: 1, sessionA2.Label);
        RenameTab(paneGateway, wtBHwnd, tabIndex: 0, sessionB1.Label);

        var tracker = new ActiveStatusTracker();
        foreach (var s in allSessions)
        {
            tracker.HandleExternalSessionDiscovered(s.SessionId, s.CopilotPid);
            s.Host = tracker.GetCopilotHost(s.SessionId);
            Assert.NotNull(s.Host);
        }

        var wtAPanes = paneGateway.EnumeratePanes(wtAHwnd).Panes;
        var wtBPanes = paneGateway.EnumeratePanes(wtBHwnd).Panes;
        IntPtr GroundTruthWtHwndFor(RealCopilotSession s)
        {
            bool MatchesPane(WindowsTerminalPaneInfo pane) =>
                (pane.PaneRootProcessId.HasValue && pane.PaneRootProcessId.Value == s.PwshPid)
                || pane.ProcessId == s.CopilotPid
                || pane.Name.Contains(s.Label, StringComparison.OrdinalIgnoreCase);

            if (wtAPanes.Any(MatchesPane))
            {
                return wtAHwnd;
            }
            if (wtBPanes.Any(MatchesPane))
            {
                return wtBHwnd;
            }
            Assert.Fail(
                $"Could not locate a pane for session {s.Label} (copilotPid={s.CopilotPid}, pwshPid={s.PwshPid}) "
                + $"in either wt window. wt-A panes: [{string.Join("|", wtAPanes.Select(p => $"name='{p.Name}',pid={p.ProcessId},paneRoot={p.PaneRootProcessId}"))}] "
                + $"wt-B panes: [{string.Join("|", wtBPanes.Select(p => $"name='{p.Name}',pid={p.ProcessId},paneRoot={p.PaneRootProcessId}"))}]");
            return IntPtr.Zero;
        }

        var groundTruthA1 = GroundTruthWtHwndFor(sessionA1);
        var groundTruthA2 = GroundTruthWtHwndFor(sessionA2);
        var groundTruthB1 = GroundTruthWtHwndFor(sessionB1);
        Assert.Equal(wtAHwnd, groundTruthA1);
        Assert.Equal(wtAHwnd, groundTruthA2);
        Assert.Equal(wtBHwnd, groundTruthB1);

        // Critical: do NOT fire OnWindowTitleChanged here. The renames happened BEFORE
        // this tracker existed (in production, before booster (re)started). The hook
        // would only fire on subsequent CHANGES — which never come for an idle wt window.
        // FullRefresh is the only path that reaches the right hwnd.
        var loadedSessions = SessionService.LoadNamedSessions(
            Program.SessionStateDir,
            Program.PidRegistryFile,
            aliasFile: Program.SessionAliasFile,
            overrideFile: Program.SessionNameOverrideFile);

        // Sanity: ensure our 3 sessions made it into the loaded list with the right summary.
        var loadedTestSessions = loadedSessions.Where(s => allSessions.Any(t => string.Equals(t.SessionId, s.Id, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.Equal(allSessions.Length, loadedTestSessions.Count);
        foreach (var s in allSessions)
        {
            var loaded = loadedTestSessions.Single(l => string.Equals(l.Id, s.SessionId, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(s.Label, loaded.Summary);
        }

        tracker.FullRefresh(loadedSessions);

        var hostA1 = tracker.GetCopilotHost(sessionA1.SessionId);
        var hostA2 = tracker.GetCopilotHost(sessionA2.SessionId);
        var hostB1 = tracker.GetCopilotHost(sessionB1.SessionId);
        Assert.NotNull(hostA1);
        Assert.NotNull(hostA2);
        Assert.NotNull(hostB1);

        var diagDetails = string.Join(
            " | ",
            allSessions.Select(s => $"{s.Label}: hostHwnd={tracker.GetCopilotHost(s.SessionId)?.ParentHostHwnd}, expected={GroundTruthWtHwndFor(s)}"));
        Assert.True(groundTruthA1 == hostA1!.ParentHostHwnd, $"sessionA1 host should rebind to {groundTruthA1} after FullRefresh. Details: {diagDetails}");
        Assert.True(groundTruthA2 == hostA2!.ParentHostHwnd, $"sessionA2 host should rebind to {groundTruthA2} after FullRefresh. Details: {diagDetails}");
        Assert.True(groundTruthB1 == hostB1!.ParentHostHwnd, $"sessionB1 host should rebind to {groundTruthB1} after FullRefresh. Details: {diagDetails}");

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Spawns a fresh wt.exe window via <c>-w new</c>, waits for its first tab's UIA pane
    /// to register, then opens a SECOND empty tab via Ctrl+Shift+T. Does NOT type any
    /// commands. Returns the new wt window's hwnd. Use when the test needs to stand up
    /// MULTIPLE wt windows before any typing — opening more wt windows AFTER typing
    /// races SendKeys delivery against the foreground change the new window triggers.
    /// </summary>
    private static IntPtr OpenFreshWtWithExtraTab(WindowsTerminalPaneGateway gateway, out Process? launcher)
    {
        var existingWtHwnds = EnumerateWindowsTerminalHwnds().ToHashSet();

        launcher = Process.Start(new ProcessStartInfo
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

        WaitUntil(
            () => gateway.EnumeratePanes(wtHwnd).Panes.Count >= 1,
            15_000,
            "wt.exe first tab's UIA pane did not appear within 15s.");
        Thread.Sleep(1_500);

        ForceForeground(wtHwnd);
        Thread.Sleep(250);
        SendKeys.SendWait("^+t");

        WaitUntil(
            () => gateway.EnumeratePanes(wtHwnd).Panes.Count >= 2,
            15_000,
            "wt.exe second tab did not appear within 15s after Ctrl+Shift+T.");
        Thread.Sleep(1_500);

        return wtHwnd;
    }

    /// <summary>
    /// Spawns a fresh wt.exe window via <c>-w new</c>, waits for its single default tab's
    /// UIA pane to register. Does NOT type any commands. Returns the new wt hwnd.
    /// </summary>
    private static IntPtr OpenFreshWtWithSingleTab(WindowsTerminalPaneGateway gateway, out Process? launcher)
    {
        var existingWtHwnds = EnumerateWindowsTerminalHwnds().ToHashSet();

        launcher = Process.Start(new ProcessStartInfo
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
            "Second wt.exe window did not appear within 20s.");

        WaitUntil(
            () => gateway.EnumeratePanes(wtHwnd).Panes.Count >= 1,
            15_000,
            "Second wt.exe window's first tab did not register a UIA pane within 15s.");
        Thread.Sleep(1_500);

        return wtHwnd;
    }

    /// <summary>
    /// Bullet-proof foreground switch for IT use. <see cref="WindowFocusService.TryFocusWindowHandle"/>
    /// returns true unconditionally, but the underlying SetForegroundWindow silently fails
    /// when the calling process doesn't own the foreground (e.g. just after another wt
    /// window spawned). The canonical fix: AttachThreadInput so the OS treats us as
    /// "owning" the source thread, then SetForegroundWindow is allowed. After detaching
    /// we verify GetForegroundWindow() == hwnd and retry up to N times if not.
    /// </summary>
    private static void ForceForeground(IntPtr hwnd, int retries = 8)
    {
        for (int attempt = 0; attempt < retries; attempt++)
        {
            uint thisThread = GetCurrentThreadId();
            uint targetThread = GetWindowThreadProcessId(hwnd, out _);
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);

            bool attachedFg = false;
            bool attachedTarget = false;
            try
            {
                if (fgThread != 0 && thisThread != fgThread)
                {
                    attachedFg = AttachThreadInput(thisThread, fgThread, true);
                }
                if (targetThread != 0 && thisThread != targetThread && targetThread != fgThread)
                {
                    attachedTarget = AttachThreadInput(thisThread, targetThread, true);
                }

                ShowWindow(hwnd, SW_RESTORE);
                BringWindowToTop(hwnd);

                // Alt-key trick still helps when AttachThreadInput is partially blocked.
                keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);

                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attachedTarget)
                {
                    AttachThreadInput(thisThread, targetThread, false);
                }
                if (attachedFg)
                {
                    AttachThreadInput(thisThread, fgThread, false);
                }
            }

            Thread.Sleep(150);
            if (GetForegroundWindow() == hwnd)
            {
                return;
            }
        }

        Assert.Fail(
            $"ForceForeground failed after {retries} attempts. Target hwnd=0x{hwnd.ToInt64():X}, "
            + $"foreground hwnd=0x{GetForegroundWindow().ToInt64():X} ('{WindowFocusService.GetWindowTitle(GetForegroundWindow())}'). "
            + "SetForegroundWindow was likely blocked by Windows foreground lock — the test runner process "
            + "doesn't own the foreground, AttachThreadInput workaround did not take effect.");
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
