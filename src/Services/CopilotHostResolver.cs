using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    string HostKindLabel);

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
    /// Walks the parent process tree starting from <paramref name="copilotPid"/> (NOT including copilotPid itself).
    /// Returns <see cref="CopilotHostInfo"/> for the FIRST ancestor that:
    /// <list type="bullet">
    /// <item>is alive (provider returns process name)</item>
    /// <item>is NOT the Booster's own PID</item>
    /// <item>owns a focusable top-level window (provider.GetTopLevelWindow returns non-zero)</item>
    /// </list>
    /// Returns null if walk reaches PID 0, a cycle is detected, or no ancestor qualifies.
    /// Walk safety: cap at 32 ancestors and break on duplicate PIDs (cycle guard).
    /// HostProcessName is exactly what GetProcessName returned (raw, e.g., "WindowsTerminal", "pwsh").
    /// HostKindLabel is <see cref="HostKindClassifier.Classify"/>(<paramref name="HostProcessName"/>).
    /// </summary>
    internal CopilotHostInfo? Resolve(int copilotPid)
    {
        try
        {
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
                return new CopilotHostInfo(hwnd, ancestor.Pid, copilotPid, ancestor.ProcessName, hostKindLabel);
            }

            return null;
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
            foreach (var ancestor in this.EnumerateAncestors(copilotPid))
            {
                if (ancestor.ProcessName == null)
                {
                    paneRootPid = ancestor.Pid;
                    continue;
                }

                if (IsWindowsTerminalProcess(ancestor.ProcessName))
                {
                    var hwnd = this._provider.GetTopLevelWindow(ancestor.Pid);
                    if (hwnd == IntPtr.Zero)
                    {
                        return null;
                    }

                    return new WindowsTerminalHostContext(
                        hwnd,
                        ancestor.Pid,
                        paneRootPid,
                        ancestor.ProcessName,
                        HostKindClassifier.Classify(ancestor.ProcessName));
                }

                paneRootPid = ancestor.Pid;
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Windows Terminal context resolution failed for pid {Pid}: {Error}", copilotPid, ex.Message);
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

    private static bool IsWindowsTerminalProcess(string processName)
    {
        return string.Equals(processName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "wt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "wt.exe", StringComparison.OrdinalIgnoreCase);
    }
}
