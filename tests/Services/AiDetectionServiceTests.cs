using System.ComponentModel;

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
    public void EvaluateMenuState_PriorTrackingDataExists_ReturnsEnabled()
    {
        GitHubTrackingService.Save(this._sessionId, new GitHubTrackingData { Owner = "A", Repo = "B" });
        using var service = new AiDetectionService(CreateFakeApi(), new ImmediateProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false)), _ => null, _ => { }, null, this._sessionRoot);

        var result = service.EvaluateMenuState(this._sessionId, Path.Combine(this._sessionRoot, "missing"));

        Assert.Equal(AiMenuState.Enabled, result);
    }

    [Fact]
    public void EvaluateMenuState_NoPriorTrackingAndGitHubOrigin_ReturnsEnabled()
    {
        var repoPath = this.CreateGitRepo("origin", "https://github.com/foo/bar.git");
        using var service = new AiDetectionService(CreateFakeApi(), new ImmediateProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false)), _ => repoPath, _ => { }, null, this._sessionRoot);

        var result = service.EvaluateMenuState(this._sessionId, repoPath);

        Assert.Equal(AiMenuState.Enabled, result);
    }

    [Fact]
    public void EvaluateMenuState_NoPriorTrackingAndNoGitRepo_ReturnsNoRepo()
    {
        var folder = Path.Combine(this._sessionRoot, "plain");
        Directory.CreateDirectory(folder);
        using var service = new AiDetectionService(CreateFakeApi(), new ImmediateProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false)), _ => folder, _ => { }, null, this._sessionRoot);

        var result = service.EvaluateMenuState(this._sessionId, folder);

        Assert.Equal(AiMenuState.NoRepo, result);
    }

    [Fact]
    public void EvaluateMenuState_NoPriorTrackingAndGitLabOrigin_ReturnsNonGitHubRemote()
    {
        var repoPath = this.CreateGitRepo("origin", "https://gitlab.com/foo/bar.git");
        using var service = new AiDetectionService(CreateFakeApi(), new ImmediateProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false)), _ => repoPath, _ => { }, null, this._sessionRoot);

        var result = service.EvaluateMenuState(this._sessionId, repoPath);

        Assert.Equal(AiMenuState.NonGitHubRemote, result);
    }

    [Fact]
    public async Task EvaluateMenuState_DetectionRunning_ReturnsDetectionInFlightAsync()
    {
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
        var runner = new BlockingProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false));
        using var service = new AiDetectionService(CreateFakeApi(), runner, _ => repoRoot, _ => { }, null, this._sessionRoot);

        var detectionTask = service.StartDetectionAsync(this._sessionId);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);
        var result = service.EvaluateMenuState(this._sessionId, Path.Combine(this._sessionRoot, "missing"));
        service.CancelDetection(this._sessionId);
        runner.Release();
        await detectionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(AiMenuState.DetectionInFlight, result);
    }

    [Fact]
    public async Task StartDetectionAsync_SuccessfulRun_TransitionsIdleRunningIdleAsync()
    {
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
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

    [Theory]
    [MemberData(nameof(FailureClassificationRows))]
    public async Task StartDetectionAsync_FailureSignal_ClassifiesFailureAsync(int exitCode, string stdout, string stderr, bool wasKilled, object expectedFailureClass)
    {
        var failureClass = expectedFailureClass is string failureClassName
            ? Enum.Parse<AiFailureClass>(failureClassName)
            : Assert.IsType<AiFailureClass>(expectedFailureClass);
        var processResult = new ProcessResult(exitCode, stdout, stderr, wasKilled);
        var service = await this.RunDetectionAsync(new ImmediateProcessRunner(processResult)).ConfigureAwait(false);
        using (service)
        {
            Assert.Equal(DetectionStatus.Idle, service.TryGetState(this._sessionId).Status);
            Assert.Equal(failureClass, service.TryGetState(this._sessionId).FailureClass);
        }
    }

    [Fact]
    public async Task StartDetectionAsync_ProcessRunnerThrows_ClassifiesProcessSpawnAsync()
    {
        var service = await this.RunDetectionAsync(new ThrowingProcessRunner(new Win32Exception("binary missing"))).ConfigureAwait(false);
        using (service)
        {
            Assert.Equal(AiFailureClass.ProcessSpawn, service.TryGetState(this._sessionId).FailureClass);
        }
    }

    [Fact]
    public async Task StartDetectionAsync_UserCancelledKilledProcess_ReturnsIdleWithoutFailureAsync()
    {
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
        var runner = new BlockingProcessRunner(new ProcessResult(-1, "", "", true));
        var service = new AiDetectionService(CreateFakeApi(), runner, _ => repoRoot, _ => { }, null, this._sessionRoot);

        var detectionTask = service.StartDetectionAsync(this._sessionId);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);
        service.CancelDetection(this._sessionId);
        runner.Release();
        await detectionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        using (service)
        {
            Assert.Equal(DetectionStatus.Idle, service.TryGetState(this._sessionId).Status);
            Assert.Null(service.TryGetState(this._sessionId).FailureClass);
        }
    }

    public static IEnumerable<object[]> FailureClassificationRows()
    {
        yield return [-1, "", "", true, AiFailureClass.Timeout.ToString()];
        yield return [1, "{}", "", false, AiFailureClass.ProcessFailure.ToString()];
        yield return [0, "not json", "", false, AiFailureClass.MalformedJson.ToString()];
        yield return [0, "{\"candidates\":[{\"type\":\"bug\",\"number\":1,\"confidence\":0.5,\"reasoning\":\"x\"}]}", "", false, AiFailureClass.SchemaViolation.ToString()];
        yield return [0, "{\"candidates\":[]}", "", false, AiFailureClass.NoCandidates.ToString()];
    }

    private async Task<AiDetectionService> RunDetectionAsync(IProcessRunner runner)
    {
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
        var service = new AiDetectionService(CreateFakeApi(), runner, _ => repoRoot, _ => { }, null, this._sessionRoot);
        await service.StartDetectionAsync(this._sessionId).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);
        return service;
    }

    private async Task WriteSessionAsync(string repoRoot)
    {
        var sessionDir = Path.Combine(this._sessionRoot, this._sessionId);
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "workspace.yaml"), $"id: {this._sessionId}\ncwd: {repoRoot}\nsummary: test\n", TestContext.Current.CancellationToken).ConfigureAwait(false);
        GitHubTrackingService.Save(this._sessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster" });
    }

    private string CreateGitRepo(string remoteName, string remoteUrl)
    {
        var repoPath = Path.Combine(this._sessionRoot, Path.GetRandomFileName());
        Directory.CreateDirectory(repoPath);

        RunGitCmd(repoPath, "init -q");
        RunGitCmd(repoPath, $"remote add {remoteName} {remoteUrl}");

        return repoPath;
    }

    private static void RunGitCmd(string workDir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(10_000);
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

    private sealed class ImmediateProcessRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        internal ImmediateProcessRunner(ProcessResult result)
        {
            this._result = result;
        }

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string cwd, int timeoutSeconds, CancellationToken cancellationToken)
        {
            return Task.FromResult(this._result);
        }
    }

    private sealed class ThrowingProcessRunner : IProcessRunner
    {
        private readonly Exception _exception;

        internal ThrowingProcessRunner(Exception exception)
        {
            this._exception = exception;
        }

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string cwd, int timeoutSeconds, CancellationToken cancellationToken)
        {
            throw this._exception;
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
            await this._release.Task.ConfigureAwait(false);
            return this._result;
        }

        internal void Release()
        {
            this._release.TrySetResult();
        }
    }
}
