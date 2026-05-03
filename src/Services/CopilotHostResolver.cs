using System;
using System.Collections.Generic;
using System.Diagnostics;
using CopilotBooster.Models;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Resolves the Copilot Host for a given Copilot CLI process by walking the parent process tree.
/// </summary>
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
            int current = copilotPid;
            var visited = new HashSet<int>();
            const int MaxDepth = 32;

            for (int depth = 0; depth < MaxDepth; depth++)
            {
                int? parentPid = this._provider.GetParentPid(current);
                if (parentPid is null or 0)
                {
                    return null;
                }

                if (!visited.Add(parentPid.Value))
                {
                    return null;
                }

                current = parentPid.Value;

                if (current == this._ownPid)
                {
                    continue;
                }

                string? processName = this._provider.GetProcessName(current);
                if (processName == null)
                {
                    continue;
                }

                IntPtr hwnd = this._provider.GetTopLevelWindow(current);
                if (hwnd == IntPtr.Zero)
                {
                    continue;
                }

                string hostKindLabel = HostKindClassifier.Classify(processName);
                return new CopilotHostInfo(hwnd, current, copilotPid, processName, hostKindLabel);
            }

            return null;
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("CopilotHostResolver failed for pid {Pid}: {Error}", copilotPid, ex.Message);
            return null;
        }
    }
}
