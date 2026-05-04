using System.Diagnostics;

namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Read-only diagnostic against the user's current desktop. Does NOT spawn / kill
/// any process and does NOT mutate booster state. Prints a snapshot of:
///   - every WindowsTerminal hwnd alive on the desktop
///   - every pane (tab) UIA reports for that hwnd, with name + runtime + pane-root pid
///   - every copilot.exe process and what <see cref="CopilotHostResolver"/> would
///     bind it to (HostHwnd / ParentHostHwnd / candidate hwnds)
/// Use this when click-to-focus targets the wrong wt window in production —
/// run it with COPILOT_BOOSTER_RUN_LOCALONLY=1 to capture the live ground truth
/// and compare it against the booster's diag log.
/// </summary>
public sealed class LiveCopilotHostDiagnosticTests
{
    [LocalOnlyStaFact]
    public void DumpLiveCopilotHostBindings()
    {
        var report = new List<string>
        {
            $"=== Live Copilot Host Diagnostic @ {DateTime.Now:O} ==="
        };

        var wtPids = Process.GetProcessesByName("WindowsTerminal")
            .Concat(Process.GetProcessesByName("wt"))
            .Select(p =>
            {
                int pid = p.Id;
                p.Dispose();
                return pid;
            })
            .Distinct()
            .ToList();

        report.Add($"WindowsTerminal pids ({wtPids.Count}): [{string.Join(",", wtPids)}]");

        var gateway = new WindowsTerminalPaneGateway();
        var hwndToPanes = new Dictionary<IntPtr, IReadOnlyList<WindowsTerminalPaneInfo>>();
        var hwndToOwnerPid = new Dictionary<IntPtr, int>();

        foreach (var pid in wtPids)
        {
            var hwnds = WindowFocusService.EnumerateWindowHandlesByPid(pid);
            report.Add($"  pid={pid} hwnds={hwnds.Count} -> [{string.Join(",", hwnds.Select(h => h.ToInt64()))}]");
            foreach (var hwnd in hwnds)
            {
                if (hwndToPanes.ContainsKey(hwnd))
                {
                    continue;
                }

                var enumeration = gateway.EnumeratePanes(hwnd);
                hwndToPanes[hwnd] = enumeration.Panes;
                hwndToOwnerPid[hwnd] = pid;

                report.Add($"    hwnd={hwnd.ToInt64()} (pid={pid}) panes={enumeration.Panes.Count} partial={enumeration.IsPartial}");
                foreach (var pane in enumeration.Panes)
                {
                    report.Add(
                        $"      pane name='{pane.Name}' runtime='{pane.RuntimeId}' paneRootPid={pane.PaneRootProcessId?.ToString() ?? "<null>"} selected={pane.IsSelected} hwnd={pane.Hwnd.ToInt64()} processId={pane.ProcessId}");
                }
            }
        }

        // Highlight any wt hwnd whose tabs include "Run Test" (the bug-repro window the user has open).
        var runTestHwnds = hwndToPanes
            .Where(kvp => kvp.Value.Any(p => p.Name?.IndexOf("Run Test", StringComparison.OrdinalIgnoreCase) >= 0))
            .Select(kvp => kvp.Key)
            .ToList();

        report.Add($"Hwnds containing a 'Run Test*' tab: [{string.Join(",", runTestHwnds.Select(h => h.ToInt64()))}]");

        // Now resolve every alive copilot.exe and report what host the production resolver picks.
        var copilotProcs = Process.GetProcessesByName("copilot")
            .Concat(Process.GetProcessesByName("copilot.exe"))
            .Select(p => p.Id)
            .Distinct()
            .ToList();

        report.Add($"copilot pids ({copilotProcs.Count}): [{string.Join(",", copilotProcs)}]");

        var resolver = new CopilotHostResolver();
        foreach (var copilotPid in copilotProcs)
        {
            try
            {
                var info = resolver.Resolve(copilotPid);
                if (info == null)
                {
                    report.Add($"  copilotPid={copilotPid} resolver returned null");
                    continue;
                }

                report.Add(
                    $"  copilotPid={copilotPid} hostKind='{info.HostKindLabel}' hostName='{info.HostProcessName}' hostPid={info.HostPid} parentHostHwnd={info.ParentHostHwnd.ToInt64()} hostHwnd={info.HostHwnd.ToInt64()} paneRootPid={info.PaneRootProcessId?.ToString() ?? "<null>"}");

                var wtCtx = resolver.ResolveWindowsTerminalContext(copilotPid);
                if (wtCtx == null)
                {
                    report.Add($"    ResolveWindowsTerminalContext returned null (host is not WT or chain dead)");
                    continue;
                }

                report.Add(
                    $"    wtContext hostPid={wtCtx.HostPid} paneRootPid={wtCtx.PaneRootPid} firstHwnd={wtCtx.HostHwnd.ToInt64()} candidates=[{string.Join(",", wtCtx.CandidateHostHwnds.Select(h => h.ToInt64()))}]");

                // For each candidate, cross-reference the panes we already enumerated.
                foreach (var cand in wtCtx.CandidateHostHwnds)
                {
                    if (hwndToPanes.TryGetValue(cand, out var panes))
                    {
                        report.Add(
                            $"    candidate {cand.ToInt64()} (ownerPid={(hwndToOwnerPid.TryGetValue(cand, out var ownerPid) ? ownerPid.ToString() : "?")}): paneNames=[{string.Join("|", panes.Select(p => p.Name))}]");
                    }
                    else
                    {
                        report.Add($"    candidate {cand.ToInt64()}: <no pane info captured>");
                    }
                }
            }
            catch (Exception ex)
            {
                report.Add($"  copilotPid={copilotPid} resolver threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // === Cross-check live sessions vs panes ===
        // Load NamedSessions exactly the way the booster's UI does and rebuild the
        // session-summary map FindTrackedWindows uses for title-match. Then for every
        // wt hwnd we enumerated above, see whether any of its tab names map to a known
        // sessionId via this dict — that's the GROUND TRUTH "session X belongs in
        // wt hwnd Y" that the booster could in principle act on but currently does
        // not propagate into _copilotHosts.
        report.Add("=== Live session ↔ tab title cross-check ===");
        var namedSessions = SessionService.LoadNamedSessions(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "session-state"),
            pidRegistryFile: null,
            sessionStateFile: null,
            aliasFile: null,
            overrideFile: null);
        report.Add($"NamedSessions loaded: {namedSessions.Count}");
        var sessionSummaryMap = ActiveStatusTracker.BuildSessionSummaryMap(namedSessions);
        report.Add($"sessionSummaryMap entries: {sessionSummaryMap.Count}");
        foreach (var kvp in sessionSummaryMap)
        {
            report.Add($"  '{kvp.Key}' -> {kvp.Value}");
        }

        // For each wt hwnd, see which of its tabs match a tracked session title.
        var sessionToExpectedHwnd = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hwnd, panes) in hwndToPanes)
        {
            foreach (var pane in panes)
            {
                if (string.IsNullOrEmpty(pane.Name))
                {
                    continue;
                }

                var match = WindowFocusService.MatchTrackedWindowTitle(pane.Name, sessionSummaryMap);
                if (match == null)
                {
                    continue;
                }

                report.Add(
                    $"  GROUND-TRUTH session={match.Value.SessionId} expectedHwnd={hwnd.ToInt64()} via tab='{pane.Name}' label={match.Value.Label}");
                sessionToExpectedHwnd[match.Value.SessionId] = hwnd;
            }
        }

        // Now compare against what ResolveCopilotHost would pick TODAY for sessions
        // we have a live copilot pid for. Mismatch == bug present.
        report.Add("=== Resolver mismatch report ===");
        var mismatches = new List<(string SessionId, IntPtr Expected, IntPtr ResolverPick)>();
        foreach (var session in namedSessions)
        {
            if (!sessionToExpectedHwnd.TryGetValue(session.Id, out var expectedHwnd))
            {
                continue;
            }

            // Find a live copilot pid for this session via the pid registry.
            int? copilotPidForSession = TryFindCopilotPidForSession(session.Id);
            if (copilotPidForSession == null)
            {
                report.Add($"  session={session.Id} expected={expectedHwnd.ToInt64()} (no live copilotPid in registry)");
                continue;
            }

            var ctx = resolver.ResolveWindowsTerminalContext(copilotPidForSession.Value);
            if (ctx == null)
            {
                report.Add($"  session={session.Id} expected={expectedHwnd.ToInt64()} (resolver returned null context for pid {copilotPidForSession})");
                continue;
            }

            var resolverPick = ctx.HostHwnd; // first-by-Z-order fallback the booster uses today
            var status = resolverPick == expectedHwnd ? "✓ MATCH" : "✗ MISMATCH (BUG)";
            report.Add(
                $"  session={session.Id} copilotPid={copilotPidForSession} expected={expectedHwnd.ToInt64()} resolverPicks={resolverPick.ToInt64()} {status} candidates=[{string.Join(",", ctx.CandidateHostHwnds.Select(h => h.ToInt64()))}]");
            if (resolverPick != expectedHwnd)
            {
                mismatches.Add((session.Id, expectedHwnd, resolverPick));
            }
        }
        report.Add($"=== {mismatches.Count} mismatch(es) detected ===");

        // Also dump tail of diag log for context on what the live booster has been logging.
        var diagPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotBooster",
            "logs",
            "diag.log");
        if (File.Exists(diagPath))
        {
            report.Add($"--- diag.log tail ({diagPath}) ---");
            try
            {
                using var fs = new FileStream(diagPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                var allLines = new List<string>();
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    allLines.Add(line);
                }

                int take = Math.Min(120, allLines.Count);
                foreach (var l in allLines.Skip(allLines.Count - take))
                {
                    report.Add(l);
                }
            }
            catch (Exception ex)
            {
                report.Add($"  diag.log read failed: {ex.Message}");
            }
        }
        else
        {
            report.Add($"diag.log not found at {diagPath}");
        }

        var snapshotPath = Path.Combine(
            Path.GetTempPath(),
            $"copilot-host-live-snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.WriteAllLines(snapshotPath, report);

        // Push to test output so it shows up regardless of where xUnit captures.
        foreach (var line in report)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine($"[snapshot saved to {snapshotPath}]");

        // Sanity assertions so the test surfaces the answer cleanly:
        Assert.NotEmpty(wtPids);
        Assert.True(hwndToPanes.Count > 0, "expected at least one wt hwnd with panes");
    }

    private static int? TryFindCopilotPidForSession(string sessionId)
    {
        // Map sessionId -> copilotPid by scanning the per-process log files in
        // %USERPROFILE%\.copilot\logs. Filenames are "process-<unix-ms>-<pid>.log";
        // each log embeds the sessionId in its telemetry JSON. We pick the most
        // recent live pid that matches the requested sessionId.
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot",
            "logs");
        if (!Directory.Exists(logsDir))
        {
            return null;
        }

        try
        {
            var candidates = Directory.GetFiles(logsDir, "process-*.log")
                .Select(f => new
                {
                    Path = f,
                    Pid = TryParsePidFromFileName(Path.GetFileName(f)),
                    LastWrite = File.GetLastWriteTimeUtc(f)
                })
                .Where(x => x.Pid.HasValue)
                .OrderByDescending(x => x.LastWrite)
                .Take(60); // most recent log files

            foreach (var c in candidates)
            {
                if (!IsProcessAlive(c.Pid!.Value))
                {
                    continue;
                }

                if (LogFileMentionsSession(c.Path, sessionId))
                {
                    return c.Pid;
                }
            }
        }
        catch
        {
            // best-effort
        }

        return null;
    }

    private static int? TryParsePidFromFileName(string fileName)
    {
        // process-<unix-ms>-<pid>.log
        var parts = fileName.Split('-');
        if (parts.Length < 3)
        {
            return null;
        }

        var pidPart = parts[^1].Replace(".log", string.Empty, StringComparison.OrdinalIgnoreCase);
        return int.TryParse(pidPart, out var pid) ? pid : null;
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool LogFileMentionsSession(string path, string sessionId)
    {
        try
        {
            // Don't read the whole file (logs can be huge); read up to first 2 MB
            // which is enough to capture the early telemetry block where sessionId lives.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[Math.Min(fs.Length, 2 * 1024 * 1024)];
            _ = fs.Read(buffer, 0, buffer.Length);
            var text = System.Text.Encoding.UTF8.GetString(buffer);
            return text.Contains(sessionId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
