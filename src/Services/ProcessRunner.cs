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

        process.Start();

        var wasKilled = false;
        using var registration = linkedCts.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    // TODO(slice #20): replace this simple child kill with JobObject-based process tree termination.
                    process.Kill();
                    wasKilled = true;
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Exception ex)
            {
                Program.Logger.LogDebug("Failed to kill process {FileName}: {Error}", fileName, ex.Message);
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
            try
            {
                if (!process.HasExited)
                {
                    // TODO(slice #20): replace this simple child kill with JobObject-based process tree termination.
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var exitCode = process.HasExited ? process.ExitCode : -1;
        return new ProcessResult(exitCode, stdout, stderr, wasKilled);
    }
}
