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
        Path.GetTempPath(),
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

        var startedAtUtc = DateTime.UtcNow;
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

        var discoveredByWatcher = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var logWatcher = new CopilotLogWatcherService();
        var tracker = new ActiveStatusTracker();
        tracker.EventsJournal.StartWatching();
        logWatcher.ExternalSessionDiscovered += (sessionId, copilotPid) =>
        {
            discoveredByWatcher[sessionId] = copilotPid;
            tracker.HandleExternalSessionDiscovered(sessionId, copilotPid);
        };
        logWatcher.StartWatching();

        var wtProcess = StartWindowsTerminalWithPanes(probes);
        if (wtProcess != null)
        {
            this._startedProcesses.Add(wtProcess);
        }

        foreach (var probe in probes)
        {
            probe.CopilotPid = WaitForPanePid(probe);
            this._copilotPids.Add(probe.CopilotPid);
            probe.SessionId = WaitForSessionIdFromCopilotLog(probe.CopilotPid, startedAtUtc);
            probe.SessionDir = Path.Combine(Program.SessionStateDir, probe.SessionId);
            this._createdSessionDirs.Add(probe.SessionDir);
        }

        foreach (var probe in probes)
        {
            WaitUntil(
                () => discoveredByWatcher.ContainsKey(probe.SessionId!)
                    && File.Exists(Path.Combine(probe.SessionDir!, "workspace.yaml")),
                WaitTimeoutMs,
                $"CopilotBooster log watcher did not discover session {probe.SessionId} for {probe.Label}.");
        }

        foreach (var probe in probes)
        {
            var eventsJsonl = Path.Combine(probe.SessionDir!, "events.jsonl");
            AppendUserMessage(eventsJsonl, probe.Label);
        }

        foreach (var probe in probes)
        {
            WaitUntil(
                () =>
                {
                    var entry = SessionNameOverrideService.Get(Program.SessionNameOverrideFile, probe.SessionId!);
                    return entry is { ResolvedFromUserMessage: true } && entry.Name == probe.Label;
                },
                WaitTimeoutMs,
                $"Booster-Resolved Name was not updated from events.jsonl for {probe.Label}.");
        }

        // Re-resolve after the deterministic user.message is in the sidecar so WT pane matching can use the resolved label.
        foreach (var probe in probes)
        {
            tracker.RemoveCopilotHost(probe.SessionId!);
            tracker.HandleExternalSessionDiscovered(probe.SessionId!, probe.CopilotPid);
            probe.Host = tracker.GetCopilotHost(probe.SessionId!);
            Assert.NotNull(probe.Host);
            Assert.Equal("Windows Terminal", probe.Host!.HostKindLabel);
            Assert.NotEqual(IntPtr.Zero, GetWindowsTerminalHwnd(probe.Host));
            Assert.Contains(probe.Label, probe.Host.PaneTitle ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            this._wtWindowHwnds.Add(GetWindowsTerminalHwnd(probe.Host));
        }

        var sessions = LoadProbeSessions(probes);
        Assert.Equal(probes.Length, sessions.Count);
        foreach (var probe in probes)
        {
            var session = Assert.Single(sessions, s => s.Id == probe.SessionId);
            Assert.Equal(probe.Label, session.Summary);
        }

        var snapshot = tracker.IncrementalRefresh(sessions);
        var grid = CreateGrid();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());
        visuals.Populate(sessions, snapshot, searchQuery: null);

        foreach (var probe in probes)
        {
            var row = Assert.Single(
                grid.Rows.Cast<DataGridViewRow>(),
                row => string.Equals(row.Tag as string, probe.SessionId, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(probe.Label, row.Cells["Session"].Value?.ToString());
            Assert.Contains("Copilot CLI", row.Cells["RunningApps"].Value?.ToString() ?? string.Empty);
        }

        var paneGateway = new WindowsTerminalPaneGateway();
        foreach (var probe in probes)
        {
            tracker.FocusActiveProcess(probe.SessionId!, clickedLineIndex: 0);
            var wtHwnd = GetWindowsTerminalHwnd(probe.Host!);
            WaitUntil(
                () => GetForegroundWindow() == wtHwnd || WindowFocusService.GetWindowProcessId(GetForegroundWindow()) == probe.Host!.HostPid,
                5_000,
                $"Focus did not migrate to Windows Terminal for {probe.Label}.");

            WaitUntil(
                () =>
                {
                    var selected = paneGateway.EnumeratePanes(wtHwnd).Panes.FirstOrDefault(pane => pane.IsSelected);
                    return selected != null && selected.Name.Contains(probe.Label, StringComparison.OrdinalIgnoreCase);
                },
                5_000,
                $"Windows Terminal did not select the pane for {probe.Label}.");
        }

        await Task.CompletedTask.ConfigureAwait(false);
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
        grid.Columns.Add("Status", string.Empty);
        grid.Columns.Add("Session", "Session");
        grid.Columns.Add("CWD", "CWD");
        grid.Columns.Add("Date", "Date");
        grid.Columns.Add("Context", "Context");
        grid.Columns.Add("RunningApps", "RunningApps");
        grid.Columns.Add("GitHub", "GitHub");
        return grid;
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
            commands.Add($"split-pane --title \"{probes[i].Label}\" --suppressApplicationTitle powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{probes[i].ScriptPath}\"");
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
    $started = Start-Process -FilePath 'copilot' -ArgumentList @('{{denyArg}}') -PassThru
    $deadline = (Get-Date).AddSeconds(20)
    $targetPid = $null
    do {
        $candidate = Get-CimInstance Win32_Process | Where-Object {
            $_.CommandLine -like '*--deny-url={{guid}}*' -and $_.ProcessId -ne $PID
        } | Sort-Object CreationDate | Select-Object -Last 1
        if ($candidate -ne $null) {
            $targetPid = [int]$candidate.ProcessId
            break
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if ($targetPid -eq $null) { $targetPid = [int]$started.Id }
    Set-Content -LiteralPath '{{marker}}' -Value $targetPid -Encoding ascii
    Wait-Process -Id $targetPid -ErrorAction SilentlyContinue
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
            Assert.Skip($"Pane {probe.Label} could not start copilot: {File.ReadAllText(errorPath)}");
        }

        var markerText = File.ReadAllText(probe.MarkerPath!).Trim();
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

    private static string WaitForSessionIdFromCopilotLog(int copilotPid, DateTime startedAtUtc)
    {
        var logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "logs");
        string? foundSessionId = null;

        WaitUntil(
            () =>
            {
                if (!Directory.Exists(logsDir))
                {
                    return false;
                }

                var candidates = Directory.GetFiles(logsDir, "process-*.log")
                    .Where(path => CopilotLogWatcherService.ExtractPidFromFilename(Path.GetFileName(path)) == copilotPid)
                    .Where(path => File.GetLastWriteTimeUtc(path) >= startedAtUtc.AddMinutes(-1))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();

                foreach (var candidate in candidates)
                {
                    var lines = ReadAllLinesShared(candidate);
                    var (sessionId, _) = CopilotLogWatcherService.TryParseLogContent(lines);
                    if (!string.IsNullOrWhiteSpace(sessionId))
                    {
                        foundSessionId = sessionId;
                        return true;
                    }
                }

                return false;
            },
            WaitTimeoutMs,
            $"No Copilot session_start log was found for PID {copilotPid}.");

        return foundSessionId!;
    }

    private static string[] ReadAllLinesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        return reader.ReadToEnd().Split('\n');
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
