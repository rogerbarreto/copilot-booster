using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;

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
    public async Task StartDetectionAsync_SettingsDisabled_DoesNotInvokeRunnerAndLeavesIdleAsync()
    {
        var originalLogger = Program.Logger;
        var logger = new CapturingLogger();
        Program.Logger = logger;
        try
        {
            var runner = new RecordingProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false));
            using var service = new AiDetectionService(CreateFakeApi(), runner, _ => null, _ => { }, null, this._sessionRoot, settingsGetter: () => new AiDetectionSettings { Enabled = false });

            await service.StartDetectionAsync(this._sessionId).ConfigureAwait(false);

            Assert.Empty(runner.Calls);
            Assert.Equal(DetectionStatus.Idle, service.TryGetState(this._sessionId).Status);
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Program.Logger = originalLogger;
        }
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

    [Fact]
    public async Task StartDetectionAsync_TimeoutSeconds_PassesConfiguredValueAsync()
    {
        var runner = new RecordingProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false));
        var service = await this.RunDetectionWithSettingsAsync(new AiDetectionSettings { TimeoutSeconds = 60 }, runner).ConfigureAwait(false);
        using (service)
        {
            var call = Assert.Single(runner.Calls);
            Assert.Equal(60, call.TimeoutSeconds);
        }
    }

    [Theory]
    [InlineData(5000, 1800)]
    [InlineData(10, 30)]
    public async Task StartDetectionAsync_TimeoutSecondsOutOfRange_ClampsAndLogsWarningAsync(int configuredTimeout, int expectedTimeout)
    {
        var originalLogger = Program.Logger;
        var logger = new CapturingLogger();
        Program.Logger = logger;
        try
        {
            var runner = new RecordingProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false));
            var service = await this.RunDetectionWithSettingsAsync(new AiDetectionSettings { TimeoutSeconds = configuredTimeout }, runner).ConfigureAwait(false);
            using (service)
            {
                var call = Assert.Single(runner.Calls);
                Assert.Equal(expectedTimeout, call.TimeoutSeconds);
                Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("clamped", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            Program.Logger = originalLogger;
        }
    }

    [Fact]
    public async Task StartDetectionAsync_ConfidenceThreshold_FiltersAutoApplyAsync()
    {
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
        var runner = new RecordingProcessRunner(new ProcessResult(0, "{\"candidates\":[{\"type\":\"pr\",\"number\":42,\"confidence\":0.7,\"reasoning\":\"maybe\"}]}", "", false));
        using var service = new AiDetectionService(CreateFakeApi(), runner, _ => repoRoot, _ => { }, null, this._sessionRoot, settingsGetter: () => new AiDetectionSettings { ConfidenceThreshold = 0.8m });

        await service.StartDetectionAsync(this._sessionId).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.Empty(GitHubTrackingService.Load(this._sessionId)!.Items);

        runner.Result = new ProcessResult(0, "{\"candidates\":[{\"type\":\"pr\",\"number\":42,\"confidence\":0.85,\"reasoning\":\"strong\"}]}", "", false);
        await service.StartDetectionAsync(this._sessionId).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        var item = Assert.Single(GitHubTrackingService.Load(this._sessionId)!.Items);
        Assert.Equal(42, item.Number);
    }

    [Fact]
    public async Task StartDetectionAsync_DefaultSettings_PassesLocatorResolvedExecutableAsync()
    {
        var runner = new RecordingProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false));
        var service = await this.RunDetectionWithSettingsAsync(new AiDetectionSettings(), runner).ConfigureAwait(false);
        using (service)
        {
            var call = Assert.Single(runner.Calls);
            Assert.Equal(CopilotLocator.FindCopilotExe(), call.FileName);
        }
    }

    [Fact]
    public async Task StartDetectionAsync_CustomSettings_PassesLocatorResolvedExecutableAsync()
    {
        var runner = new RecordingProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false));
        var service = await this.RunDetectionWithSettingsAsync(new AiDetectionSettings { TimeoutSeconds = 60 }, runner).ConfigureAwait(false);
        using (service)
        {
            var call = Assert.Single(runner.Calls);
            Assert.Equal(CopilotLocator.FindCopilotExe(), call.FileName);
        }
    }

    [Fact]
    public async Task StartDetectionAsync_ModelConfigured_AppendsModelFlagAsync()
    {
        var runner = new RecordingProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false));
        var service = await this.RunDetectionWithSettingsAsync(new AiDetectionSettings { Model = "gpt-5.2" }, runner).ConfigureAwait(false);
        using (service)
        {
            var call = Assert.Single(runner.Calls);
            var modelIndex = Array.IndexOf(call.Args, "--model");
            Assert.True(modelIndex >= 0);
            Assert.Equal("gpt-5.2", call.Args[modelIndex + 1]);
        }
    }

    [Fact]
    public async Task StartDetectionAsync_ModelEmpty_OmitsModelFlagAsync()
    {
        var runner = new RecordingProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false));
        var service = await this.RunDetectionWithSettingsAsync(new AiDetectionSettings { Model = "" }, runner).ConfigureAwait(false);
        using (service)
        {
            var call = Assert.Single(runner.Calls);
            Assert.DoesNotContain("--model", call.Args);
        }
    }

    [Fact]
    public async Task StartDetectionAsync_SettingsChangedInFlight_UsesInvocationSnapshotAsync()
    {
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(this._sessionId, repoRoot).ConfigureAwait(false);
        var secondSessionId = Guid.NewGuid().ToString();
        await this.WriteSessionAsync(secondSessionId, repoRoot).ConfigureAwait(false);
        var settings = new AiDetectionSettings { TimeoutSeconds = 300 };
        var runner = new ControlledProcessRunner();
        using var service = new AiDetectionService(CreateFakeApi(), runner, _ => repoRoot, _ => { }, null, this._sessionRoot, settingsGetter: () => settings);

        var firstTask = service.StartDetectionAsync(this._sessionId);
        var firstCall = await runner.WaitForCallAsync(0).ConfigureAwait(false);
        settings = new AiDetectionSettings { TimeoutSeconds = 60 };

        Assert.Equal(300, firstCall.TimeoutSeconds);
        firstCall.Complete(new ProcessResult(0, "{\"candidates\":[]}", "", false));
        await firstTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        var secondTask = service.StartDetectionAsync(secondSessionId);
        var secondCall = await runner.WaitForCallAsync(1).ConfigureAwait(false);
        Assert.Equal(60, secondCall.TimeoutSeconds);
        secondCall.Complete(new ProcessResult(0, "{\"candidates\":[]}", "", false));
        await secondTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    [Fact]
    public void EvaluateMenuState_SettingsDisabled_ReturnsFeatureDisabled()
    {
        using var service = new AiDetectionService(CreateFakeApi(), new ImmediateProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false)), _ => null, _ => { }, null, this._sessionRoot, settingsGetter: () => new AiDetectionSettings { Enabled = false });

        var result = service.EvaluateMenuState(this._sessionId, null);

        Assert.Equal(AiMenuState.FeatureDisabled, result);
    }

    [Fact]
    public void EvaluateMenuState_ProbeUnavailable_ReturnsCopilotUnavailable()
    {
        using var service = new AiDetectionService(CreateFakeApi(), new ImmediateProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false)), _ => null, _ => { }, null, this._sessionRoot, settingsGetter: () => new AiDetectionSettings { Enabled = true }, copilotProbe: new FakeCopilotProbe(false));

        var result = service.EvaluateMenuState(this._sessionId, null);

        Assert.Equal(AiMenuState.CopilotUnavailable, result);
    }

    /// <summary>
    /// Regression test for bug where CopilotProbe.IsCopilotAvailable returns false
    /// even when copilot.exe is installed and working, causing the AI detection menu
    /// to be permanently greyed out with "Copilot CLI not found" tooltip.
    /// 
    /// ROOT CAUSE: ProbeVersion uses Process.Start with RedirectStandardOutput=true,
    /// then WaitForExit(5000). With WinGet-installed copilot.exe, the process prints
    /// the version but never exits within 5 seconds because a background subprocess
    /// inherits the stdout handle. The probe kills the process and returns false.
    /// 
    /// EXPECTED: After Trinity's fix, when the locator returns a valid existing path,
    /// IsCopilotAvailable should return true, and EvaluateMenuState should NOT return
    /// CopilotUnavailable for sessions with valid GitHub repos.
    /// 
    /// This test FAILS today (probe returns false → menu shows CopilotUnavailable).
    /// After Trinity's fix, it should PASS (probe returns true → menu enabled).
    /// </summary>
    [Fact]
    public void EvaluateMenuState_ProbeReturnsTrue_DoesNotReturnCopilotUnavailable()
    {
        // Arrange: Probe says available, session has prior tracking (enabled scenario)
        GitHubTrackingService.Save(this._sessionId, new GitHubTrackingData { Owner = "foo", Repo = "bar" });
        using var service = new AiDetectionService(
            CreateFakeApi(),
            new ImmediateProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false)),
            _ => null,
            _ => { },
            null,
            this._sessionRoot,
            settingsGetter: () => new AiDetectionSettings { Enabled = true },
            copilotProbe: new FakeCopilotProbe(true));

        // Act
        var result = service.EvaluateMenuState(this._sessionId, null);

        // Assert: Should NOT be CopilotUnavailable when probe returns true
        Assert.NotEqual(AiMenuState.CopilotUnavailable, result);
        // Should be Enabled (no repo issues, probe available, not running)
        Assert.Equal(AiMenuState.Enabled, result);
    }

    /// <summary>
    /// Documents the inverse: when the probe correctly returns false for a missing
    /// copilot.exe, EvaluateMenuState should still return CopilotUnavailable.
    /// This test ensures Trinity's fix doesn't break the legitimate unavailable case.
    /// </summary>
    [Fact]
    public void EvaluateMenuState_ProbeReturnsFalse_ReturnsCopilotUnavailable()
    {
        using var service = new AiDetectionService(
            CreateFakeApi(),
            new ImmediateProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false)),
            _ => null,
            _ => { },
            null,
            this._sessionRoot,
            settingsGetter: () => new AiDetectionSettings { Enabled = true },
            copilotProbe: new FakeCopilotProbe(false));

        // Act
        var result = service.EvaluateMenuState(this._sessionId, null);

        // Assert: Should be CopilotUnavailable when probe legitimately returns false
        Assert.Equal(AiMenuState.CopilotUnavailable, result);
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
            Assert.Equal(DetectionStatus.Error, service.TryGetState(this._sessionId).Status);
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

    [Fact]
    public async Task CancelDetection_RunningDetectionCancelsTokenAndReturnsIdleWithoutFailureAsync()
    {
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
        var runner = new ControlledProcessRunner();
        var transitions = new List<(DetectionStatus OldStatus, DetectionStatus NewStatus)>();
        using var service = new AiDetectionService(CreateFakeApi(), runner, _ => repoRoot, _ => { }, null, this._sessionRoot);
        service.DetectionStateChanged += (sid, oldState, newState) =>
        {
            if (sid == this._sessionId)
            {
                transitions.Add((oldState, newState));
            }
        };

        var detectionTask = service.StartDetectionAsync(this._sessionId);
        var call = await runner.WaitForCallAsync(0).ConfigureAwait(false);

        Assert.Equal(DetectionStatus.Running, service.TryGetState(this._sessionId).Status);
        service.CancelDetection(this._sessionId);
        Assert.True(call.CancellationToken.IsCancellationRequested);

        call.Complete(new ProcessResult(-1, "", "", true));
        await detectionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(DetectionStatus.Idle, service.TryGetState(this._sessionId).Status);
        Assert.Null(service.TryGetState(this._sessionId).FailureClass);
        Assert.Equal([(DetectionStatus.Idle, DetectionStatus.Running), (DetectionStatus.Running, DetectionStatus.Idle)], transitions);
    }

    [Fact]
    public async Task Dispose_RunningDetectionsCancelsAllTokensAndReturnsSessionsToIdleAsync()
    {
        var repoRoot = FindRepoRoot();
        var sessionIds = new[] { this._sessionId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
        foreach (var sessionId in sessionIds)
        {
            await this.WriteSessionAsync(sessionId, repoRoot).ConfigureAwait(false);
        }

        var runner = new ControlledProcessRunner();
        var service = new AiDetectionService(CreateFakeApi(), runner, _ => repoRoot, _ => { }, null, this._sessionRoot);
        var detectionTasks = sessionIds.Select(service.StartDetectionAsync).ToArray();
        await runner.WaitForCallCountAsync(sessionIds.Length).ConfigureAwait(false);

        service.Dispose();

        Assert.All(runner.Calls, call => Assert.True(call.CancellationToken.IsCancellationRequested));
        foreach (var call in runner.Calls)
        {
            call.Complete(new ProcessResult(-1, "", "", true));
        }

        await Task.WhenAll(detectionTasks).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.All(sessionIds, sessionId => Assert.Equal(DetectionStatus.Idle, service.TryGetState(sessionId).Status));
        Assert.All(sessionIds, sessionId => Assert.Null(service.TryGetState(sessionId).FailureClass));
    }

    [Fact]
    public async Task StartDetectionAsync_BelowThresholdCandidate_TransitionsToUndecidedLowConfidenceAsync()
    {
        var toasts = new List<string>();
        var transitions = new List<(DetectionStatus OldStatus, DetectionStatus NewStatus)>();
        using var service = await this.RunDetectionAsync(new ProcessResult(0, CandidatesJson(("pr", 42, 0.3, "weak match")), "", false), toasts, transitions).ConfigureAwait(false);

        var state = service.TryGetState(this._sessionId);
        Assert.Equal(DetectionStatus.Undecided, state.Status);
        Assert.Equal(UndecidedReason.LowConfidence, state.UndecidedReason);
        var candidate = Assert.Single(state.TopCandidates!);
        Assert.Equal(42, candidate.Number);
        Assert.Empty(GitHubTrackingService.Load(this._sessionId)!.Items);
        Assert.Empty(toasts);
        Assert.Equal([(DetectionStatus.Idle, DetectionStatus.Running), (DetectionStatus.Running, DetectionStatus.Undecided)], transitions);
    }

    [Fact]
    public async Task StartDetectionAsync_MultipleBelowThresholdCandidates_RetainsTopThreeByConfidenceAsync()
    {
        using var service = await this.RunDetectionAsync(new ProcessResult(0, CandidatesJson(
            ("pr", 1, 0.1, "one"),
            ("pr", 2, 0.4, "two"),
            ("pr", 3, 0.3, "three"),
            ("pr", 4, 0.2, "four"),
            ("pr", 5, 0.45, "five")), "", false)).ConfigureAwait(false);

        var state = service.TryGetState(this._sessionId);
        Assert.Equal(DetectionStatus.Undecided, state.Status);
        Assert.Equal(UndecidedReason.LowConfidence, state.UndecidedReason);
        Assert.Equal([0.45, 0.4, 0.3], state.TopCandidates!.Select(c => c.Confidence));
    }

    [Fact]
    public async Task StartDetectionAsync_AllAboveThresholdCandidatesAlreadyLinked_TransitionsToUndecidedAllAlreadyLinkedAsync()
    {
        var toasts = new List<string>();
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
        GitHubTrackingService.Save(this._sessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster", Items = [Tracked("pr", 42)] });
        using var service = new AiDetectionService(CreateFakeApi(), new ImmediateProcessRunner(new ProcessResult(0, CandidatesJson(("pr", 42, 0.9, "already linked")), "", false)), _ => repoRoot, toasts.Add, null, this._sessionRoot);

        await service.StartDetectionAsync(this._sessionId).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        var state = service.TryGetState(this._sessionId);
        Assert.Equal(DetectionStatus.Undecided, state.Status);
        Assert.Equal(UndecidedReason.AllAlreadyLinked, state.UndecidedReason);
        Assert.Single(state.TopCandidates!);
        Assert.Single(GitHubTrackingService.Load(this._sessionId)!.Items);
        Assert.Empty(toasts);
    }

    [Fact]
    public async Task StartDetectionAsync_MixedNewAndDuplicateCandidates_AddsNewAndShowsPartialDedupToastAsync()
    {
        var toasts = new List<string>();
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
        GitHubTrackingService.Save(this._sessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster", Items = [Tracked("issue", 99)] });
        using var service = new AiDetectionService(CreateFakeApi(), new ImmediateProcessRunner(new ProcessResult(0, CandidatesJson(("pr", 123, 0.9, "new"), ("issue", 99, 0.85, "duplicate")), "", false)), _ => repoRoot, toasts.Add, null, this._sessionRoot);

        await service.StartDetectionAsync(this._sessionId).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        var items = GitHubTrackingService.Load(this._sessionId)!.Items;
        Assert.Contains(items, item => item.Type == "pr" && item.Number == 123);
        Assert.Single(items, item => item.Type == "issue" && item.Number == 99);
        Assert.Equal(DetectionStatus.Idle, service.TryGetState(this._sessionId).Status);
        Assert.Equal("✅ AI added PR #123 (already linked: Issue #99)", Assert.Single(toasts));
    }

    [Fact]
    public async Task StartDetectionAsync_MultipleNewAndDuplicateCandidates_AddsNewAndShowsPartialDedupToastAsync()
    {
        var toasts = new List<string>();
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
        GitHubTrackingService.Save(this._sessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster", Items = [Tracked("pr", 42), Tracked("issue", 99)] });
        using var service = new AiDetectionService(CreateFakeApi(), new ImmediateProcessRunner(new ProcessResult(0, CandidatesJson(("pr", 123, 0.9, "new pr"), ("pr", 42, 0.85, "dup pr"), ("issue", 456, 0.8, "new issue"), ("issue", 99, 0.75, "dup issue")), "", false)), _ => repoRoot, toasts.Add, null, this._sessionRoot);

        await service.StartDetectionAsync(this._sessionId).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        var items = GitHubTrackingService.Load(this._sessionId)!.Items;
        Assert.Contains(items, item => item.Type == "pr" && item.Number == 123);
        Assert.Contains(items, item => item.Type == "issue" && item.Number == 456);
        Assert.Single(items, item => item.Type == "pr" && item.Number == 42);
        Assert.Single(items, item => item.Type == "issue" && item.Number == 99);
        Assert.Equal("✅ AI added PR #123 + Issue #456 (already linked: PR #42 + Issue #99)", Assert.Single(toasts));
    }

    [Fact]
    public async Task StartDetectionAsync_AllCandidatesNew_KeepsExistingSuccessToastAsync()
    {
        var toasts = new List<string>();
        using var service = await this.RunDetectionAsync(new ProcessResult(0, CandidatesJson(("pr", 1, 0.9, "new pr"), ("issue", 2, 0.85, "new issue")), "", false), toasts).ConfigureAwait(false);

        Assert.Equal(DetectionStatus.Idle, service.TryGetState(this._sessionId).Status);
        Assert.Equal("✅ AI added PR #1 + Issue #2 to session", Assert.Single(toasts));
    }

    [Theory]
    [MemberData(nameof(FailureClassificationRows))]
    public async Task StartDetectionAsync_FailureClass_TransitionsToErrorAsync(int exitCode, string stdout, string stderr, bool wasKilled, object expectedFailureClass)
    {
        var failureClass = expectedFailureClass is string failureClassName
            ? Enum.Parse<AiFailureClass>(failureClassName)
            : Assert.IsType<AiFailureClass>(expectedFailureClass);
        var transitions = new List<(DetectionStatus OldStatus, DetectionStatus NewStatus)>();
        using var service = await this.RunDetectionAsync(new ProcessResult(exitCode, stdout, stderr, wasKilled), null, transitions).ConfigureAwait(false);

        Assert.Equal(DetectionStatus.Error, service.TryGetState(this._sessionId).Status);
        Assert.Equal(failureClass, service.TryGetState(this._sessionId).FailureClass);
        Assert.Equal([(DetectionStatus.Idle, DetectionStatus.Running), (DetectionStatus.Running, DetectionStatus.Error)], transitions);
    }

    [Fact]
    public async Task Reset_UndecidedState_ClearsToIdleAndRaisesEventAsync()
    {
        var transitions = new List<(DetectionStatus OldStatus, DetectionStatus NewStatus)>();
        using var service = await this.RunDetectionAsync(new ProcessResult(0, CandidatesJson(("pr", 42, 0.3, "weak")), "", false), null, transitions).ConfigureAwait(false);
        transitions.Clear();

        service.Reset(this._sessionId);

        Assert.Equal(DetectionStatus.Idle, service.TryGetState(this._sessionId).Status);
        Assert.Equal([(DetectionStatus.Undecided, DetectionStatus.Idle)], transitions);
    }

    [Fact]
    public async Task Reset_ErrorState_ClearsToIdleAndRaisesEventAsync()
    {
        var transitions = new List<(DetectionStatus OldStatus, DetectionStatus NewStatus)>();
        using var service = await this.RunDetectionAsync(new ProcessResult(1, "", "failed", false), null, transitions).ConfigureAwait(false);
        transitions.Clear();

        service.Reset(this._sessionId);

        Assert.Equal(DetectionStatus.Idle, service.TryGetState(this._sessionId).Status);
        Assert.Equal([(DetectionStatus.Error, DetectionStatus.Idle)], transitions);
    }

    [Fact]
    public void Reset_IdleState_DoesNotRaiseEvent()
    {
        var transitions = new List<(DetectionStatus OldStatus, DetectionStatus NewStatus)>();
        using var service = new AiDetectionService(CreateFakeApi(), new ImmediateProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false)), _ => null, _ => { }, null, this._sessionRoot);
        service.DetectionStateChanged += (_, oldStatus, newStatus) => transitions.Add((oldStatus, newStatus));

        service.Reset(this._sessionId);

        Assert.Equal(DetectionStatus.Idle, service.TryGetState(this._sessionId).Status);
        Assert.Empty(transitions);
    }

    [Fact]
    public async Task Reset_RunningState_DoesNotCancelOrRaiseResetEventAsync()
    {
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
        var runner = new ControlledProcessRunner();
        var transitions = new List<(DetectionStatus OldStatus, DetectionStatus NewStatus)>();
        using var service = new AiDetectionService(CreateFakeApi(), runner, _ => repoRoot, _ => { }, null, this._sessionRoot);
        service.DetectionStateChanged += (_, oldStatus, newStatus) => transitions.Add((oldStatus, newStatus));

        var detectionTask = service.StartDetectionAsync(this._sessionId);
        var call = await runner.WaitForCallAsync(0).ConfigureAwait(false);
        service.Reset(this._sessionId);

        Assert.Equal(DetectionStatus.Running, service.TryGetState(this._sessionId).Status);
        Assert.False(call.CancellationToken.IsCancellationRequested);
        Assert.Equal([(DetectionStatus.Idle, DetectionStatus.Running)], transitions);
        service.CancelDetection(this._sessionId);
        call.Complete(new ProcessResult(-1, "", "", true));
        await detectionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    [Fact]
    public async Task ManualAddPrSimulation_ResetsUndecidedStateForSessionAsync()
    {
        using var service = await this.RunDetectionAsync(new ProcessResult(0, CandidatesJson(("pr", 42, 0.3, "weak")), "", false)).ConfigureAwait(false);
        Assert.Equal(DetectionStatus.Undecided, service.TryGetState(this._sessionId).Status);

        GitHubTrackingService.AddItem(this._sessionId, "rogerbarreto", "copilot-booster", Tracked("pr", 42));
        service.Reset(this._sessionId);

        Assert.Equal(DetectionStatus.Idle, service.TryGetState(this._sessionId).Status);
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

    private async Task<AiDetectionService> RunDetectionWithSettingsAsync(AiDetectionSettings settings, IProcessRunner runner)
    {
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
        var service = new AiDetectionService(CreateFakeApi(), runner, _ => repoRoot, _ => { }, null, this._sessionRoot, settingsGetter: () => settings);
        await service.StartDetectionAsync(this._sessionId).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);
        return service;
    }

    private Task<AiDetectionService> RunDetectionAsync(ProcessResult result, List<string>? toasts = null, List<(DetectionStatus OldStatus, DetectionStatus NewStatus)>? transitions = null)
    {
        return this.RunDetectionAsync(new ImmediateProcessRunner(result), toasts, transitions);
    }

    private async Task<AiDetectionService> RunDetectionAsync(IProcessRunner runner, List<string>? toasts, List<(DetectionStatus OldStatus, DetectionStatus NewStatus)>? transitions)
    {
        var repoRoot = FindRepoRoot();
        await this.WriteSessionAsync(repoRoot).ConfigureAwait(false);
        Action<string> toastSink = toasts == null ? _ => { } : toasts.Add;
        var service = new AiDetectionService(CreateFakeApi(), runner, _ => repoRoot, toastSink, null, this._sessionRoot);
        if (transitions != null)
        {
            service.DetectionStateChanged += (sid, oldStatus, newStatus) =>
            {
                if (sid == this._sessionId)
                {
                    transitions.Add((oldStatus, newStatus));
                }
            };
        }

        await service.StartDetectionAsync(this._sessionId).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);
        return service;
    }

    private static GitHubTrackedItem Tracked(string type, int number)
    {
        return new GitHubTrackedItem { Type = type, Number = number, State = "open", Title = $"{type} {number}" };
    }

    private static string CandidatesJson(params (string Type, int Number, double Confidence, string Reasoning)[] candidates)
    {
        return "{\"candidates\":[" + string.Join(",", candidates.Select(candidate => $"{{\"type\":\"{candidate.Type}\",\"number\":{candidate.Number},\"confidence\":{candidate.Confidence.ToString(CultureInfo.InvariantCulture)},\"reasoning\":\"{candidate.Reasoning}\"}}")) + "]}";
    }
    private Task WriteSessionAsync(string repoRoot)
    {
        return this.WriteSessionAsync(this._sessionId, repoRoot);
    }

    private async Task WriteSessionAsync(string sessionId, string repoRoot)
    {
        var sessionDir = Path.Combine(this._sessionRoot, sessionId);
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "workspace.yaml"), $"id: {sessionId}\ncwd: {repoRoot}\nsummary: test\n", TestContext.Current.CancellationToken).ConfigureAwait(false);
        GitHubTrackingService.Save(sessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster" });
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
            const string PullPrefix = "api repos/rogerbarreto/copilot-booster/pulls/";
            const string IssuePrefix = "api repos/rogerbarreto/copilot-booster/issues/";
            if (command == "gh" && args != null && args.StartsWith(PullPrefix, StringComparison.Ordinal))
            {
                var number = int.Parse(args[PullPrefix.Length..], CultureInfo.InvariantCulture);
                return Task.FromResult((0, $"{{\"number\":{number},\"title\":\"Test PR {number}\",\"state\":\"open\",\"draft\":false,\"merged\":false,\"user\":{{\"login\":\"tester\"}},\"head\":{{\"ref\":\"feature/test\"}},\"updated_at\":\"2026-05-08T00:00:00Z\"}}", ""));
            }

            if (command == "gh" && args != null && args.StartsWith(IssuePrefix, StringComparison.Ordinal))
            {
                var number = int.Parse(args[IssuePrefix.Length..], CultureInfo.InvariantCulture);
                return Task.FromResult((0, $"{{\"number\":{number},\"title\":\"Test Issue {number}\",\"state\":\"open\",\"state_reason\":null,\"user\":{{\"login\":\"tester\"}},\"labels\":[],\"updated_at\":\"2026-05-08T00:00:00Z\"}}", ""));
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

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        internal RecordingProcessRunner(ProcessResult result)
        {
            this.Result = result;
        }

        internal ProcessResult Result { get; set; }

        internal List<RecordedProcessCall> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string cwd, int timeoutSeconds, CancellationToken cancellationToken)
        {
            this.Calls.Add(new RecordedProcessCall(fileName, args.ToArray(), cwd, timeoutSeconds, cancellationToken));
            return Task.FromResult(this.Result);
        }
    }

    private sealed record RecordedProcessCall(string FileName, string[] Args, string Cwd, int TimeoutSeconds, CancellationToken CancellationToken);

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

    private sealed class ControlledProcessRunner : IProcessRunner
    {
        private readonly object _gate = new();
        private readonly List<ControlledProcessRunnerCall> _calls = [];

        internal IReadOnlyList<ControlledProcessRunnerCall> Calls
        {
            get
            {
                lock (this._gate)
                {
                    return this._calls.ToArray();
                }
            }
        }

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string cwd, int timeoutSeconds, CancellationToken cancellationToken)
        {
            var call = new ControlledProcessRunnerCall(fileName, args.ToArray(), cwd, timeoutSeconds, cancellationToken);
            lock (this._gate)
            {
                this._calls.Add(call);
            }

            return call.Task;
        }

        internal async Task<ControlledProcessRunnerCall> WaitForCallAsync(int index)
        {
            await this.WaitForCallCountAsync(index + 1).ConfigureAwait(false);
            lock (this._gate)
            {
                return this._calls[index];
            }
        }

        internal async Task WaitForCallCountAsync(int count)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (this._gate)
                {
                    if (this._calls.Count >= count)
                    {
                        return;
                    }
                }

                await Task.Delay(25, TestContext.Current.CancellationToken).ConfigureAwait(false);
            }

            Assert.Fail($"Expected {count} process calls before timeout.");
        }
    }

    private sealed class ControlledProcessRunnerCall
    {
        private readonly TaskCompletionSource<ProcessResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ControlledProcessRunnerCall(string fileName, string[] args, string cwd, int timeoutSeconds, CancellationToken cancellationToken)
        {
            this.FileName = fileName;
            this.Args = args;
            this.Cwd = cwd;
            this.TimeoutSeconds = timeoutSeconds;
            this.CancellationToken = cancellationToken;
        }

        internal string FileName { get; }

        internal string[] Args { get; }

        internal string Cwd { get; }

        internal int TimeoutSeconds { get; }

        internal CancellationToken CancellationToken { get; }

        internal Task<ProcessResult> Task => this._completion.Task;

        internal void Complete(ProcessResult result)
        {
            this._completion.TrySetResult(result);
        }
    }

    private sealed class FakeCopilotProbe : ICopilotProbe
    {
        private readonly bool _available;

        internal FakeCopilotProbe(bool available)
        {
            this._available = available;
        }

        public bool IsCopilotAvailable() => this._available;

        public void InvalidateCache()
        {
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<CapturedLogEntry> _entries = [];

        internal IReadOnlyList<CapturedLogEntry> Entries
        {
            get
            {
                lock (this._entries)
                {
                    return this._entries.ToArray();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (this._entries)
            {
                this._entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed record CapturedLogEntry(LogLevel Level, string Message);

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
