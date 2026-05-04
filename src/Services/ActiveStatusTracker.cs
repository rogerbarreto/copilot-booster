using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using CopilotBooster.Models;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Result snapshot returned by <see cref="ActiveStatusTracker.FullRefresh"/> or <see cref="ActiveStatusTracker.IncrementalRefresh"/>.
/// </summary>
internal record ActiveStatusSnapshot(
    Dictionary<string, string> ActiveTextBySessionId,
    Dictionary<string, string> SessionNamesById,
    Dictionary<string, string> StatusIconBySessionId
);

/// <summary>
/// Tracks active processes, terminals, and Edge workspaces for sessions.
/// </summary>
[ExcludeFromCodeCoverage]
internal class ActiveStatusTracker
{
    private HashSet<string> _activeSessionIds = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>> _activeTrackedWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ActiveProcess>> _trackedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EdgeWorkspaceService> _edgeWorkspaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TeamsWindowService> _teamsWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string Label, IntPtr Hwnd)>> _explorerWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CopilotHostInfo> _copilotHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly CopilotHostResolver _hostResolver;
    private readonly IWindowsTerminalPaneGateway _windowsTerminalPaneGateway;
    private readonly WindowsTerminalPaneCacheService _windowsTerminalPaneCache;
    private readonly Func<IntPtr, bool> _focusWindowHandle;
    private readonly Func<IntPtr, bool> _isWindowAlive;
    private readonly Func<int, bool> _isProcessAlive;
    private readonly HashSet<string> _startedSessionIds = new(StringComparer.OrdinalIgnoreCase);
    internal readonly EventsJournalService EventsJournal = new();
    private bool _handleCacheInitialLoadDone;

    private static readonly HashSet<string> s_ignoredSummaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "GitHub Copilot"
    };

    /// <summary>
    /// Callback invoked (possibly from a background thread) when an Edge workspace is closed.
    /// </summary>
    internal Action<string>? OnEdgeWorkspaceClosed { get; set; }

    /// <summary>
    /// Callback invoked when a tracked Teams window is detected as closed.
    /// </summary>
    internal Action<string>? OnTeamsWindowClosed { get; set; }

    /// <summary>
    /// Fired when a Copilot Host is resolved and set for a session.
    /// </summary>
    internal event Action<string, CopilotHostInfo>? CopilotHostResolved;

    /// <summary>
    /// Fired when a Copilot Host entry is removed for a session.
    /// </summary>
    internal event Action<string>? CopilotHostRemoved;

    internal ActiveStatusTracker()
        : this(new CopilotHostResolver(), new WindowsTerminalPaneGateway(), new WindowsTerminalPaneCacheService())
    {
    }

    internal ActiveStatusTracker(
        CopilotHostResolver hostResolver,
        IWindowsTerminalPaneGateway windowsTerminalPaneGateway,
        WindowsTerminalPaneCacheService windowsTerminalPaneCache)
        : this(hostResolver, windowsTerminalPaneGateway, windowsTerminalPaneCache, WindowFocusService.TryFocusWindowHandle, WindowFocusService.IsWindowAlive)
    {
    }

    internal ActiveStatusTracker(
        CopilotHostResolver hostResolver,
        IWindowsTerminalPaneGateway windowsTerminalPaneGateway,
        WindowsTerminalPaneCacheService windowsTerminalPaneCache,
        Func<IntPtr, bool> focusWindowHandle)
        : this(hostResolver, windowsTerminalPaneGateway, windowsTerminalPaneCache, focusWindowHandle, WindowFocusService.IsWindowAlive)
    {
    }

    internal ActiveStatusTracker(
        CopilotHostResolver hostResolver,
        IWindowsTerminalPaneGateway windowsTerminalPaneGateway,
        WindowsTerminalPaneCacheService windowsTerminalPaneCache,
        Func<IntPtr, bool> focusWindowHandle,
        Func<IntPtr, bool> isWindowAlive)
        : this(hostResolver, windowsTerminalPaneGateway, windowsTerminalPaneCache, focusWindowHandle, isWindowAlive, IsProcessAlive)
    {
    }

    internal ActiveStatusTracker(
        CopilotHostResolver hostResolver,
        IWindowsTerminalPaneGateway windowsTerminalPaneGateway,
        WindowsTerminalPaneCacheService windowsTerminalPaneCache,
        Func<IntPtr, bool> focusWindowHandle,
        Func<IntPtr, bool> isWindowAlive,
        Func<int, bool> isProcessAlive)
    {
        this._hostResolver = hostResolver;
        this._windowsTerminalPaneGateway = windowsTerminalPaneGateway;
        this._windowsTerminalPaneCache = windowsTerminalPaneCache;
        this._focusWindowHandle = focusWindowHandle;
        this._isWindowAlive = isWindowAlive;
        this._isProcessAlive = isProcessAlive;
    }

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            return !Process.GetProcessById(pid).HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Seeds sessions present at startup. These will output "" instead of "bell"
    /// until they transition to working first, preventing false bell notifications on app launch.
    /// </summary>
    internal void InitStartedSessions(IEnumerable<string> copilotCliSessionIds)
    {
        this._startedSessionIds.UnionWith(copilotCliSessionIds);
    }

    /// <summary>
    /// Marks a session as having transitioned to working (clears startup suppression).
    /// </summary>
    internal void MarkSessionWorking(string sessionId)
    {
        this._startedSessionIds.Remove(sessionId);
    }

    /// <summary>
    /// Returns true if this session is still in startup-suppression (hasn't worked yet).
    /// If true, idle status should show "" instead of "bell".
    /// </summary>
    internal bool IsStartupSuppressed(string sessionId)
    {
        return this._startedSessionIds.Contains(sessionId);
    }

    /// <summary>
    /// Gets the resolved CopilotHostInfo for a session, or null if no host has been resolved yet.
    /// </summary>
    internal CopilotHostInfo? GetCopilotHost(string sessionId)
    {
        return this._copilotHosts.TryGetValue(sessionId, out CopilotHostInfo? info) ? info : null;
    }

    /// <summary>
    /// Sets/updates the host entry for a session. Triggers projection into _activeTrackedWindows
    /// by HWND identity. Idempotent: identical re-set is a no-op.
    /// </summary>
    internal void SetCopilotHost(string sessionId, CopilotHostInfo info)
    {
        if (this._copilotHosts.TryGetValue(sessionId, out CopilotHostInfo? existing) &&
            existing.HostHwnd == info.HostHwnd &&
            existing.HostPid == info.HostPid &&
            existing.CopilotPid == info.CopilotPid &&
            existing.ParentHostHwnd == info.ParentHostHwnd &&
            string.Equals(existing.PaneRuntimeId, info.PaneRuntimeId, StringComparison.Ordinal) &&
            existing.PaneRootProcessId == info.PaneRootProcessId)
        {
            return;
        }

        if (this._copilotHosts.TryGetValue(sessionId, out existing))
        {
            this.UnprojectCopilotHostFromActiveWindows(sessionId, existing);
        }

        this._copilotHosts[sessionId] = info;
        this.ProjectCopilotHostToActiveWindows(sessionId, info);
        this.CopilotHostResolved?.Invoke(sessionId, info);
    }

    /// <summary>
    /// Removes the host entry for a session. No-op if missing.
    /// </summary>
    internal void RemoveCopilotHost(string sessionId)
    {
        if (!this._copilotHosts.Remove(sessionId, out CopilotHostInfo? removed))
        {
            return;
        }

        this.UnprojectCopilotHostFromActiveWindows(sessionId, removed);
        this.CopilotHostRemoved?.Invoke(sessionId);
    }

    /// <summary>
    /// T1 trigger: handles external session discovery from CopilotLogWatcherService.
    /// Resolves the host and sets Booster-Resolved Name placeholder if needed.
    /// </summary>
    internal void HandleExternalSessionDiscovered(string sessionId, int copilotPid)
    {
        var info = this.ResolveCopilotHost(sessionId, copilotPid, sessionSummary: null);
        if (info == null)
        {
            return;
        }

        this.SetCopilotHost(sessionId, info);

        // Set Booster-Resolved Name placeholder if no override exists yet
        var existing = SessionNameOverrideService.Get(Program.SessionNameOverrideFile, sessionId);
        if (existing == null)
        {
            var placeholder = BoosterResolvedNameFormatter.BuildPlaceholder(info.HostProcessName);
            SessionNameOverrideService.Set(Program.SessionNameOverrideFile, sessionId, placeholder, resolvedFromUserMessage: false);
        }
    }

    /// <summary>
    /// T2 trigger: handles internal Copilot PID registration from PidRegistryService.
    /// Resolves the host only if missing or dead, to avoid redundant re-resolution.
    /// </summary>
    internal void HandleInternalCopilotPidRegistered(string sessionId, int copilotPid)
    {
        // Don't re-resolve if we already have a host for this session and it's still alive
        var existing = this.GetCopilotHost(sessionId);
        if (existing != null && this.IsCopilotHostActive(existing))
        {
            return;
        }

        var info = this.ResolveCopilotHost(sessionId, copilotPid, sessionSummary: null);
        if (info == null)
        {
            return;
        }

        this.SetCopilotHost(sessionId, info);

        // Internal sessions get the same placeholder treatment for parity (Q3 unified scope)
        var nameOverride = SessionNameOverrideService.Get(Program.SessionNameOverrideFile, sessionId);
        if (nameOverride == null)
        {
            var placeholder = BoosterResolvedNameFormatter.BuildPlaceholder(info.HostProcessName);
            SessionNameOverrideService.Set(Program.SessionNameOverrideFile, sessionId, placeholder, resolvedFromUserMessage: false);
        }
    }

    /// <summary>
    /// T5 trigger: handles window destruction. Evicts any host entry whose HWND matches.
    /// </summary>
    internal void HandleWindowDestroyed(IntPtr hwnd)
    {
        this._windowsTerminalPaneCache.InvalidatePane(hwnd);

        // Find any session whose host HWND matches and evict
        var toRemove = this._copilotHosts
            .Where(kvp => kvp.Value.HostHwnd == hwnd || kvp.Value.ParentHostHwnd == hwnd)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var sid in toRemove)
        {
            this.RemoveCopilotHost(sid);
        }
    }

    internal HashSet<string> HandleWindowNameChanged(IntPtr hwnd)
    {
        this._windowsTerminalPaneCache.InvalidateForTerminalWindow(hwnd);

        var affected = this._copilotHosts
            .Where(kvp => IsWindowsTerminalHost(kvp.Value) && GetParentHostHwnd(kvp.Value) == hwnd)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (affected.Count > 0)
        {
            RuntimeDiagnosticLog.Write(
                "WT title/name changed hwnd={0}; preserving {1} host projection(s): {2}",
                hwnd,
                affected.Count,
                string.Join(",", affected));
        }

        return affected;
    }

    private CopilotHostInfo? ResolveCopilotHost(string sessionId, int copilotPid, string? sessionSummary)
    {
        var info = this._hostResolver.Resolve(copilotPid);
        var wtContext = this._hostResolver.ResolveWindowsTerminalContext(copilotPid);
        if (wtContext != null)
        {
            return this.ResolveWindowsTerminalAcrossCandidates(sessionId, copilotPid, sessionSummary, wtContext);
        }

        if (info == null)
        {
            return null;
        }

        return IsWindowsTerminalHost(info)
            ? this.ResolveWindowsTerminalPane(sessionId, info, sessionSummary, paneRootPid: null)
            : info;
    }

    /// <summary>
    /// Picks the right wt window hwnd from <paramref name="wtContext"/>'s candidates by
    /// asking the pane gateway which window's pane tree physically owns the copilot
    /// session. Single-candidate path (the dominant case) collapses to the legacy
    /// behaviour. Multi-candidate path: try each hwnd via <see cref="ResolveWindowsTerminalPane"/>;
    /// keep the first one that produces a non-fallback pane match (real pane title /
    /// runtime id / pane-root pid). If none match, return the result for the first
    /// candidate so we still produce a host info (better than dropping the session).
    /// </summary>
    private CopilotHostInfo ResolveWindowsTerminalAcrossCandidates(
        string sessionId,
        int copilotPid,
        string? sessionSummary,
        WindowsTerminalHostContext wtContext)
    {
        var candidates = wtContext.CandidateHostHwnds;
        if (candidates == null || candidates.Count == 0)
        {
            candidates = [wtContext.HostHwnd];
        }

        CopilotHostInfo? firstAttempt = null;
        foreach (var candidateHwnd in candidates)
        {
            var wtInfo = new CopilotHostInfo(
                candidateHwnd,
                wtContext.HostPid,
                copilotPid,
                wtContext.HostProcessName,
                wtContext.HostKindLabel,
                candidateHwnd,
                PaneRootProcessId: wtContext.PaneRootPid);
            var resolved = this.ResolveWindowsTerminalPane(sessionId, wtInfo, sessionSummary, wtContext.PaneRootPid);

            if (IsRealPaneMatch(resolved))
            {
                if (candidates.Count > 1)
                {
                    RuntimeDiagnosticLog.Write(
                        "WT multi-hwnd disambiguated session={0} copilotPid={1} wtPid={2} chosenHwnd={3} candidates=[{4}]",
                        sessionId,
                        copilotPid,
                        wtContext.HostPid,
                        candidateHwnd,
                        string.Join(",", candidates));
                }
                return resolved;
            }

            firstAttempt ??= resolved;
        }

        RuntimeDiagnosticLog.Write(
            "WT multi-hwnd no pane match session={0} copilotPid={1} wtPid={2} candidates=[{3}] fallback={4}",
            sessionId,
            copilotPid,
            wtContext.HostPid,
            string.Join(",", candidates),
            candidates[0]);
        return firstAttempt!;
    }

    private static bool IsRealPaneMatch(CopilotHostInfo info)
    {
        // ResolveWindowsTerminalPane returns the wtWindowHwnd as both HostHwnd and
        // ParentHostHwnd when no pane matched (the fallback path); a real pane match
        // populates PaneRuntimeId AND/OR PaneTitle from the gateway.
        return !string.IsNullOrEmpty(info.PaneRuntimeId)
            || !string.IsNullOrEmpty(info.PaneTitle);
    }

    private CopilotHostInfo ResolveWindowsTerminalPane(string sessionId, CopilotHostInfo info, string? sessionSummary, int? paneRootPid)
    {
        var wtWindowHwnd = GetParentHostHwnd(info);
        var terms = BuildWindowsTerminalPaneMatchTerms(sessionId, sessionSummary);
        var result = this._windowsTerminalPaneGateway.EnumeratePanes(wtWindowHwnd);
        LogWindowsTerminalPaneEnumeration(result, wtWindowHwnd, info.CopilotPid);
        RuntimeDiagnosticLog.Write(
            "WT resolve session={0} copilotPid={1} paneRootPid={2} hwnd={3} panes=[{4}]",
            sessionId,
            info.CopilotPid,
            paneRootPid?.ToString(CultureInfo.InvariantCulture) ?? "null",
            wtWindowHwnd,
            FormatPaneDiagnostics(result.Panes));

        var pane = FindMatchingPane(result.Panes, info.CopilotPid, terms, preferredTitle: null, paneRootPid);
        if (pane == null)
        {
            return info with { HostHwnd = wtWindowHwnd, ParentHostHwnd = wtWindowHwnd };
        }

        var paneHwnd = pane.Hwnd == IntPtr.Zero ? wtWindowHwnd : pane.Hwnd;
        this._windowsTerminalPaneCache.Set(wtWindowHwnd, info.CopilotPid, paneHwnd, pane.Name, pane.RuntimeId);
        return info with
        {
            HostHwnd = paneHwnd,
            ParentHostHwnd = wtWindowHwnd,
            PaneTitle = pane.Name,
            PaneRuntimeId = pane.RuntimeId,
            PaneRootProcessId = paneRootPid ?? pane.PaneRootProcessId
        };
    }

    private void FocusCopilotHost(string sessionId, CopilotHostInfo hostInfo)
    {
        RuntimeDiagnosticLog.Write(
            "FocusCopilotHost session={0} copilotPid={1} hostPid={2} host={3} parent={4} runtimeId={5} paneRootPid={6} paneTitle={7}",
            sessionId,
            hostInfo.CopilotPid,
            hostInfo.HostPid,
            hostInfo.HostHwnd,
            hostInfo.ParentHostHwnd,
            hostInfo.PaneRuntimeId ?? "null",
            hostInfo.PaneRootProcessId?.ToString(CultureInfo.InvariantCulture) ?? "null",
            hostInfo.PaneTitle ?? "null");

        if (IsWindowsTerminalHost(hostInfo) && hostInfo.ParentHostHwnd != IntPtr.Zero)
        {
            this._focusWindowHandle(hostInfo.ParentHostHwnd);
            this.TrySelectWindowsTerminalPane(hostInfo);
            return;
        }

        // Defensive live re-resolve: hostInfo doesn't claim Windows Terminal, but copilot
        // may actually be hosted inside one (stale cache rehydrated before wt was up,
        // pre-wt resolution captured a transient parent, or the HostKindClassifier missed
        // a forked wt build). Walk the parent chain now and project to the live wt context
        // when found, then persist so subsequent clicks skip the re-probe.
        var liveWtContext = this._hostResolver.ResolveWindowsTerminalContext(hostInfo.CopilotPid);
        if (liveWtContext != null)
        {
            var liveWt = this.ResolveWindowsTerminalAcrossCandidates(sessionId, hostInfo.CopilotPid, sessionSummary: null, liveWtContext);
            RuntimeDiagnosticLog.Write(
                "FocusCopilotHost live-wt re-resolve session={0} copilotPid={1} prevHostKind={2} wtHwnd={3} runtimeId={4}",
                sessionId,
                hostInfo.CopilotPid,
                hostInfo.HostKindLabel,
                liveWt.HostHwnd,
                liveWt.PaneRuntimeId ?? "null");
            this.SetCopilotHost(sessionId, liveWt);
            this._focusWindowHandle(GetParentHostHwnd(liveWt));
            this.TrySelectWindowsTerminalPane(liveWt);
            return;
        }

        this._focusWindowHandle(hostInfo.HostHwnd);
    }

    private bool TrySelectWindowsTerminalPane(CopilotHostInfo hostInfo)
    {
        var wtWindowHwnd = GetParentHostHwnd(hostInfo);
        if (!string.IsNullOrWhiteSpace(hostInfo.PaneRuntimeId))
        {
            try
            {
                if (this._windowsTerminalPaneGateway.FocusPane(wtWindowHwnd, hostInfo.PaneRuntimeId))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Program.Logger.LogInformation(
                    "Windows Terminal pane runtime-id focus failed for pid {CopilotPid} in hwnd {Hwnd}: {Error}",
                    hostInfo.CopilotPid,
                    wtWindowHwnd,
                    ex.Message);
            }
        }

        var terms = BuildWindowsTerminalPaneMatchTerms(string.Empty, sessionSummary: null);
        var result = this._windowsTerminalPaneGateway.EnumeratePanes(wtWindowHwnd);
        LogWindowsTerminalPaneEnumeration(result, wtWindowHwnd, hostInfo.CopilotPid);

        var pane = FindMatchingPane(result.Panes, hostInfo.CopilotPid, terms, hostInfo.PaneTitle, hostInfo.PaneRootProcessId);
        if (pane == null)
        {
            return false;
        }

        try
        {
            pane.Select();
            var paneHwnd = pane.Hwnd == IntPtr.Zero ? wtWindowHwnd : pane.Hwnd;
            this._windowsTerminalPaneCache.Set(wtWindowHwnd, hostInfo.CopilotPid, paneHwnd, pane.Name, pane.RuntimeId);
            return true;
        }
        catch (Exception ex)
        {
            Program.Logger.LogInformation(
                "Windows Terminal pane selection failed for pid {CopilotPid} in hwnd {Hwnd}: {Error}",
                hostInfo.CopilotPid,
                wtWindowHwnd,
                ex.Message);
            return false;
        }
    }

    private static string FormatPaneDiagnostics(IReadOnlyList<WindowsTerminalPaneInfo> panes)
    {
        return string.Join(
            "; ",
            panes.Select(pane => string.Create(
                CultureInfo.InvariantCulture,
                $"name='{pane.Name}',runtime='{pane.RuntimeId ?? "null"}',pid={pane.ProcessId},paneRoot={pane.PaneRootProcessId?.ToString(CultureInfo.InvariantCulture) ?? "null"},selected={pane.IsSelected}")));
    }

    private static void LogWindowsTerminalPaneEnumeration(WindowsTerminalPaneEnumeration result, IntPtr wtWindowHwnd, int copilotPid)
    {
        if (result.Panes.Count == 0)
        {
            Program.Logger.LogInformation(
                "Windows Terminal pane enumeration returned no panes for pid {CopilotPid} in hwnd {Hwnd}; using parent window fallback",
                copilotPid,
                wtWindowHwnd);
        }
        else if (result.IsPartial)
        {
            Program.Logger.LogInformation(
                "Windows Terminal pane enumeration returned partial results for pid {CopilotPid} in hwnd {Hwnd}; match may fall back to parent window",
                copilotPid,
                wtWindowHwnd);
        }
    }

    private static WindowsTerminalPaneInfo? FindMatchingPane(
        IReadOnlyList<WindowsTerminalPaneInfo> panes,
        int copilotPid,
        IReadOnlyList<string> terms,
        string? preferredTitle,
        int? paneRootPid)
    {
        if (paneRootPid.HasValue)
        {
            var paneRootMatches = panes.Where(pane => pane.PaneRootProcessId == paneRootPid.Value).ToList();
            if (paneRootMatches.Count > 0)
            {
                return ChooseBestPane(paneRootMatches);
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredTitle))
        {
            var preferredMatches = panes
                .Where(pane => string.Equals(NormalizePaneTitle(pane.Name), NormalizePaneTitle(preferredTitle), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (preferredMatches.Count > 0)
            {
                return ChooseBestPane(preferredMatches);
            }
        }

        var processMatches = panes.Where(pane => pane.ProcessId == copilotPid).ToList();
        if (processMatches.Count > 0)
        {
            return ChooseBestPane(processMatches);
        }

        var titleMatches = panes.Where(pane => IsPaneTitleMatch(pane.Name, terms)).ToList();
        return titleMatches.Count == 0 ? null : ChooseBestPane(titleMatches);
    }

    private static WindowsTerminalPaneInfo ChooseBestPane(List<WindowsTerminalPaneInfo> panes)
    {
        return panes.FirstOrDefault(pane => pane.IsSelected) ?? panes[0];
    }

    private static bool IsPaneTitleMatch(string paneTitle, IReadOnlyList<string> terms)
    {
        var normalizedTitle = NormalizePaneTitle(paneTitle);
        foreach (var term in terms)
        {
            var normalizedTerm = NormalizePaneTitle(term);
            if (string.Equals(normalizedTitle, normalizedTerm, StringComparison.OrdinalIgnoreCase)
                || (normalizedTerm.Length >= 16 && normalizedTitle.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePaneTitle(string title)
    {
        return WindowFocusService.StripLeadingEmoji(title).Trim();
    }

    private static List<string> BuildWindowsTerminalPaneMatchTerms(string sessionId, string? sessionSummary)
    {
        var terms = new List<string>();
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            AddPaneMatchTerm(terms, $"Copilot CLI - {sessionId}");
            AddPaneMatchTerm(terms, sessionId);

            var overrideEntry = SessionNameOverrideService.Get(Program.SessionNameOverrideFile, sessionId);
            AddPaneMatchTerm(terms, overrideEntry?.Name);
            AddPaneMatchTerm(terms, TryReadWorkspaceSummary(sessionId));
        }

        AddPaneMatchTerm(terms, sessionSummary);
        return terms;
    }

    private static void AddPaneMatchTerm(List<string> terms, string? term)
    {
        if (string.IsNullOrWhiteSpace(term) || s_ignoredSummaries.Contains(term))
        {
            return;
        }

        if (!terms.Contains(term, StringComparer.OrdinalIgnoreCase))
        {
            terms.Add(term);
        }
    }

    private static string? TryReadWorkspaceSummary(string sessionId)
    {
        try
        {
            var workspaceFile = Path.Combine(Program.SessionStateDir, sessionId, "workspace.yaml");
            if (!File.Exists(workspaceFile))
            {
                return null;
            }

            foreach (var line in File.ReadLines(workspaceFile))
            {
                if (line.StartsWith("summary:", StringComparison.OrdinalIgnoreCase))
                {
                    return line[8..].Trim().Trim('"');
                }
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Failed to read workspace summary for pane match: {Error}", ex.Message);
        }

        return null;
    }

    private static bool IsWindowsTerminalHost(CopilotHostInfo info)
    {
        return string.Equals(info.HostKindLabel, "Windows Terminal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(info.HostProcessName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(info.HostProcessName, "wt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(info.HostProcessName, "wt.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static IntPtr GetParentHostHwnd(CopilotHostInfo info)
    {
        return info.ParentHostHwnd == IntPtr.Zero ? info.HostHwnd : info.ParentHostHwnd;
    }

    internal static HashSet<string> LoadActiveSessionIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var activeSessions = SessionService.GetActiveSessions(Program.PidRegistryFile, Program.SessionStateDir);
            foreach (var s in activeSessions)
            {
                ids.Add(s.Id);
            }
        }
        catch (Exception ex) { Program.Logger.LogWarning("Failed to load active session IDs: {Error}", ex.Message); }
        return ids;
    }

    /// <summary>
    /// Resolves which session owns the given window HWND by checking all tracking collections.
    /// Returns the session ID or null if the window is not tracked.
    /// </summary>
    internal string? ResolveSessionForHwnd(IntPtr hwnd)
    {
        int pid = WindowFocusService.GetWindowProcessId(hwnd);

        // Collect ALL sessions whose tracked windows include this hwnd. A single wt
        // window typically hosts multiple copilot tabs (one per session) — they all
        // map to the same wt hwnd, so the simple first-match returns whichever
        // sessionId iterates first and silently mis-attributes the foreground tab.
        var matchingSessions = new List<string>();
        foreach (var kvp in this._activeTrackedWindows)
        {
            if (kvp.Value.Any(t => t.Hwnd == hwnd))
            {
                matchingSessions.Add(kvp.Key);
            }
        }

        if (matchingSessions.Count == 1)
        {
            return matchingSessions[0];
        }

        if (matchingSessions.Count > 1)
        {
            // Multiple sessions share this hwnd — disambiguate via the currently-
            // selected pane. Match the selected pane's UIA RuntimeId against each
            // candidate session's stored CopilotHostInfo.PaneRuntimeId. This cannot
            // false-positive: pane runtime ids are unique within a wt window.
            try
            {
                var panes = this._windowsTerminalPaneGateway.EnumeratePanes(hwnd).Panes;
                var selectedPane = panes.FirstOrDefault(p => p.IsSelected);
                if (selectedPane != null)
                {
                    if (!string.IsNullOrEmpty(selectedPane.RuntimeId))
                    {
                        foreach (var sessionId in matchingSessions)
                        {
                            if (this._copilotHosts.TryGetValue(sessionId, out var host)
                                && !string.IsNullOrEmpty(host.PaneRuntimeId)
                                && string.Equals(host.PaneRuntimeId, selectedPane.RuntimeId, StringComparison.OrdinalIgnoreCase))
                            {
                                return sessionId;
                            }
                        }
                    }

                    // Fallback: when the resolver couldn't pin a unique pane during
                    // initial host resolution (common for new tabs whose user-set
                    // titles don't include any session metadata), each host's
                    // PaneRuntimeId stays null. PaneRootProcessId is reliably set
                    // from wtContext.PaneRootPid at create time AND the SELECTED
                    // pane's PaneRootProcessId is reliably populated by the gateway
                    // (UIA exposes the active pane's terminal hwnd descendant). Two
                    // sessions in the same wt have distinct pwsh ancestors, so this
                    // disambiguates without false-positive risk.
                    if (selectedPane.PaneRootProcessId.HasValue)
                    {
                        foreach (var sessionId in matchingSessions)
                        {
                            if (this._copilotHosts.TryGetValue(sessionId, out var host)
                                && host.PaneRootProcessId.HasValue
                                && host.PaneRootProcessId.Value == selectedPane.PaneRootProcessId.Value)
                            {
                                return sessionId;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Logger.LogDebug("ResolveSessionForHwnd pane disambiguation failed: {Error}", ex.Message);
            }

            return matchingSessions[0];
        }

        // Check tracked processes (IDEs)
        foreach (var kvp in this._trackedProcesses)
        {
            if (kvp.Value.Any(p => p.Hwnd == hwnd || (p.Pid > 0 && p.Pid == pid)))
            {
                return kvp.Key;
            }
        }

        // Check Edge workspaces
        foreach (var kvp in this._edgeWorkspaces)
        {
            if (kvp.Value.CachedHwnd == hwnd)
            {
                return kvp.Key;
            }
        }

        // Check Explorer windows
        foreach (var kvp in this._explorerWindows)
        {
            if (kvp.Value.Any(e => e.Hwnd == hwnd))
            {
                return kvp.Key;
            }
        }

        return null;
    }

    /// <summary>
    /// Reloads <see cref="_activeSessionIds"/> from the PID registry so that the
    /// PID-based fallback in <see cref="BuildActiveText"/> is current during
    /// incremental refreshes (which otherwise skip this reload).
    /// </summary>
    internal void RefreshActiveSessionIds()
    {
        this._activeSessionIds = LoadActiveSessionIds();
    }

    /// <summary>
    /// Syncs the terminal cache file with the set of actually open terminal windows.
    /// Adds newly discovered terminals and removes stale entries.
    /// </summary>
    internal static void SyncTerminalCache(HashSet<string> openTerminalSessionIds)
    {
        try
        {
            var cachedIds = TerminalCacheService.GetCachedTerminals(Program.TerminalCacheFile);

            // Remove cache entries for terminals that are no longer open
            foreach (var cachedId in cachedIds)
            {
                if (!openTerminalSessionIds.Contains(cachedId))
                {
                    TerminalCacheService.RemoveTerminal(Program.TerminalCacheFile, cachedId);
                }
            }

            // Add cache entries for newly discovered terminals
            foreach (var openId in openTerminalSessionIds)
            {
                if (!cachedIds.Contains(openId))
                {
                    TerminalCacheService.CacheTerminal(Program.TerminalCacheFile, openId, 0);
                }
            }
        }
        catch (Exception ex) { Program.Logger.LogDebug("Process not found: {Error}", ex.Message); }
    }

    internal string BuildActiveText(string sessionId)
    {
        var parts = new List<string>();
        var hasLiveCopilotHost = this._copilotHosts.TryGetValue(sessionId, out var copilotHost)
            && this.IsCopilotHostActive(copilotHost);
        if (hasLiveCopilotHost)
        {
            parts.Add("Copilot CLI");
        }

        if (this._activeTrackedWindows.TryGetValue(sessionId, out var tracked))
        {
            var visibleTracked = hasLiveCopilotHost
                ? tracked.Where(t => !t.Label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase)).ToList()
                : tracked;

            var terminals = visibleTracked.Where(t => t.Label.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase)).ToList();
            var copilotClis = visibleTracked.Where(t => t.Label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase)).ToList();
            int cliIndex = 0;
            foreach (var (label, _, _) in visibleTracked)
            {
                if (label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase))
                {
                    cliIndex++;
                    parts.Add(copilotClis.Count > 1 ? $"Copilot CLI #{cliIndex}" : "Copilot CLI");
                }
                else if (label.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(terminals.Count > 1 ? label : "Terminal");
                }
                else
                {
                    parts.Add(label);
                }
            }
        }
        else if (!hasLiveCopilotHost && this._activeSessionIds.Contains(sessionId))
        {
            parts.Add("Copilot CLI");
        }

        if (this._trackedProcesses.TryGetValue(sessionId, out var procs))
        {
            foreach (var proc in procs)
            {
                if (proc.Hwnd != IntPtr.Zero)
                {
                    if (this._isWindowAlive(proc.Hwnd))
                    {
                        parts.Add(proc.Name);
                    }
                    else if (proc.Pid > 0)
                    {
                        // HWND died but process still alive (e.g. VS splash screen closed,
                        // real window opened). Try to recapture immediately.
                        bool stillAlive = false;
                        try { stillAlive = !Process.GetProcessById(proc.Pid).HasExited; }
                        catch { }

                        if (stillAlive)
                        {
                            var newHwnd = WindowFocusService.FindWindowHandleByPid(proc.Pid);
                            if (newHwnd != IntPtr.Zero)
                            {
                                proc.Hwnd = newHwnd;
                                parts.Add(proc.Name);
                            }
                        }
                        // PID dead + HWND dead = IDE genuinely closed. Don't show.
                    }
                    // Pid=0 + HWND dead = launcher exited and window was destroyed. Don't show.
                }
                else if (proc.Pid > 0)
                {
                    bool alive = false;
                    try { alive = !Process.GetProcessById(proc.Pid).HasExited; }
                    catch { }

                    if (alive)
                    {
                        // Try to recapture HWND (e.g., after splash → main transition)
                        var newHwnd = WindowFocusService.FindWindowHandleByPid(proc.Pid);
                        if (newHwnd != IntPtr.Zero)
                        {
                            proc.Hwnd = newHwnd;
                            proc.HwndEverCaptured = true;
                        }

                        parts.Add(proc.Name);
                    }
                    else if (proc.FolderPath != null && !proc.HwndEverCaptured)
                    {
                        // Launcher exited, HWND never captured — keep showing while
                        // ForegroundChanged/OnWindowCreated tries to capture the real window.
                        parts.Add(proc.Name);
                    }
                    // PID dead + HWND was previously captured = IDE genuinely closed. Don't show.
                }
                else if (proc.FolderPath != null && !proc.HwndEverCaptured)
                {
                    // Pid=0, HWND never captured — launcher exited, waiting for window.
                    parts.Add(proc.Name);
                }
            }
        }

        if (this._explorerWindows.TryGetValue(sessionId, out var explorers))
        {
            foreach (var (label, hwnd) in explorers)
            {
                if (this._isWindowAlive(hwnd))
                {
                    parts.Add(label);
                }
            }
        }

        if (this._edgeWorkspaces.TryGetValue(sessionId, out var ws) && ws.IsOpen)
        {
            parts.Add("Edge");
        }

        if (this._teamsWindows.TryGetValue(sessionId, out var teams) && (teams.IsOpen || teams.IsPendingOpen))
        {
            parts.Add("Teams");
        }

        return string.Join("\n", parts);
    }

    private bool IsCopilotHostActive(CopilotHostInfo hostInfo)
    {
        var focusHwnd = IsWindowsTerminalHost(hostInfo) ? GetParentHostHwnd(hostInfo) : hostInfo.HostHwnd;
        return this._isWindowAlive(focusHwnd) && this._isProcessAlive(hostInfo.CopilotPid);
    }

    /// <summary>
    /// Tries to focus an existing Copilot CLI window for the given session.
    /// Returns true if a window was found and focused, false otherwise.
    /// </summary>
    internal bool TryFocusCopilotCli(string sessionId)
    {
        // Priority 1: Use host HWND if available and alive
        if (this._copilotHosts.TryGetValue(sessionId, out var hostInfo) && this.IsCopilotHostActive(hostInfo))
        {
            this.FocusCopilotHost(sessionId, hostInfo);
            return true;
        }

        // Priority 2: Check tracked windows (HWND-based, legacy title-scan path)
        if (this._activeTrackedWindows.TryGetValue(sessionId, out var tracked))
        {
            var cli = tracked.FirstOrDefault(t => t.Label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase));
            if (cli != default && this._isWindowAlive(cli.Hwnd))
            {
                WindowFocusService.TryFocusWindowHandle(cli.Hwnd);
                return true;
            }
        }

        // Priority 3: PID-based fallback
        if (this._activeSessionIds.Contains(sessionId))
        {
            var activeSessions = SessionService.GetActiveSessions(Program.PidRegistryFile, Program.SessionStateDir);
            var session = activeSessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null && session.CopilotPid > 0)
            {
                try
                {
                    var p = Process.GetProcessById(session.CopilotPid);
                    if (!p.HasExited)
                    {
                        WindowFocusService.TryFocusProcessWindow(session.CopilotPid);
                        return true;
                    }
                }
                catch { }
            }
        }

        return false;
    }

    internal void FocusActiveProcess(string sessionId, int clickedLineIndex)
    {
        var focusTargets = new List<(string name, Action focus)>();
        // Priority 1: Add host-resolved Copilot CLI first if available and alive
        if (this._copilotHosts.TryGetValue(sessionId, out var hostInfo) && this.IsCopilotHostActive(hostInfo))
        {
            var capturedHostInfo = hostInfo;
            var capturedSessionId = sessionId;
            focusTargets.Add(("Copilot CLI", () => this.FocusCopilotHost(capturedSessionId, capturedHostInfo)));
        }

        // Priority 2: Add tracked windows (legacy title-scan path)
        if (this._activeTrackedWindows.TryGetValue(sessionId, out var tracked))
        {
            foreach (var (label, title, hwnd) in tracked)
            {
                // Skip if this is the host HWND we already added (avoid duplicate)
                if (this._copilotHosts.TryGetValue(sessionId, out var h)
                    && (h.HostHwnd == hwnd || h.ParentHostHwnd == hwnd))
                {
                    continue;
                }

                var capturedHwnd = hwnd;
                focusTargets.Add((label, () => WindowFocusService.TryFocusWindowHandle(capturedHwnd)));
            }
        }
        else if (this._activeSessionIds.Contains(sessionId))
        {
            // Priority 3: PID-based fallback for Copilot CLI sessions without a titled window
            var activeSessions = SessionService.GetActiveSessions(Program.PidRegistryFile, Program.SessionStateDir);
            var session = activeSessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null && session.CopilotPid > 0)
            {
                var pid = session.CopilotPid;
                focusTargets.Add(("Copilot CLI", () => WindowFocusService.TryFocusProcessWindow(pid)));
            }
        }

        if (this._trackedProcesses.TryGetValue(sessionId, out var procs))
        {
            foreach (var proc in procs)
            {
                // Prefer HWND-based focus (avoids VS/VS Code title collision)
                if (proc.Hwnd != IntPtr.Zero && this._isWindowAlive(proc.Hwnd))
                {
                    var capturedHwnd = proc.Hwnd;
                    focusTargets.Add((proc.Name, () => WindowFocusService.TryFocusWindowHandle(capturedHwnd)));
                }
                else if (proc.Pid > 0)
                {
                    try
                    {
                        var p = Process.GetProcessById(proc.Pid);
                        if (!p.HasExited)
                        {
                            var capturedPid = proc.Pid;
                            focusTargets.Add((proc.Name, () => WindowFocusService.TryFocusProcessWindow(capturedPid)));
                        }
                    }
                    catch (Exception ex) { Program.Logger.LogDebug("Process not found for focus: {Error}", ex.Message); }
                }
            }
        }

        if (this._explorerWindows.TryGetValue(sessionId, out var explorers))
        {
            foreach (var (label, hwnd) in explorers)
            {
                if (this._isWindowAlive(hwnd))
                {
                    var capturedHwnd = hwnd;
                    focusTargets.Add((label, () => WindowFocusService.TryFocusWindowHandle(capturedHwnd)));
                }
            }
        }

        if (this._edgeWorkspaces.TryGetValue(sessionId, out var ws) && ws.IsOpen)
        {
            focusTargets.Add(("Edge", () => ws.Focus()));
        }

        if (this._teamsWindows.TryGetValue(sessionId, out var teams) && teams.IsOpen)
        {
            focusTargets.Add(("Teams", () => teams.Focus()));
        }

        if (focusTargets.Count == 0)
        {
            return;
        }

        // Auto-hide: minimize tracked windows from other sessions
        if (Program._settings.AutoHideOnFocus)
        {
            this.MinimizeOtherSessions(sessionId);
        }

        // Directly focus the target matching the clicked line
        var index = Math.Min(clickedLineIndex, focusTargets.Count - 1);
        RuntimeDiagnosticLog.Write(
            "FocusActiveProcess session={0} clickedLine={1} pickedIndex={2} pickedLabel={3} targets=[{4}]",
            sessionId,
            clickedLineIndex,
            index,
            focusTargets[index].name,
            string.Join("|", focusTargets.Select((t, i) => string.Create(CultureInfo.InvariantCulture, $"#{i}:{t.name}"))));
        focusTargets[index].focus();
    }

    /// <summary>
    /// Minimizes all tracked windows belonging to sessions other than the specified one.
    /// Only targets windows tracked by CopilotBooster (terminals, IDEs, Edge workspaces).
    /// </summary>
    internal void MinimizeOtherSessions(string excludeSessionId)
    {
        // Collect HWNDs belonging to the focused session so we never minimize them
        var excludeHwnds = new HashSet<IntPtr>();
        if (this._activeTrackedWindows.TryGetValue(excludeSessionId, out var focusedWindows))
        {
            foreach (var (_, _, hwnd) in focusedWindows)
            {
                if (hwnd != IntPtr.Zero)
                {
                    excludeHwnds.Add(hwnd);
                }
            }
        }

        if (this._edgeWorkspaces.TryGetValue(excludeSessionId, out var focusedEdge)
            && focusedEdge.CachedHwnd != IntPtr.Zero)
        {
            excludeHwnds.Add(focusedEdge.CachedHwnd);
        }

        if (this._teamsWindows.TryGetValue(excludeSessionId, out var focusedTeams)
            && focusedTeams.CachedHwnd != IntPtr.Zero)
        {
            excludeHwnds.Add(focusedTeams.CachedHwnd);
        }

        if (this._explorerWindows.TryGetValue(excludeSessionId, out var focusedExplorers))
        {
            foreach (var (_, hwnd) in focusedExplorers)
            {
                if (hwnd != IntPtr.Zero)
                {
                    excludeHwnds.Add(hwnd);
                }
            }
        }

        // Snapshot to avoid concurrent modification with Refresh
        var trackedWindows = this._activeTrackedWindows.ToList();
        foreach (var kvp in trackedWindows)
        {
            if (string.Equals(kvp.Key, excludeSessionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (_, _, hwnd) in kvp.Value)
            {
                if (hwnd != IntPtr.Zero && !excludeHwnds.Contains(hwnd)
                    && this._isWindowAlive(hwnd)
                    && !IsCmdExeTitle(hwnd))
                {
                    WindowFocusService.MinimizeWindow(hwnd);
                }
            }
        }

        var trackedProcs = this._trackedProcesses.ToList();
        foreach (var kvp in trackedProcs)
        {
            if (string.Equals(kvp.Key, excludeSessionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var proc in kvp.Value.ToList())
            {
                if (proc.Hwnd != IntPtr.Zero && !excludeHwnds.Contains(proc.Hwnd)
                    && this._isWindowAlive(proc.Hwnd))
                {
                    WindowFocusService.MinimizeWindow(proc.Hwnd);
                }
            }
        }

        foreach (var kvp in this._edgeWorkspaces.ToList())
        {
            if (string.Equals(kvp.Key, excludeSessionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // IsOpen refreshes CachedHwnd if needed
            if (kvp.Value.IsOpen && kvp.Value.CachedHwnd != IntPtr.Zero)
            {
                WindowFocusService.MinimizeWindow(kvp.Value.CachedHwnd);
            }
        }

        foreach (var kvp in this._teamsWindows.ToList())
        {
            if (string.Equals(kvp.Key, excludeSessionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (kvp.Value.IsOpen && kvp.Value.CachedHwnd != IntPtr.Zero)
            {
                WindowFocusService.MinimizeWindow(kvp.Value.CachedHwnd);
            }
        }

        foreach (var kvp in this._explorerWindows.ToList())
        {
            if (string.Equals(kvp.Key, excludeSessionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (_, hwnd) in kvp.Value)
            {
                if (hwnd != IntPtr.Zero && !excludeHwnds.Contains(hwnd)
                    && this._isWindowAlive(hwnd))
                {
                    WindowFocusService.MinimizeWindow(hwnd);
                }
            }
        }
    }

    /// <summary>
    /// Returns true if the window title is a generic cmd.exe title (not yet renamed by copilot).
    /// These windows should not be minimized since they can't be reliably re-focused.
    /// </summary>
    private static bool IsCmdExeTitle(IntPtr hwnd)
    {
        var title = WindowFocusService.GetWindowTitle(hwnd);
        return title.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a dictionary mapping non-empty session summaries to session IDs
    /// for window title matching. Excludes generic titles like "GitHub Copilot".
    /// </summary>
    internal static Dictionary<string, string> BuildSessionSummaryMap(List<NamedSession> sessions)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions)
        {
            if (!string.IsNullOrWhiteSpace(session.Summary)
                && !s_ignoredSummaries.Contains(session.Summary)
                && !map.ContainsKey(session.Summary))
            {
                map[session.Summary] = session.Id;
            }
        }
        return map;
    }

    /// <summary>
    /// Performs a full refresh of active-status tracking state including Win32 calls,
    /// process liveness checks, and cache persistence. Called at startup and by the fallback timer.
    /// </summary>
    internal ActiveStatusSnapshot FullRefresh(List<NamedSession> sessions)
    {
        // Snapshot to avoid concurrent modification from UI thread
        var sessionSnapshot = sessions.ToList();
        this._activeSessionIds = LoadActiveSessionIds();

        // Scan for open tracked windows by title (including session-summary matching)
        // Pass previously tracked HWNDs as fallback for Copilot CLI windows whose titles change dynamically
        this._activeTrackedWindows = WindowFocusService.FindTrackedWindows(BuildSessionSummaryMap(sessionSnapshot), this._activeTrackedWindows);

        // Propagate title-scan ground truth into _copilotHosts BEFORE we project hosts
        // back into _activeTrackedWindows. This is the FullRefresh complement to the
        // OnWindowTitleChanged-driven rebind: when wt tabs are renamed before the
        // booster starts, no EVENT_OBJECT_NAMECHANGE fires, so the title-change rebind
        // never runs. The startup title-scan still finds the right wt hwnd via tab
        // title match — without this rebind, _copilotHosts stays at the resolver's
        // wrong fallback hwnd indefinitely and click-to-focus targets the wrong wt.
        this.RebindCopilotHostsFromTitleScannedWindows();

        this.ReprojectActiveCopilotHosts();

        // Sync terminal cache with actual open windows
        var openTerminalIds = new HashSet<string>(this._activeTrackedWindows.Keys, StringComparer.OrdinalIgnoreCase);
        SyncTerminalCache(openTerminalIds);

        // Load cached window handles on first refresh (restores IDE/Explorer/Edge tracking across app restarts)
        if (!this._handleCacheInitialLoadDone)
        {
            this._handleCacheInitialLoadDone = true;
            var (cachedProcesses, cachedExplorers, cachedEdges, cachedTeams, cachedHosts) = WindowHandleCacheService.Load(Program.WindowHandleCacheFile);
            foreach (var kvp in cachedProcesses)
            {
                if (!this._trackedProcesses.ContainsKey(kvp.Key))
                {
                    this._trackedProcesses[kvp.Key] = kvp.Value;
                }
            }

            foreach (var kvp in cachedExplorers)
            {
                if (!this._explorerWindows.ContainsKey(kvp.Key))
                {
                    this._explorerWindows[kvp.Key] = kvp.Value;
                }
            }

            foreach (var kvp in cachedEdges)
            {
                if (!this._edgeWorkspaces.ContainsKey(kvp.Key))
                {
                    this._edgeWorkspaces[kvp.Key] = new EdgeWorkspaceService(kvp.Key) { CachedHwnd = kvp.Value };
                }
            }

            foreach (var kvp in cachedTeams)
            {
                if (!this._teamsWindows.ContainsKey(kvp.Key))
                {
                    var teams = new TeamsWindowService();
                    teams.RestoreCachedHwnd(kvp.Value);
                    this._teamsWindows[kvp.Key] = teams;
                }
            }

            foreach (var kvp in cachedHosts)
            {
                if (!this._copilotHosts.ContainsKey(kvp.Key))
                {
                    this._copilotHosts[kvp.Key] = kvp.Value;
                    this.ProjectCopilotHostToActiveWindows(kvp.Key, kvp.Value);
                }
            }
            this.ReprojectActiveCopilotHosts();

            // Also load legacy ide-cache.json if window-handles.json doesn't exist yet
            if (!File.Exists(Program.WindowHandleCacheFile) && File.Exists(Program.IdeCacheFile))
            {
                var legacyCached = IdeCacheService.Load(Program.IdeCacheFile);
                foreach (var kvp in legacyCached)
                {
                    if (!this._trackedProcesses.ContainsKey(kvp.Key))
                    {
                        this._trackedProcesses[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        // Clean up dead tracked processes and capture HWNDs for those that don't have one yet
        foreach (var kvp in this._trackedProcesses.ToList())
        {
            for (int i = kvp.Value.Count - 1; i >= 0; i--)
            {
                var proc = kvp.Value[i];

                // If we have a cached HWND, check if it's still alive
                if (proc.Hwnd != IntPtr.Zero)
                {
                    if (!this._isWindowAlive(proc.Hwnd))
                    {
                        // HWND died — try to recapture from the same PID (e.g. VS opening a .sln)
                        if (proc.Pid > 0)
                        {
                            bool stillAlive;
                            try { stillAlive = !Process.GetProcessById(proc.Pid).HasExited; }
                            catch (Exception ex) { stillAlive = false; Program.Logger.LogDebug("Process exited: {Error}", ex.Message); }

                            if (stillAlive)
                            {
                                var newHwnd = WindowFocusService.FindWindowHandleByPid(proc.Pid);
                                if (newHwnd != IntPtr.Zero)
                                {
                                    proc.Hwnd = newHwnd;
                                    continue;
                                }
                            }
                        }

                        kvp.Value.RemoveAt(i);
                    }

                    continue;
                }

                // No HWND yet — try to capture one from the PID
                bool alive;
                try { alive = !Process.GetProcessById(proc.Pid).HasExited; }
                catch (Exception ex) { alive = false; Program.Logger.LogDebug("Process exited: {Error}", ex.Message); }

                if (alive)
                {
                    // Try to find the window handle by PID
                    var hwnd = WindowFocusService.FindWindowHandleByPid(proc.Pid);
                    if (hwnd != IntPtr.Zero)
                    {
                        proc.Hwnd = hwnd;
                    }
                }
                else if (proc.FolderPath != null)
                {
                    // Launcher exited — try to find the real IDE window by title
                    var folderName = Path.GetFileName(proc.FolderPath.TrimEnd('\\'));
                    var hwnd = WindowFocusService.FindWindowHandleByTitle(folderName, proc.Name);
                    if (hwnd != IntPtr.Zero)
                    {
                        proc.Hwnd = hwnd;
                        proc.Pid = 0;
                    }
                    else
                    {
                        kvp.Value.RemoveAt(i);
                    }
                }
                else
                {
                    kvp.Value.RemoveAt(i);
                }
            }
        }

        // Clean up dead explorer windows
        foreach (var kvp in this._explorerWindows)
        {
            kvp.Value.RemoveAll(e => !this._isWindowAlive(e.Hwnd));
        }

        var emptyExplorers = new List<string>();
        foreach (var kvp in this._explorerWindows)
        {
            if (kvp.Value.Count == 0)
            {
                emptyExplorers.Add(kvp.Key);
            }
        }

        foreach (var id in emptyExplorers)
        {
            this._explorerWindows.Remove(id);
        }

        // Clean up closed Edge workspaces
        var closedEdge = new List<string>();
        foreach (var kvp in this._edgeWorkspaces.ToList())
        {
            if (!kvp.Value.IsOpen)
            {
                closedEdge.Add(kvp.Key);
                Program.Logger.LogInformation("[EdgeCleanup] Removing {SessionId} (IsOpen=false, HWND={Hwnd})", kvp.Key, kvp.Value.CachedHwnd);
            }
        }

        foreach (var id in closedEdge)
        {
            this._edgeWorkspaces.Remove(id);
        }

        // Clean up closed Teams windows (skip entries still pending HWND capture)
        var closedTeams = new List<string>();
        foreach (var kvp in this._teamsWindows.ToList())
        {
            if (!kvp.Value.IsPendingOpen && !kvp.Value.IsOpen)
            {
                closedTeams.Add(kvp.Key);
            }
        }

        foreach (var id in closedTeams)
        {
            if (this._teamsWindows.TryGetValue(id, out var tw))
            {
                tw.Release();
            }

            this._teamsWindows.Remove(id);
        }

        // T4: Re-resolve host for sessions whose host is missing or dead
        this._windowsTerminalPaneCache.Revalidate();
        var activeSessions = SessionService.GetActiveSessions(Program.PidRegistryFile, Program.SessionStateDir);
        foreach (var session in activeSessions)
        {
            if (session.CopilotPid <= 0)
            {
                continue;
            }

            var existing = this.GetCopilotHost(session.Id);
            if (existing != null && this.IsCopilotHostActive(existing))
            {
                continue;
            }

            var info = this.ResolveCopilotHost(session.Id, session.CopilotPid, session.Summary);
            if (info != null)
            {
                this.SetCopilotHost(session.Id, info);
            }
            else if (existing != null)
            {
                this.RemoveCopilotHost(session.Id);
            }
        }

        this.ReprojectActiveCopilotHosts();

        // Edge workspace scanning happens separately via ScanAndTrackEdgeWorkspaces()
        // which must run on the UI (STA) thread for UI Automation to work.

        // Build active text for each session
        var activeTextBySessionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sessionNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessionSnapshot)
        {
            var activeText = this.BuildActiveText(session.Id);
            if (!string.IsNullOrEmpty(activeText))
            {
                activeTextBySessionId[session.Id] = activeText;
            }

            var displayName = !string.IsNullOrEmpty(session.Alias) ? session.Alias : session.Summary;
            if (!string.IsNullOrEmpty(displayName))
            {
                sessionNamesById[session.Id] = displayName;
            }
        }

        // Status icons from events.jsonl — read from cache only (watcher updates async).
        // Fallback poll runs only on watcher errors, rate-limited to 1/30s.
        this.EventsJournal.ProcessFallbackPoll(sessionSnapshot.Select(s => s.Id).ToList());
        var statusIconBySessionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessionSnapshot)
        {
            var status = this.EventsJournal.GetCachedStatus(session.Id);
            switch (status)
            {
                case EventsJournalService.SessionStatus.Working:
                    statusIconBySessionId[session.Id] = "working";
                    break;
                case EventsJournalService.SessionStatus.Idle:
                    statusIconBySessionId[session.Id] = this._startedSessionIds.Contains(session.Id) ? "" : "bell";
                    break;
                case EventsJournalService.SessionStatus.IdleSilent:
                    // Silent idle — no bell, just clear the working state
                    statusIconBySessionId[session.Id] = "";
                    break;
            }
        }

        return new ActiveStatusSnapshot(activeTextBySessionId, sessionNamesById, statusIconBySessionId);
    }

    /// <summary>
    /// Builds an <see cref="ActiveStatusSnapshot"/> from the already-cached in-memory state
    /// without any Win32 calls, process liveness checks, or cache persistence.
    /// Intended for fast, event-driven refreshes between full refresh cycles.
    /// </summary>
    internal ActiveStatusSnapshot IncrementalRefresh(List<NamedSession> sessions)
    {
        var sessionSnapshot = sessions.ToList();

        var activeTextBySessionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sessionNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessionSnapshot)
        {
            var activeText = this.BuildActiveText(session.Id);
            if (!string.IsNullOrEmpty(activeText))
            {
                activeTextBySessionId[session.Id] = activeText;
            }

            var displayName = !string.IsNullOrEmpty(session.Alias) ? session.Alias : session.Summary;
            if (!string.IsNullOrEmpty(displayName))
            {
                sessionNamesById[session.Id] = displayName;
            }
        }

        this.EventsJournal.ProcessFallbackPoll(sessionSnapshot.Select(s => s.Id).ToList());
        var statusIconBySessionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessionSnapshot)
        {
            var status = this.EventsJournal.GetCachedStatus(session.Id);
            switch (status)
            {
                case EventsJournalService.SessionStatus.Working:
                    statusIconBySessionId[session.Id] = "working";
                    break;
                case EventsJournalService.SessionStatus.Idle:
                    statusIconBySessionId[session.Id] = this._startedSessionIds.Contains(session.Id) ? "" : "bell";
                    break;
                case EventsJournalService.SessionStatus.IdleSilent:
                    statusIconBySessionId[session.Id] = "";
                    break;
            }
        }

        return new ActiveStatusSnapshot(activeTextBySessionId, sessionNamesById, statusIconBySessionId);
    }

    /// <summary>
    /// Called when a window's title changes. Updates tracking state by matching the new title
    /// against tracked session patterns. If the HWND was previously tracked but no longer matches,
    /// it is removed from tracking.
    /// </summary>
    /// <param name="hwnd">The window handle whose title changed.</param>
    /// <param name="title">The new window title.</param>
    /// <param name="sessionSummaries">Optional mapping of session summary to session ID for title matching.</param>
    internal HashSet<string> OnWindowTitleChanged(IntPtr hwnd, string title, Dictionary<string, string>? sessionSummaries)
    {
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var match = WindowFocusService.MatchTrackedWindowTitle(title, sessionSummaries);
        RuntimeDiagnosticLog.Write(
            "WindowTitleChanged hwnd={0} title='{1}' titleMatch={2}",
            hwnd,
            title,
            match.HasValue ? $"{match.Value.SessionId}:{match.Value.Label}" : "none");

        // Remove this HWND from any title-scan session it was previously tracked under.
        // Host-resolved Copilot CLI projections are keyed by session/runtime-id and survive WT title churn.
        foreach (var kvp in this._activeTrackedWindows)
        {
            int before = kvp.Value.Count;
            kvp.Value.RemoveAll(t => t.Hwnd == hwnd && !this.IsCopilotHostProjection(kvp.Key, t));
            if (kvp.Value.Count < before)
            {
                affected.Add(kvp.Key);
            }
        }

        if (match != null)
        {
            var (sessionId, label) = match.Value;
            if (!this._activeTrackedWindows.TryGetValue(sessionId, out List<(string Label, string Title, nint Hwnd)>? value))
            {
                value = [];
                this._activeTrackedWindows[sessionId] = value;
            }

            // Avoid duplicate HWND entries
            if (!value.Any(t => t.Hwnd == hwnd))
            {
                value.Add((label, title, hwnd));
            }

            // Keep PID registry current so BuildActiveText fallback works during incremental refreshes
            if (label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase))
            {
                this.RefreshActiveSessionIds();
            }

            affected.Add(sessionId);

            // Title-match is the strongest possible per-session identifier — both
            // "Copilot CLI - {sessionId}" and a session-summary equality match cannot
            // false-positive across wt windows. When the resolver mis-bound the host
            // (CopilotHostResolver pane-term match misses for tabs renamed by users
            // with arbitrary labels → ResolveWindowsTerminalAcrossCandidates falls back
            // to first-by-Z-order), this signal is what gives us the truth. Propagate
            // it into _copilotHosts so click-to-focus targets the right wt window.
            this.RebindWindowsTerminalHostFromTitleMatch(sessionId, hwnd);
        }

        return affected;
    }

    /// <summary>
    /// When <see cref="OnWindowTitleChanged"/> matches a session via title and the
    /// stored <see cref="CopilotHostInfo"/> for that session is a Windows Terminal
    /// host pointing at a different wt hwnd, rebind the host info to <paramref name="hwnd"/>
    /// (and re-resolve pane info against the right wt window). No-op when no host is
    /// stored, the host is not a wt, or the hwnd already matches.
    /// </summary>
    private void RebindWindowsTerminalHostFromTitleMatch(string sessionId, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!this._copilotHosts.TryGetValue(sessionId, out var existing))
        {
            return;
        }

        if (!IsWindowsTerminalHost(existing))
        {
            return;
        }

        if (existing.ParentHostHwnd == hwnd)
        {
            return;
        }

        RuntimeDiagnosticLog.Write(
            "WT title-match rebind session={0} prevHwnd={1} newHwnd={2} copilotPid={3}",
            sessionId,
            existing.ParentHostHwnd,
            hwnd,
            existing.CopilotPid);

        var rebased = existing with { HostHwnd = hwnd, ParentHostHwnd = hwnd };
        var resolved = this.ResolveWindowsTerminalPane(sessionId, rebased, sessionSummary: null, paneRootPid: existing.PaneRootProcessId);
        this.SetCopilotHost(sessionId, resolved);
    }

    /// <summary>
    /// Walks <see cref="_activeTrackedWindows"/> after a title-scan refresh and
    /// propagates any title-matched wt hwnd into <see cref="_copilotHosts"/>. Closes
    /// the gap where <see cref="OnWindowTitleChanged"/> only fires on title CHANGES
    /// (Win32 EVENT_OBJECT_NAMECHANGE) — when wt tabs were already named before the
    /// booster started, no name-change event ever fires for them, so the rebind
    /// never runs. This method runs unconditionally on every <see cref="FullRefresh"/>
    /// so the startup title-scan ground truth flows into <see cref="_copilotHosts"/>.
    ///
    /// Safety filter: only rebinds to hwnds whose owning process id equals the
    /// existing host's <see cref="CopilotHostInfo.HostPid"/>. <see cref="_activeTrackedWindows"/>
    /// can contain hwnds from any visible top-level window whose title matched a
    /// session (e.g., session-summary equality could match a Notepad window titled
    /// the same). Without this guard, a wt-typed host could be rebound to a non-wt
    /// hwnd in another process. The hook-driven path (OnWindowTitleChanged) does
    /// NOT need this filter because the WindowEventHookService passes the hwnd that
    /// fired the event directly, so the caller already validated provenance.
    /// </summary>
    private void RebindCopilotHostsFromTitleScannedWindows()
    {
        foreach (var kvp in this._activeTrackedWindows.ToList())
        {
            var sessionId = kvp.Key;
            if (!this._copilotHosts.TryGetValue(sessionId, out var existing) || !IsWindowsTerminalHost(existing))
            {
                continue;
            }

            foreach (var entry in kvp.Value.ToList())
            {
                if (entry.Hwnd == IntPtr.Zero || entry.Hwnd == existing.ParentHostHwnd)
                {
                    continue;
                }

                var hwndPid = WindowFocusService.GetWindowProcessId(entry.Hwnd);
                if (hwndPid <= 0 || hwndPid != existing.HostPid)
                {
                    continue;
                }

                this.RebindWindowsTerminalHostFromTitleMatch(sessionId, entry.Hwnd);
                // After rebind, refresh `existing` snapshot so subsequent entries in
                // this session's list re-evaluate against the new ParentHostHwnd.
                if (this._copilotHosts.TryGetValue(sessionId, out var refreshed))
                {
                    existing = refreshed;
                }
            }
        }
    }

    /// <summary>
    /// Called when a new window is created or gains focus. If the window's owning process matches
    /// a tracked process, captures the HWND for that process entry. Also handles launcher-based
    /// IDEs where the launcher PID exited (Pid=0) by matching the window title to FolderPath.
    /// </summary>
    /// <param name="hwnd">The window handle to try to associate.</param>
    internal string? OnWindowCreated(IntPtr hwnd)
    {
        int pid = WindowFocusService.GetWindowProcessId(hwnd);
        if (pid <= 0)
        {
            return null;
        }

        // First pass: match by PID (direct process match)
        foreach (var kvp in this._trackedProcesses)
        {
            foreach (var proc in kvp.Value)
            {
                if (proc.Pid == pid && proc.Hwnd == IntPtr.Zero)
                {
                    proc.Hwnd = hwnd;
                    proc.HwndEverCaptured = true;
                    return kvp.Key;
                }
            }
        }

        // Second pass: for launcher-based IDEs where the HWND hasn't been captured yet,
        // match by checking if the window title contains the folder name.
        // This handles both cases: Pid=0 (launcher already exited) and Pid>0 (launcher
        // still exiting while the host creates the window under a different PID).
        var title = WindowFocusService.GetWindowTitle(hwnd);
        if (!string.IsNullOrEmpty(title))
        {
            foreach (var kvp in this._trackedProcesses)
            {
                foreach (var proc in kvp.Value)
                {
                    if (proc.Hwnd == IntPtr.Zero && proc.FolderPath != null)
                    {
                        var folderName = Path.GetFileName(proc.FolderPath.TrimEnd('\\'));
                        if (!string.IsNullOrEmpty(folderName)
                            && title.Contains(folderName, StringComparison.OrdinalIgnoreCase))
                        {
                            proc.Hwnd = hwnd;
                            proc.HwndEverCaptured = true;
                            return kvp.Key;
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Called when a window is destroyed. Removes the HWND from all tracking collections.
    /// </summary>
    /// <param name="hwnd">The destroyed window handle.</param>
    internal HashSet<string> OnWindowDestroyed(IntPtr hwnd)
    {
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in this._activeTrackedWindows)
        {
            int before = kvp.Value.Count;
            kvp.Value.RemoveAll(t => t.Hwnd == hwnd);
            if (kvp.Value.Count < before)
            {
                affected.Add(kvp.Key);
            }
        }

        foreach (var kvp in this._trackedProcesses)
        {
            for (int i = kvp.Value.Count - 1; i >= 0; i--)
            {
                var proc = kvp.Value[i];
                if (proc.Hwnd != hwnd)
                {
                    continue;
                }

                // HWND destroyed — try to recapture from the same PID
                if (proc.Pid > 0)
                {
                    bool stillAlive = false;
                    try { stillAlive = !Process.GetProcessById(proc.Pid).HasExited; }
                    catch { }

                    if (stillAlive)
                    {
                        var newHwnd = WindowFocusService.FindWindowHandleByPid(proc.Pid);
                        if (newHwnd != IntPtr.Zero && newHwnd != hwnd && !this.IsHwndTracked(newHwnd))
                        {
                            proc.Hwnd = newHwnd;
                            affected.Add(kvp.Key);
                            continue;
                        }

                        bool sharedPid = false;
                        foreach (var other in this._trackedProcesses)
                        {
                            if (string.Equals(other.Key, kvp.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (other.Value.Any(p => p.Pid == proc.Pid || (p.Hwnd != IntPtr.Zero
                                && WindowFocusService.GetWindowProcessId(p.Hwnd) == proc.Pid)))
                            {
                                sharedPid = true;
                                break;
                            }
                        }

                        if (sharedPid)
                        {
                            kvp.Value.RemoveAt(i);
                            affected.Add(kvp.Key);
                            continue;
                        }

                        proc.Hwnd = IntPtr.Zero;
                        affected.Add(kvp.Key);
                        continue;
                    }
                }

                // PID dead and HWND destroyed — the IDE window is genuinely closed.
                // Remove the entry (don't keep it alive via FolderPath — that's only for
                // entries that never had an HWND captured).
                kvp.Value.RemoveAt(i);
                affected.Add(kvp.Key);
            }
        }

        foreach (var kvp in this._explorerWindows)
        {
            int before = kvp.Value.Count;
            kvp.Value.RemoveAll(e => e.Hwnd == hwnd);
            if (kvp.Value.Count < before)
            {
                affected.Add(kvp.Key);
            }
        }

        var emptyExplorers = this._explorerWindows.Where(kvp => kvp.Value.Count == 0).Select(kvp => kvp.Key).ToList();
        foreach (var id in emptyExplorers)
        {
            this._explorerWindows.Remove(id);
        }

        var deadEdge = this._edgeWorkspaces.Where(kvp => kvp.Value.CachedHwnd == hwnd).Select(kvp => kvp.Key).ToList();
        foreach (var id in deadEdge)
        {
            affected.Add(id);
            this._edgeWorkspaces.Remove(id);
            Program.Logger.LogInformation("[EdgeDestroyed] Removed Edge workspace {SessionId} (HWND={Hwnd})", id, hwnd);
        }

        var deadTeams = this._teamsWindows.Where(kvp => kvp.Value.CachedHwnd == hwnd).Select(kvp => kvp.Key).ToList();
        foreach (var id in deadTeams)
        {
            affected.Add(id);
            if (this._teamsWindows.TryGetValue(id, out var tw))
            {
                tw.Release();
            }

            this._teamsWindows.Remove(id);
            this.OnTeamsWindowClosed?.Invoke(id);
        }

        return affected;
    }

    /// <summary>
    /// Returns true if the given HWND is already tracked by any session in <see cref="_trackedProcesses"/>.
    /// </summary>
    private bool IsHwndTracked(IntPtr hwnd)
    {
        foreach (var kvp in this._trackedProcesses)
        {
            foreach (var proc in kvp.Value)
            {
                if (proc.Hwnd == hwnd)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Called when a tracked process exits. Removes all entries from <see cref="_trackedProcesses"/>
    /// where the PID matches.
    /// </summary>
    /// <param name="pid">The process ID that exited.</param>
    internal HashSet<string> OnProcessExited(int pid)
    {
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in this._trackedProcesses)
        {
            int before = kvp.Value.Count;

            // Entries with FolderPath are IDE launchers — don't remove them,
            // just clear the PID so FullRefresh can recapture via title fallback.
            foreach (var proc in kvp.Value)
            {
                if (proc.Pid == pid && proc.FolderPath != null)
                {
                    proc.Pid = 0;
                    affected.Add(kvp.Key);
                }
            }

            kvp.Value.RemoveAll(p => p.Pid == pid && p.FolderPath == null);
            if (kvp.Value.Count < before)
            {
                affected.Add(kvp.Key);
            }
        }

        foreach (var sessionId in this._copilotHosts
            .Where(kvp => kvp.Value.CopilotPid == pid)
            .Select(kvp => kvp.Key)
            .ToList())
        {
            this.RemoveCopilotHost(sessionId);
            affected.Add(sessionId);
        }

        return affected;
    }

    /// <summary>
    /// Tracks a launched process (IDE or terminal) for the given session.
    /// </summary>
    internal void TrackProcess(string sessionId, ActiveProcess process)
    {
        if (!this._trackedProcesses.TryGetValue(sessionId, out List<ActiveProcess>? value))
        {
            value = [];
            this._trackedProcesses[sessionId] = value;
        }

        value.Add(process);
    }

    /// <summary>
    /// Detaches a window from a session by removing it from <see cref="_trackedProcesses"/>.
    /// </summary>
    internal void DetachWindow(string sessionId, IntPtr hwnd)
    {
        if (this._trackedProcesses.TryGetValue(sessionId, out var procs))
        {
            procs.RemoveAll(p => p.Hwnd == hwnd);
        }
    }

    /// <summary>
    /// Tracks an Explorer window HWND for a session by matching the folder path
    /// via Shell COM ShellWindows. Explorer.exe is single-instance so PID-based
    /// lookup doesn't work — the spawned process exits immediately.
    /// </summary>
    internal void TrackExplorerWindow(string sessionId, string folderPath, string label = "Explorer")
    {
        var hwnd = FindExplorerByPath(folderPath);
        if (hwnd != IntPtr.Zero)
        {
            if (!this._explorerWindows.TryGetValue(sessionId, out List<(string Label, nint Hwnd)>? list))
            {
                list = [];
                this._explorerWindows[sessionId] = list;
            }

            var idx = list.FindIndex(e => string.Equals(e.Label, label, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                list[idx] = (label, hwnd);
            }
            else
            {
                list.Add((label, hwnd));
            }
        }
    }

    /// <summary>
    /// Finds an open Explorer window whose location matches the given folder path
    /// using Shell COM ShellWindows (CLSID 9BA05972-F6A8-11CF-A442-00A0C90A8F39).
    /// </summary>
    private static IntPtr FindExplorerByPath(string targetPath)
    {
        try
        {
            targetPath = Path.GetFullPath(targetPath).TrimEnd('\\');
            var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (shellWindowsType == null)
            {
                return IntPtr.Zero;
            }

            dynamic? shellWindows = Activator.CreateInstance(shellWindowsType);
            if (shellWindows == null)
            {
                return IntPtr.Zero;
            }

            int count = (int)shellWindows.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic? window = shellWindows.Item(i);
                    if (window == null)
                    {
                        continue;
                    }

                    string? url = window.LocationURL?.ToString();
                    if (url != null && url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = new Uri(url).LocalPath.TrimEnd('\\');
                        if (string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return checked((IntPtr)(long)window.HWND);
                        }
                    }
                }
                catch
                {
                    // Skip individual window errors
                }
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Shell COM explorer lookup failed: {Error}", ex.Message);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Checks if an IDE with the given name is already tracked for the session.
    /// If found and still alive, focuses it and returns true (skip launching a new instance).
    /// </summary>
    internal bool TryFocusExistingIde(string sessionId, string ideName)
    {
        if (!this._trackedProcesses.TryGetValue(sessionId, out var procs))
        {
            return false;
        }

        foreach (var proc in procs)
        {
            if (!string.Equals(proc.Name, ideName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (proc.Hwnd != IntPtr.Zero && this._isWindowAlive(proc.Hwnd))
            {
                WindowFocusService.TryFocusWindowHandle(proc.Hwnd);
                return true;
            }

            if (proc.Pid > 0)
            {
                try
                {
                    if (!Process.GetProcessById(proc.Pid).HasExited)
                    {
                        WindowFocusService.TryFocusProcessWindow(proc.Pid);
                        return true;
                    }
                }
                catch (Exception ex) { Program.Logger.LogDebug("Process not found: {Error}", ex.Message); }
            }
        }

        return false;
    }

    /// <summary>
    /// Tracks an Edge workspace for the given session.
    /// </summary>
    internal void TrackEdge(string sessionId, EdgeWorkspaceService workspace)
    {
        this._edgeWorkspaces[sessionId] = workspace;
    }

    /// <summary>
    /// Removes an Edge workspace for the given session.
    /// </summary>
    internal void RemoveEdge(string sessionId)
    {
        this._edgeWorkspaces.Remove(sessionId);
    }

    /// <summary>
    /// Returns true if the given session has an associated Edge workspace.
    /// </summary>
    internal bool HasEdgeWorkspace(string sessionId)
    {
        return this._edgeWorkspaces.ContainsKey(sessionId);
    }

    /// <summary>
    /// Tries to get the Edge workspace for the given session.
    /// </summary>
    internal bool TryGetEdge(string sessionId, [NotNullWhen(true)] out EdgeWorkspaceService? workspace)
    {
        return this._edgeWorkspaces.TryGetValue(sessionId, out workspace);
    }

    /// <summary>
    /// Tracks a Teams window for the given session.
    /// </summary>
    internal void TrackTeams(string sessionId, TeamsWindowService teamsWindow)
    {
        this._teamsWindows[sessionId] = teamsWindow;
    }

    /// <summary>
    /// Removes a Teams window for the given session.
    /// </summary>
    internal void RemoveTeams(string sessionId)
    {
        if (this._teamsWindows.TryGetValue(sessionId, out var tw))
        {
            tw.Release();
        }

        this._teamsWindows.Remove(sessionId);
    }

    /// <summary>
    /// Returns true if the given session has an associated Teams window.
    /// </summary>
    internal bool HasTeamsWindow(string sessionId)
    {
        return this._teamsWindows.ContainsKey(sessionId);
    }

    /// <summary>
    /// Tries to get the Teams window for the given session.
    /// </summary>
    internal bool TryGetTeams(string sessionId, [NotNullWhen(true)] out TeamsWindowService? teamsWindow)
    {
        return this._teamsWindows.TryGetValue(sessionId, out teamsWindow);
    }

    /// <summary>
    /// Scans all Edge windows for session tabs and registers any newly found workspaces.
    /// Must be called on the UI (STA) thread since it uses UI Automation.
    /// </summary>
    /// <returns>True if new Edge workspaces were discovered.</returns>
    internal bool ScanAndTrackEdgeWorkspaces()
    {
        var edgeMatches = EdgeWorkspaceService.ScanEdgeForSessionTabs();
        Program.Logger.LogInformation("[EdgeScan] Found {Count} session Edge window(s)", edgeMatches.Count);
        bool changed = false;
        foreach (var kvp in edgeMatches)
        {
            Program.Logger.LogInformation("[EdgeScan] Session {SessionId} → HWND={Hwnd}", kvp.Key, kvp.Value);
            if (!this._edgeWorkspaces.TryGetValue(kvp.Key, out EdgeWorkspaceService? value))
            {
                var ws = new EdgeWorkspaceService(kvp.Key)
                {
                    CachedHwnd = kvp.Value
                };
                ws.WindowClosed += () => this.OnEdgeWorkspaceClosed?.Invoke(kvp.Key);
                this._edgeWorkspaces[kvp.Key] = ws;
                changed = true;
                Program.Logger.LogInformation("[EdgeScan] Tracked new Edge workspace for {SessionId}", kvp.Key);
            }
            else if (value.CachedHwnd != kvp.Value)
            {
                value.CachedHwnd = kvp.Value;
                Program.Logger.LogInformation("[EdgeScan] Updated HWND for {SessionId}", kvp.Key);
            }
        }

        return changed;
    }

    /// <summary>
    /// Returns all currently tracked Edge workspace services for change detection.
    /// </summary>
    internal IEnumerable<EdgeWorkspaceService> GetTrackedEdgeWorkspaces()
        => this._edgeWorkspaces.Values.ToList();

    private void ReprojectActiveCopilotHosts()
    {
        foreach (var kvp in this._copilotHosts.ToList())
        {
            if (this.IsCopilotHostActive(kvp.Value))
            {
                this.ProjectCopilotHostToActiveWindows(kvp.Key, kvp.Value);
            }
            else
            {
                this.UnprojectCopilotHostFromActiveWindows(kvp.Key, kvp.Value);
            }
        }
    }

    /// <summary>
    /// Projects a CopilotHostInfo entry into _activeTrackedWindows as a "Copilot CLI" entry.
    /// Deduplicates by HWND: if an entry with the same HWND already exists, no-op.
    /// </summary>
    private void ProjectCopilotHostToActiveWindows(string sessionId, CopilotHostInfo info)
    {
        if (!this._activeTrackedWindows.TryGetValue(sessionId, out List<(string Label, string Title, nint Hwnd)>? value))
        {
            value = [];
            this._activeTrackedWindows[sessionId] = value;
        }

        var projectionKey = GetCopilotHostProjectionKey(info);
        if (value.Any(t => t.Hwnd == info.HostHwnd
            && t.Label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.Title, projectionKey, StringComparison.Ordinal)))
        {
            return;
        }

        value.Add(("Copilot CLI", projectionKey, info.HostHwnd));
    }

    /// <summary>
    /// Removes the projected Copilot host entry from _activeTrackedWindows for the session.
    /// </summary>
    private void UnprojectCopilotHostFromActiveWindows(string sessionId, CopilotHostInfo info)
    {
        if (!this._activeTrackedWindows.TryGetValue(sessionId, out List<(string Label, string Title, nint Hwnd)>? value))
        {
            return;
        }

        var projectionKey = GetCopilotHostProjectionKey(info);
        value.RemoveAll(t => t.Hwnd == info.HostHwnd
            && t.Label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.Title, projectionKey, StringComparison.Ordinal));
    }

    private bool IsCopilotHostProjection(string sessionId, (string Label, string Title, IntPtr Hwnd) entry)
    {
        if (!entry.Label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase)
            || !this._copilotHosts.TryGetValue(sessionId, out var hostInfo))
        {
            return false;
        }

        return entry.Hwnd == hostInfo.HostHwnd
            && string.Equals(entry.Title, GetCopilotHostProjectionKey(hostInfo), StringComparison.Ordinal);
    }

    private static string GetCopilotHostProjectionKey(CopilotHostInfo info)
    {
        return IsWindowsTerminalHost(info) && !string.IsNullOrWhiteSpace(info.PaneRuntimeId)
            ? info.PaneRuntimeId
            : string.Empty;
    }

    /// <summary>
    /// Persists the window handle cache to disk. Called once on shutdown.
    /// </summary>
    internal void SaveWindowHandleCache()
    {
        WindowHandleCacheService.Save(Program.WindowHandleCacheFile, this._trackedProcesses, this._explorerWindows, this._edgeWorkspaces, this._teamsWindows, this._copilotHosts);
    }
}
