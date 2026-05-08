namespace CopilotBooster.Tests.Services;

public sealed class AiDetectionServiceTests : IDisposable
{
    private readonly string _sessionRoot = Path.Combine(Path.GetTempPath(), $"cb-ai-unit-{Guid.NewGuid():N}");
    private readonly string _sessionId = Guid.NewGuid().ToString();

    public void Dispose()
    {
        DeleteDirectory(this._sessionRoot);
        DeleteDirectory(SessionStateService.GetSessionDir(this._sessionId));
    }

    [Fact]
    public async Task StartDetectionAsync_SuccessfulRun_TransitionsIdleRunningIdleAsync()
    {
        var repoRoot = FindRepoRoot();
        var sessionDir = Path.Combine(this._sessionRoot, this._sessionId);
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "workspace.yaml"), $"id: {this._sessionId}\ncwd: {repoRoot}\nsummary: test\n", TestContext.Current.CancellationToken).ConfigureAwait(false);
        GitHubTrackingService.Save(this._sessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster" });

        var result = new ProcessResult(0, "{\"candidates\":[{\"type\":\"pr\",\"number\":42,\"confidence\":0.9,\"reasoning\":\"explicitly mentioned in latest user turn\"}]}", "", false);
        var runner = new BlockingProcessRunner(result);
        var api = CreateFakeApi();
        var transitions = new List<(DetectionStatus OldStatus, DetectionStatus NewStatus)>();
        using var service = new AiDetectionService(api, runner, _ => repoRoot, _ => { }, null, this._sessionRoot);
        service.DetectionStateChanged += (sid, oldState, newState) =>
        {
            if (sid == this._sessionId)
            {
                transitions.Add((oldState, newState));
            }
        };

        var detectionTask = service.StartDetectionAsync(this._sessionId);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(DetectionStatus.Running, service.TryGetState(this._sessionId).Status);
        runner.Release();
        await detectionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(DetectionStatus.Idle, service.TryGetState(this._sessionId).Status);
        Assert.Equal([(DetectionStatus.Idle, DetectionStatus.Running), (DetectionStatus.Running, DetectionStatus.Idle)], transitions);
    }

    private static GitHubApiService CreateFakeApi()
    {
        return new GitHubApiService(processRunner: (command, args) =>
        {
            if (command == "gh" && args == "api repos/rogerbarreto/copilot-booster/pulls/42")
            {
                return Task.FromResult((0, "{\"number\":42,\"title\":\"Test PR\",\"state\":\"open\",\"draft\":false,\"merged\":false,\"user\":{\"login\":\"tester\"},\"head\":{\"ref\":\"feature/test\"},\"updated_at\":\"2026-05-08T00:00:00Z\"}", ""));
            }

            return Task.FromResult((1, "", $"Unexpected command: {command} {args}"));
        });
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

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class BlockingProcessRunner : IProcessRunner
    {
        private readonly ProcessResult _result;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal BlockingProcessRunner(ProcessResult result)
        {
            this._result = result;
        }

        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string cwd, int timeoutSeconds, CancellationToken cancellationToken)
        {
            this.Started.TrySetResult();
            await this._release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return this._result;
        }

        internal void Release()
        {
            this._release.TrySetResult();
        }
    }
}
