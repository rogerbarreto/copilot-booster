using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string cwd, int timeoutSeconds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        using var process = new Process();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        var job = Win32JobObject.CreateKillOnCloseJob();
        var jobAssigned = false;
        var wasKilled = false;

        try
        {
            process.Start();
            try
            {
                Win32JobObject.AssignProcess(job, process.Handle);
                jobAssigned = true;
            }
            catch
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }

                throw;
            }

            using var registration = linkedCts.Token.Register(() =>
            {
                try
                {
                    wasKilled = true;
                    TerminateProcessTree(job, jobAssigned, process, fileName);
                }
                catch (InvalidOperationException)
                {
                }
                catch (Exception ex)
                {
                    Program.Logger.LogDebug("Failed to kill process tree {FileName}: {Error}", fileName, ex.Message);
                }
            });

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
                wasKilled = true;
                TerminateProcessTree(job, jobAssigned, process, fileName);

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var exitCode = process.HasExited ? process.ExitCode : -1;
            return new ProcessResult(exitCode, stdout, stderr, wasKilled);
        }
        finally
        {
            Win32JobObject.Close(job);
        }
    }

    private static void TerminateProcessTree(IntPtr job, bool jobAssigned, Process process, string fileName)
    {
        if (jobAssigned)
        {
            try
            {
                Win32JobObject.Terminate(job, 1);
                return;
            }
            catch (Exception ex)
            {
                Program.Logger.LogDebug("Failed to terminate JobObject for {FileName}: {Error}", fileName, ex.Message);
            }
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
