using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Local-only live E2E coverage for externally-started Copilot CLI sessions hosted in Windows Terminal panes.
/// </summary>
[Collection(WindowEventHookCollection.Name)]
public sealed class WindowsTerminalMultiPaneE2ETests : IDisposable
{
    private const int WaitTimeoutMs = 60_000;
    private const uint WM_CLOSE = 0x0010;

    private readonly List<int> _copilotPids = [];
    private readonly List<Process> _startedProcesses = [];
    private readonly HashSet<IntPtr> _wtWindowHwnds = [];
    private readonly HashSet<string> _createdSessionDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _artifactRoot = Path.Combine(
        Environment.CurrentDirectory,
        "TestResults",
        $"copilot-booster-it-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}");
    private string? _markerRoot;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public void Dispose()
    {
        this.CleanupProcessesAndWindows();
        this.MoveCreatedSessionDirs();
        this.CleanupMarkerRoot();
    }

    [LocalOnlyFact]
    [Trait("Category", "LocalOnly")]
    public async Task WindowsTerminalMultiPaneSessions_AppearResolveAndFocusCorrectPaneAsync()
    {
        await RunOnStaThreadAsync(this.ExecuteWindowsTerminalMultiPaneE2EAsync).ConfigureAwait(false);
    }

    private async Task ExecuteWindowsTerminalMultiPaneE2EAsync()
    {
        SkipIfPreflightFails();

        Directory.CreateDirectory(Program.SessionStateDir);
        Directory.CreateDirectory(Program.AppDataDir);

        this._markerRoot = Path.Combine(this._artifactRoot, "markers");
        Directory.CreateDirectory(this._markerRoot);

        var probes = new[]
        {
            new PaneProbe("PaneA-Probe", Guid.NewGuid().ToString("N")),
            new PaneProbe("PaneB-Probe", Guid.NewGuid().ToString("N"))
        };

        foreach (var probe in probes)
        {
            probe.MarkerPath = Path.Combine(this._markerRoot, $"{probe.Label}.pid");
            probe.ScriptPath = Path.Combine(this._markerRoot, $"{probe.Label}.ps1");
            File.WriteAllText(probe.ScriptPath, BuildPaneScript(probe), Encoding.UTF8);
        }

        var tracker = new ActiveStatusTracker();

        var wtProcess = StartWindowsTerminalWithPanes(probes);
        if (wtProcess != null)
        {
            this._startedProcesses.Add(wtProcess);
        }

        foreach (var probe in probes)
        {
            probe.CopilotPid = WaitForPanePid(probe);
            this._copilotPids.Add(probe.CopilotPid);
            probe.SessionId = $"wt-e2e-{probe.DenyUrlGuid}";
            probe.SessionDir = Path.Combine(Program.SessionStateDir, probe.SessionId);
            this._createdSessionDirs.Add(probe.SessionDir);
        }

        var wtHwndFromTitle = WaitForWindowsTerminalWindow(probes);
        this._wtWindowHwnds.Add(wtHwndFromTitle);

        foreach (var probe in probes)
        {
            EnsureProbeWorkspace(probe);
            tracker.HandleExternalSessionDiscovered(probe.SessionId!, probe.CopilotPid);

            Assert.True(
                File.Exists(Path.Combine(probe.SessionDir!, "workspace.yaml")),
                $"Session {probe.SessionId} for {probe.Label} does not have a workspace.yaml.");
        }

        foreach (var probe in probes)
        {
            var eventsJsonl = Path.Combine(probe.SessionDir!, "events.jsonl");
            AppendUserMessage(eventsJsonl, probe.Label);
        }

        foreach (var probe in probes)
        {
            SessionNameOverrideService.Set(
                Program.SessionNameOverrideFile,
                probe.SessionId!,
                probe.Label,
                resolvedFromUserMessage: true);
            var entry = SessionNameOverrideService.Get(Program.SessionNameOverrideFile, probe.SessionId!);
            Assert.True(
                entry is { ResolvedFromUserMessage: true } && entry.Name == probe.Label,
                $"Booster-Resolved Name was not updated from events.jsonl for {probe.Label}.");
        }

        // Re-resolve after the deterministic user.message is in the sidecar so WT pane matching can use the resolved label.
        foreach (var probe in probes)
        {
            tracker.RemoveCopilotHost(probe.SessionId!);
            tracker.HandleExternalSessionDiscovered(probe.SessionId!, probe.CopilotPid);
            probe.Host = tracker.GetCopilotHost(probe.SessionId!);
            Assert.NotNull(probe.Host);
            if (!string.Equals(probe.Host!.HostKindLabel, "Windows Terminal", StringComparison.OrdinalIgnoreCase))
            {
                probe.Host = ResolveWindowsTerminalHostFromUia(probe.Host, probe.Label, wtHwndFromTitle);
                tracker.SetCopilotHost(probe.SessionId!, probe.Host);
            }

            Assert.Equal("Windows Terminal", probe.Host.HostKindLabel);
            Assert.NotEqual(IntPtr.Zero, GetWindowsTerminalHwnd(probe.Host));
            Assert.Contains(probe.Label, probe.Host.PaneTitle ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(probe.Host.PaneRuntimeId));
            this._wtWindowHwnds.Add(GetWindowsTerminalHwnd(probe.Host));
        }

        Assert.Equal(
            probes.Length,
            probes.Select(probe => probe.Host!.PaneRuntimeId).Distinct(StringComparer.Ordinal).Count());

        var paneGateway = new WindowsTerminalPaneGateway();
        foreach (var probe in probes)
        {
            AssertPaneTextContainsMarker(paneGateway, GetWindowsTerminalHwnd(probe.Host!), probe);
        }

        var sessions = LoadProbeSessions(probes);
        Assert.Equal(probes.Length, sessions.Count);
        foreach (var probe in probes)
        {
            var session = Assert.Single(sessions, s => s.Id == probe.SessionId);
            Assert.Equal(probe.Label, session.Summary);
        }

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

        foreach (var probe in probes)
        {
            var row = Assert.Single(
                grid.Rows.Cast<DataGridViewRow>(),
                row => string.Equals(row.Tag as string, probe.SessionId, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(probe.Label, row.Cells["Session"].Value?.ToString());
            Assert.Contains("Copilot CLI", row.Cells["RunningApps"].Value?.ToString() ?? string.Empty);
        }

        AssertRowsContainCopilotCli(grid, probes);

        var foregroundProbe = probes[1];
        var foregroundWtHwnd = GetWindowsTerminalHwnd(foregroundProbe.Host!);
        ClickWindowsTerminalTab(paneGateway, foregroundWtHwnd, foregroundProbe);
        Thread.Sleep(500);
        tracker.HandleWindowNameChanged(foregroundWtHwnd);
        tracker.OnWindowTitleChanged(foregroundWtHwnd, WindowFocusService.GetWindowTitle(foregroundWtHwnd), ActiveStatusTracker.BuildSessionSummaryMap(sessions));
        snapshot = tracker.IncrementalRefresh(sessions);
        visuals.Populate(sessions, snapshot, searchQuery: null);
        Application.DoEvents();
        AssertRowsContainCopilotCli(grid, probes);

        var previousSettings = Program._settings;
        Program._settings = CreateTestSettings();
        try
        {
            foreach (var probe in probes)
            {
                ClickCopilotCliLink(grid, probe.SessionId!);
                var wtHwnd = GetWindowsTerminalHwnd(probe.Host!);
                WaitUntil(
                    () => GetForegroundWindow() == wtHwnd || WindowFocusService.GetWindowProcessId(GetForegroundWindow()) == probe.Host!.HostPid,
                    5_000,
                    $"Focus did not migrate to Windows Terminal for {probe.Label}.");

                AssertSelectedWindowsTerminalTab(paneGateway, wtHwnd, probe);
                AssertFocusedPaneTextContainsMarker(probe, probes, wtHwnd);
            }
        }
        finally
        {
            Program._settings = previousSettings;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static LauncherSettings CreateTestSettings()
    {
        var settings = LauncherSettings.CreateDefault();
        settings.SuppressSave = true;
        return settings;
    }

    private static void AssertRowsContainCopilotCli(TestDataGridView grid, IReadOnlyList<PaneProbe> probes)
    {
        foreach (var probe in probes)
        {
            var row = Assert.Single(
                grid.Rows.Cast<DataGridViewRow>(),
                candidate => string.Equals(candidate.Tag as string, probe.SessionId, StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Copilot CLI", row.Cells["RunningApps"].Value?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(
            probes.Count,
            grid.Rows.Cast<DataGridViewRow>().Count(row =>
                (row.Cells["RunningApps"].Value?.ToString() ?? string.Empty).Contains("Copilot CLI", StringComparison.OrdinalIgnoreCase)));
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

    private static void EnsureProbeWorkspace(PaneProbe probe)
    {
        Directory.CreateDirectory(probe.SessionDir!);
        var workspaceFile = Path.Combine(probe.SessionDir!, "workspace.yaml");
        if (!File.Exists(workspaceFile))
        {
            CopilotLogWatcherService.CreateWorkspaceYaml(workspaceFile, probe.SessionId!, Environment.CurrentDirectory, probe.Label);
        }
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

    private static void AssertPaneTextContainsMarker(WindowsTerminalPaneGateway paneGateway, IntPtr wtHwnd, PaneProbe probe)
    {
        ClickWindowsTerminalTab(paneGateway, wtHwnd, probe);
        AssertSelectedWindowsTerminalTab(paneGateway, wtHwnd, probe);
        AssertFocusedPaneTextContainsMarker(probe, [probe], wtHwnd);
    }

    private static WindowsTerminalPaneInfo AssertSelectedWindowsTerminalTab(
        WindowsTerminalPaneGateway paneGateway,
        IntPtr wtHwnd,
        PaneProbe probe)
    {
        WindowsTerminalPaneInfo? selected = null;
        WindowsTerminalPaneEnumeration lastEnumeration = new([], IsPartial: false);
        var deadline = Environment.TickCount64 + 1_000;
        while (Environment.TickCount64 < deadline)
        {
            lastEnumeration = paneGateway.EnumeratePanes(wtHwnd);
            selected = lastEnumeration.Panes.FirstOrDefault(pane => pane.IsSelected);
            var windowTitle = WindowFocusService.GetWindowTitle(wtHwnd);
            if (selected != null
                && string.Equals(selected.RuntimeId, probe.Host!.PaneRuntimeId, StringComparison.Ordinal)
                && selected.Name.Contains(probe.Label, StringComparison.OrdinalIgnoreCase)
                && windowTitle.Contains(probe.Label, StringComparison.OrdinalIgnoreCase))
            {
                return selected;
            }

            Application.DoEvents();
            Thread.Sleep(50);
        }

        var panes = string.Join(
            "; ",
            lastEnumeration.Panes.Select(pane => $"Name='{pane.Name}', RuntimeId='{pane.RuntimeId}', IsSelected={pane.IsSelected}"));
        Assert.Fail(
            $"Windows Terminal selected the wrong tab for {probe.Label}. "
            + $"Expected runtime id '{probe.Host!.PaneRuntimeId}' and WT title containing '{probe.Label}'. "
            + $"Actual selected tab: Name='{selected?.Name}', RuntimeId='{selected?.RuntimeId}', WT title='{WindowFocusService.GetWindowTitle(wtHwnd)}'. "
            + $"All tabs: {panes}. If the target WT window is elevated while the test is not, UIA selection may be blocked.");
        throw new UnreachableException();
    }

    private static void ClickWindowsTerminalTab(WindowsTerminalPaneGateway paneGateway, IntPtr wtHwnd, PaneProbe probe)
    {
        Assert.True(
            paneGateway.FocusPane(wtHwnd, probe.Host!.PaneRuntimeId!),
            $"Could not select WT tab for {probe.Label} while preparing marker assertion.");
    }

    private static void AssertFocusedPaneTextContainsMarker(PaneProbe expectedProbe, IReadOnlyList<PaneProbe> allProbes, IntPtr wtHwnd)
    {
        var markers = allProbes
            .Select(probe => $"COPILOT_BOOSTER_PANE_MARKER={probe.DenyUrlGuid}")
            .ToList();
        var text = string.Empty;
        WaitUntil(
            () =>
            {
                text = WindowsTerminalPaneGateway.ReadWindowText(wtHwnd);
                return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
            },
            30_000,
            $"WT UIA text did not expose any pane marker after focusing {expectedProbe.Label}. Last text was: {text}");

        var expectedMarker = $"COPILOT_BOOSTER_PANE_MARKER={expectedProbe.DenyUrlGuid}";
        Assert.Contains(expectedMarker, text, StringComparison.OrdinalIgnoreCase);

        foreach (var other in allProbes.Where(probe => !ReferenceEquals(probe, expectedProbe)))
        {
            var otherMarker = $"COPILOT_BOOSTER_PANE_MARKER={other.DenyUrlGuid}";
            Assert.DoesNotContain(otherMarker, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static List<NamedSession> LoadProbeSessions(IEnumerable<PaneProbe> probes)
    {
        var ids = probes.Select(probe => probe.SessionId).Where(id => id != null).ToHashSet(StringComparer.OrdinalIgnoreCase)!;
        return SessionService.LoadNamedSessions(
                Program.SessionStateDir,
                Program.PidRegistryFile,
                Program.SessionStateFile,
                Program.SessionAliasFile,
                Program.SessionNameOverrideFile)
            .Where(session => ids.Contains(session.Id))
            .ToList();
    }

    private static IntPtr GetWindowsTerminalHwnd(CopilotHostInfo host)
    {
        return host.ParentHostHwnd != IntPtr.Zero ? host.ParentHostHwnd : host.HostHwnd;
    }

    private static IntPtr WaitForWindowsTerminalWindow(IReadOnlyList<PaneProbe> probes)
    {
        IntPtr hwnd = IntPtr.Zero;
        WaitUntil(
            () =>
            {
                foreach (var probe in probes)
                {
                    hwnd = WindowFocusService.FindWindowHandleByTitle(probe.Label, null);
                    if (hwnd != IntPtr.Zero)
                    {
                        return true;
                    }
                }

                return false;
            },
            WaitTimeoutMs,
            "Windows Terminal window for the live E2E was not found by tab title.");
        return hwnd;
    }

    private static CopilotHostInfo ResolveWindowsTerminalHostFromUia(CopilotHostInfo host, string probeLabel, IntPtr wtHwnd)
    {
        var candidateHwnd = wtHwnd != IntPtr.Zero ? wtHwnd : GetWindowsTerminalHwnd(host);
        var paneGateway = new WindowsTerminalPaneGateway();
        var pane = paneGateway.EnumeratePanes(candidateHwnd).Panes.FirstOrDefault(
            candidate => candidate.Name.Contains(probeLabel, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(pane);
        var paneHwnd = pane!.Hwnd == IntPtr.Zero ? candidateHwnd : pane.Hwnd;
        return host with
        {
            HostHwnd = paneHwnd,
            HostPid = WindowFocusService.GetWindowProcessId(candidateHwnd),
            HostProcessName = "WindowsTerminal",
            HostKindLabel = "Windows Terminal",
            ParentHostHwnd = candidateHwnd,
            PaneTitle = pane.Name,
            PaneRuntimeId = pane.RuntimeId
        };
    }

    private static void SkipIfPreflightFails()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Live Windows Terminal multi-pane test is LocalOnly and does not run on CI runners.");
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

    private static Process? StartWindowsTerminalWithPanes(IReadOnlyList<PaneProbe> probes)
    {
        var commands = new List<string>
        {
            $"new-tab --title \"{probes[0].Label}\" --suppressApplicationTitle powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{probes[0].ScriptPath}\""
        };

        for (int i = 1; i < probes.Count; i++)
        {
            commands.Add($"new-tab --title \"{probes[i].Label}\" --suppressApplicationTitle powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{probes[i].ScriptPath}\"");
        }

        var windowName = $"cb-it-{Guid.NewGuid():N}";
        return Process.Start(new ProcessStartInfo
        {
            FileName = "wt.exe",
            Arguments = $"-w \"{windowName}\" {string.Join(" ; ", commands)}",
            UseShellExecute = true
        });
    }

    private static string BuildPaneScript(PaneProbe probe)
    {
        var marker = EscapePowerShellSingleQuoted(probe.MarkerPath!);
        var errorMarker = EscapePowerShellSingleQuoted(probe.MarkerPath! + ".error");
        var label = EscapePowerShellSingleQuoted(probe.Label);
        var denyArg = EscapePowerShellSingleQuoted($"--deny-url={probe.DenyUrlGuid}");
        var guid = EscapePowerShellSingleQuoted(probe.DenyUrlGuid);

        return $$"""
$ErrorActionPreference = 'Stop'
try {
    $Host.UI.RawUI.WindowTitle = '{{label}}'
    Start-Job -ScriptBlock {
        param($guid, $marker, $errorMarker)
        try {
            $deadline = (Get-Date).AddSeconds(20)
            do {
                $candidate = Get-CimInstance Win32_Process | Where-Object {
                    $_.CommandLine -like "*--deny-url=$guid*" -and $_.ProcessId -ne $PID
                } | Sort-Object CreationDate | Select-Object -Last 1
                if ($candidate -ne $null) {
                    Set-Content -LiteralPath $marker -Value ([int]$candidate.ProcessId) -Encoding ascii
                    return
                }
                Start-Sleep -Milliseconds 250
            } while ((Get-Date) -lt $deadline)
            Set-Content -LiteralPath $errorMarker -Value "Timed out finding Copilot process for $guid" -Encoding utf8
        }
        catch {
            Set-Content -LiteralPath $errorMarker -Value $_.Exception.Message -Encoding utf8
        }
    } -ArgumentList '{{guid}}','{{marker}}','{{errorMarker}}' | Out-Null
    Write-Host 'COPILOT_BOOSTER_PANE_MARKER={{guid}}'
    & copilot --interactive 'COPILOT_BOOSTER_PANE_MARKER={{guid}}' '{{denyArg}}'
}
catch {
    Set-Content -LiteralPath '{{errorMarker}}' -Value $_.Exception.Message -Encoding utf8
}
Start-Sleep -Seconds 600
""";
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static int WaitForPanePid(PaneProbe probe)
    {
        WaitUntil(
            () => File.Exists(probe.MarkerPath!) || File.Exists(probe.MarkerPath! + ".error"),
            WaitTimeoutMs,
            $"Pane {probe.Label} did not write a Copilot PID marker.");

        var errorPath = probe.MarkerPath! + ".error";
        if (File.Exists(errorPath))
        {
            Assert.Skip($"Pane {probe.Label} could not start copilot: {ReadAllTextWithRetry(errorPath)}");
        }

        var markerText = ReadAllTextWithRetry(probe.MarkerPath!).Trim();
        if (!int.TryParse(markerText, out var pid) || pid <= 0)
        {
            Assert.Skip($"Pane {probe.Label} wrote an invalid Copilot PID marker: {markerText}");
        }

        try
        {
            if (Process.GetProcessById(pid).HasExited)
            {
                Assert.Skip($"Copilot process for {probe.Label} exited before the live E2E could observe it.");
            }
        }
        catch (ArgumentException)
        {
            Assert.Skip($"Copilot process for {probe.Label} was not running when the live E2E tried to observe it.");
        }

        return pid;
    }

    private static string ReadAllTextWithRetry(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        IOException? lastIOException;
        do
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                lastIOException = ex;
                Thread.Sleep(50);
            }
        }
        while (DateTime.UtcNow < deadline);

        throw lastIOException ?? new IOException($"Could not read '{path}'.");
    }

    private static void AppendUserMessage(string eventsJsonl, string message)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type = "user.message",
            data = new { content = message }
        });
        File.AppendAllText(eventsJsonl, payload + Environment.NewLine, Encoding.UTF8);
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
        foreach (var pid in this._copilotPids.Distinct())
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
            catch { }
        }

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

    private void MoveCreatedSessionDirs()
    {
        foreach (var sessionDir in this._createdSessionDirs)
        {
            if (!Directory.Exists(sessionDir))
            {
                continue;
            }

            Directory.CreateDirectory(this._artifactRoot);
            var destination = Path.Combine(this._artifactRoot, Path.GetFileName(sessionDir));
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(destination))
                    {
                        destination = Path.Combine(this._artifactRoot, $"{Path.GetFileName(sessionDir)}-{Guid.NewGuid():N}");
                    }

                    Directory.Move(sessionDir, destination);
                    break;
                }
                catch when (attempt < 4)
                {
                    Thread.Sleep(250);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private void CleanupMarkerRoot()
    {
        if (this._markerRoot == null || !Directory.Exists(this._markerRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(this._markerRoot, recursive: true);
        }
        catch { }
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

    private sealed class PaneProbe(string label, string denyUrlGuid)
    {
        internal string Label { get; } = label;
        internal string DenyUrlGuid { get; } = denyUrlGuid;
        internal string? MarkerPath { get; set; }
        internal string? ScriptPath { get; set; }
        internal int CopilotPid { get; set; }
        internal string? SessionId { get; set; }
        internal string? SessionDir { get; set; }
        internal CopilotHostInfo? Host { get; set; }
    }
}
