namespace CopilotBooster.IntegrationTests.Integration;

public sealed class AiDetectTreeKillIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessRunner_CancelKillsParentAndSpawnedChildProcessAsync()
    {
        SkipIfTreeKillImplementationNotPresent();

        var repoRoot = FindRepoRoot();
        var pidHandshakeFile = Path.Combine(Path.GetTempPath(), $"cb-treekill-{Guid.NewGuid():N}.pids");
        var cts = new CancellationTokenSource();
        var runner = new ProcessRunner();
        var trackedPids = new HashSet<int>();

        try
        {
            // Handshake protocol: parent writes its own PID first, then the spawned child's PID.
            // The test only ever tracks these two specific PIDs, eliminating by-name discovery races.
            // -NoNewWindow keeps ping in the parent's console (and JobObject lineage); $ErrorActionPreference=Stop
            // ensures Start-Process failures abort the script with a non-zero exit so the test fails fast & loud
            // instead of timing out on a partial handshake file.
            var command = $"$ErrorActionPreference = 'Stop'; " +
                $"$pidsFile = '{pidHandshakeFile.Replace("'", "''", StringComparison.Ordinal)}'; " +
                "$PID | Out-File -FilePath $pidsFile -Encoding ASCII; " +
                "$p = Start-Process ping -ArgumentList '-n','60','127.0.0.1' -PassThru -NoNewWindow; " +
                "if ($null -eq $p) { throw 'Start-Process ping returned null' }; " +
                "$p.Id | Out-File -FilePath $pidsFile -Append -Encoding ASCII; " +
                "Start-Sleep -Seconds 60";
            var runTask = runner.RunAsync(
                "powershell.exe",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
                repoRoot,
                120,
                cts.Token);

            trackedPids = await WaitForHandshakePidsAsync(pidHandshakeFile, expectedCount: 2, TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            cts.Cancel();
            var result = await runTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(false);

            Assert.True(result.WasKilled);
            await WaitForProcessesToExitAsync(trackedPids, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.All(trackedPids, AssertProcessExited);
        }
        finally
        {
            cts.Dispose();
            CleanupProcesses(trackedPids);
            try
            {
                if (File.Exists(pidHandshakeFile))
                {
                    File.Delete(pidHandshakeFile);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task<HashSet<int>> WaitForHandshakePidsAsync(string pidFile, int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(pidFile))
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(pidFile, TestContext.Current.CancellationToken).ConfigureAwait(false);
                    var pids = new HashSet<int>();
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.Length > 0 && int.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture, out var pid))
                        {
                            pids.Add(pid);
                        }
                    }

                    if (pids.Count >= expectedCount)
                    {
                        return pids;
                    }
                }
                catch (IOException)
                {
                    // File still being written; retry.
                }
            }

            await Task.Delay(100, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        Assert.Fail($"Handshake file {pidFile} did not contain {expectedCount} PIDs before timeout.");
        return [];
    }

    private static void SkipIfTreeKillImplementationNotPresent()
    {
        var processRunnerPath = Path.Combine(FindRepoRoot(), "src", "Services", "ProcessRunner.cs");
        var source = File.ReadAllText(processRunnerPath);
        if (source.Contains("TODO(slice #20)", StringComparison.Ordinal))
        {
            Assert.Skip("ProcessRunner JobObject tree-kill implementation is not present in this worktree yet.");
        }
    }

    private static async Task WaitForProcessesToExitAsync(IEnumerable<int> processIds, TimeSpan timeout)
    {
        var ids = processIds.ToArray();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ids.All(IsProcessExited))
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
    }

    private static void AssertProcessExited(int processId)
    {
        Assert.True(IsProcessExited(processId), $"Process {processId} should be gone after cancellation.");
    }

    private static bool IsProcessExited(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void CleanupProcesses(IEnumerable<int> processIds)
    {
        foreach (var processId in processIds.Distinct())
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "copilot-booster.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
