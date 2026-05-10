using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using CopilotBooster.Models;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Resolves the Copilot Host for a given Copilot CLI process by walking the parent process tree.
/// </summary>
internal sealed record WindowsTerminalHostContext(
    IntPtr HostHwnd,
    int HostPid,
    int PaneRootPid,
    string HostProcessName,
    string HostKindLabel,
    IReadOnlyList<IntPtr> CandidateHostHwnds);

internal sealed class CopilotHostResolver
{
    private readonly IProcessTreeProvider _provider;
    private readonly int _ownPid;

    /// <summary>
    /// Default constructor using Win32ProcessTreeProvider and the current process ID.
    /// </summary>
    internal CopilotHostResolver()
        : this(new Win32ProcessTreeProvider(), Process.GetCurrentProcess().Id)
    {
    }

    /// <summary>
    /// Test seam: inject a fake provider and an "own pid" to skip.
    /// </summary>
    internal CopilotHostResolver(IProcessTreeProvider provider, int ownPid)
    {
        this._provider = provider;
        this._ownPid = ownPid;
    }

    /// <summary>
    /// Three-arg overload retained for unit tests that exercised the multi-wt-window
    /// disambiguation through this resolver. The gateway argument is currently ignored
    /// — disambiguation lives in <see cref="ActiveStatusTracker.ResolveCopilotHost"/>
    /// where session terms are also available — but kept on the surface to avoid
    /// breaking callers that stub it.
    /// </summary>
    internal CopilotHostResolver(IProcessTreeProvider provider, IWindowsTerminalPaneGateway? paneGateway, int ownPid)
        : this(provider, ownPid)
    {
        _ = paneGateway;
    }

    /// <summary>
    /// Walks the parent process tree starting from <paramref name="copilotPid"/> (NOT including copilotPid itself).
    /// Returns <see cref="CopilotHostInfo"/> for the FIRST ancestor that:
    /// <list type="bullet">
    /// <item>is alive (provider returns process name)</item>
    /// <item>is NOT the Booster's own PID</item>
    /// <item>owns a focusable top-level window (provider.GetTopLevelWindow returns non-zero)</item>
    /// <item>is NOT a shell wrapper (PowerShell, Command Prompt, Console) — these are skipped to find the actual terminal host</item>
    /// </list>
    /// If no non-shell ancestor is found, falls back to the first shell wrapper encountered (for standalone pwsh scenarios).
    /// Returns null if walk reaches PID 0, a cycle is detected, or no ancestor qualifies.
    /// Walk safety: cap at 32 ancestors and break on duplicate PIDs (cycle guard).
    /// HostProcessName is exactly what GetProcessName returned (raw, e.g., "WindowsTerminal", "pwsh").
    /// HostKindLabel is <see cref="HostKindClassifier.Classify"/>(<paramref name="HostProcessName"/>).
    /// </summary>
    internal CopilotHostInfo? Resolve(int copilotPid)
    {
        try
        {
            CopilotHostInfo? shellFallback = null;

            foreach (var ancestor in this.EnumerateAncestors(copilotPid))
            {
                if (ancestor.Pid == this._ownPid || ancestor.ProcessName == null)
                {
                    continue;
                }

                IntPtr hwnd = this._provider.GetTopLevelWindow(ancestor.Pid);
                if (hwnd == IntPtr.Zero)
                {
                    continue;
                }

                string hostKindLabel = HostKindClassifier.Classify(ancestor.ProcessName);

                if (IsShellWrapper(hostKindLabel))
                {
                    // Cache as fallback for standalone shell scenarios
                    if (shellFallback == null)
                    {
                        shellFallback = new CopilotHostInfo(hwnd, ancestor.Pid, copilotPid, ancestor.ProcessName, hostKindLabel);
                    }

                    RuntimeDiagnosticLog.Write(
                        "CopilotHostResolver shell-wrapper skipped copilotPid={0} ancestorPid={1} hostKindLabel={2}",
                        copilotPid,
                        ancestor.Pid,
                        hostKindLabel);
                    continue;
                }

                // Found non-shell ancestor with HWND — this is the terminal host
                return new CopilotHostInfo(hwnd, ancestor.Pid, copilotPid, ancestor.ProcessName, hostKindLabel);
            }

            // No non-shell ancestor found — fall back to last shell (standalone pwsh scenario)
            return shellFallback;
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("CopilotHostResolver failed for pid {Pid}: {Error}", copilotPid, ex.Message);
            return null;
        }
    }

    internal WindowsTerminalHostContext? ResolveWindowsTerminalContext(int copilotPid)
    {
        try
        {
            int paneRootPid = copilotPid;
            var ancestors = this.EnumerateAncestors(copilotPid).ToList();
            RuntimeDiagnosticLog.Write(
                "WT parent-chain copilotPid={0} chain={1}",
                copilotPid,
                string.Join(" -> ", ancestors.Select(ancestor => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{ancestor.Pid}:{ancestor.ProcessName ?? "<unknown>"}"))));

            foreach (var ancestor in ancestors)
            {
                if (ancestor.ProcessName == null)
                {
                    paneRootPid = ancestor.Pid;
                    continue;
                }

                if (IsWindowsTerminalProcess(ancestor.ProcessName))
                {
                    var candidates = this._provider.EnumerateTopLevelWindows(ancestor.Pid);
                    if (candidates.Count == 0)
                    {
                        // Older fakes that don't override EnumerateTopLevelWindows: fall back to GetTopLevelWindow.
                        var single = this._provider.GetTopLevelWindow(ancestor.Pid);
                        candidates = single == IntPtr.Zero ? Array.Empty<IntPtr>() : [single];
                    }

                    if (candidates.Count == 0)
                    {
                        RuntimeDiagnosticLog.Write("WT parent-chain copilotPid={0} reached WT pid={1} with no hwnd", copilotPid, ancestor.Pid);
                        return null;
                    }

                    RuntimeDiagnosticLog.Write(
                        "WT context copilotPid={0} wtPid={1} paneRootPid={2} candidateHwnds=[{3}]",
                        copilotPid,
                        ancestor.Pid,
                        paneRootPid,
                        string.Join(",", candidates));
                    return new WindowsTerminalHostContext(
                        candidates[0],
                        ancestor.Pid,
                        paneRootPid,
                        ancestor.ProcessName,
                        HostKindClassifier.Classify(ancestor.ProcessName),
                        candidates);
                }

                paneRootPid = ancestor.Pid;
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Windows Terminal context resolution failed for pid {Pid}: {Error}", copilotPid, ex.Message);
            RuntimeDiagnosticLog.Write("WT context resolution failed copilotPid={0} error={1}", copilotPid, ex.Message);
        }

        return null;
    }

    private IEnumerable<(int Pid, string? ProcessName)> EnumerateAncestors(int copilotPid)
    {
        int current = copilotPid;
        var visited = new HashSet<int>();
        const int MaxDepth = 32;

        for (int depth = 0; depth < MaxDepth; depth++)
        {
            int? parentPid = this._provider.GetParentPid(current);
            if (parentPid is null or 0 || !visited.Add(parentPid.Value))
            {
                yield break;
            }

            current = parentPid.Value;
            yield return (current, this._provider.GetProcessName(current));
        }
    }

    private static bool IsShellWrapper(string hostKindLabel)
    {
        return hostKindLabel is "PowerShell" or "Command Prompt" or "Console";
    }

    private static bool IsWindowsTerminalProcess(string processName)
    {
        return string.Equals(processName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "wt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "wt.exe", StringComparison.OrdinalIgnoreCase);
    }
}
