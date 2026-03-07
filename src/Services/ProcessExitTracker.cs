using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Tracks process lifecycle using <see cref="Process.Exited"/> events,
/// providing instant notification when a watched process terminates.
/// </summary>
internal class ProcessExitTracker : IDisposable
{
    private readonly Dictionary<int, Process> _watchedProcesses = [];
    private readonly object _lock = new();

    /// <summary>
    /// Fires with the PID when a watched process exits.
    /// </summary>
    public event Action<int>? ProcessExited;

    /// <summary>
    /// Starts watching a process by PID. If the process has already exited
    /// or does not exist, <see cref="ProcessExited"/> fires immediately.
    /// </summary>
    /// <param name="pid">The process ID to watch.</param>
    public void Watch(int pid)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            Program.Logger.LogDebug("Process {Pid} does not exist; firing exit immediately", pid);
            this.ProcessExited?.Invoke(pid);
            return;
        }

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => this.OnProcessExited(pid);

        lock (this._lock)
        {
            this._watchedProcesses[pid] = process;
        }

        // The process may have exited between GetProcessById and attaching the event.
        if (process.HasExited)
        {
            Program.Logger.LogDebug("Process {Pid} already exited; firing exit immediately", pid);
            this.OnProcessExited(pid);
        }
    }

    /// <summary>
    /// Stops watching a specific PID and disposes its <see cref="Process"/> object.
    /// </summary>
    /// <param name="pid">The process ID to stop watching.</param>
    public void Unwatch(int pid)
    {
        Process? process;
        lock (this._lock)
        {
            if (!this._watchedProcesses.Remove(pid, out process))
            {
                return;
            }
        }

        process.Dispose();
        Program.Logger.LogDebug("Unwatched process {Pid}", pid);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (this._lock)
        {
            foreach (var process in this._watchedProcesses.Values)
            {
                process.Dispose();
            }

            this._watchedProcesses.Clear();
        }
    }

    private void OnProcessExited(int pid)
    {
        lock (this._lock)
        {
            if (!this._watchedProcesses.ContainsKey(pid))
            {
                return;
            }
        }

        Program.Logger.LogDebug("Process {Pid} exited", pid);
        this.ProcessExited?.Invoke(pid);
    }
}
