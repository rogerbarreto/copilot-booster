namespace CopilotBooster.IntegrationTests.Integration;

public sealed class AiDetectTreeKillIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessRunner_CancelKillsParentAndSpawnedChildProcessAsync()
    {
        SkipIfTreeKillImplementationNotPresent();

        var repoRoot = FindRepoRoot();
        var beforePowerShell = SnapshotProcessIds("powershell");
        var beforePing = SnapshotProcessIds("PING");
        var cts = new CancellationTokenSource();
        var runner = new ProcessRunner();
        var childPids = new HashSet<int>();
        var parentPids = new HashSet<int>();

        try
        {
            var command = "$p = Start-Process ping -ArgumentList '-n','60','127.0.0.1' -PassThru; Start-Sleep -Seconds 60";
            var runTask = runner.RunAsync(
                "powershell.exe",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
                repoRoot,
                120,
                cts.Token);

            parentPids = await WaitForNewProcessIdsAsync("powershell", beforePowerShell, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            childPids = await WaitForNewProcessIdsAsync("PING", beforePing, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            cts.Cancel();
            var result = await runTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(false);

            Assert.True(result.WasKilled);
            await WaitForProcessesToExitAsync(parentPids.Concat(childPids), TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.All(parentPids, AssertProcessExited);
            Assert.All(childPids, AssertProcessExited);
        }
        finally
        {
            cts.Dispose();
            CleanupProcesses(parentPids.Concat(childPids));
        }
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

    private static HashSet<int> SnapshotProcessIds(string processName)
    {
        return System.Diagnostics.Process.GetProcessesByName(processName)
            .Select(p => p.Id)
            .ToHashSet();
    }

    private static async Task<HashSet<int>> WaitForNewProcessIdsAsync(string processName, HashSet<int> before, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var current = SnapshotProcessIds(processName);
            current.ExceptWith(before);
            if (current.Count > 0)
            {
                return current;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        Assert.Fail($"No new {processName} process appeared before timeout.");
        return [];
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
