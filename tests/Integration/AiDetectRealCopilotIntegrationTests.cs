namespace CopilotBooster.IntegrationTests.Integration;

[Trait("Category", "LocalOnly")]
public sealed class AiDetectRealCopilotIntegrationTests(ITestOutputHelper output)
{
    [LocalOnlyFact]
    public void Ai_auto_detect_real_copilot_p_against_fixture_session_returns_valid_response()
    {
        this.RunRealCopilotScenarioAsync().GetAwaiter().GetResult();
    }

    private async Task RunRealCopilotScenarioAsync()
    {
        var fixture = FindFixtureSession();
        if (fixture == null)
        {
            output.WriteLine("No local Copilot session-state fixture with workspace.yaml and non-empty events.jsonl was found.");
            Assert.Skip("No local Copilot session-state fixture with workspace.yaml and non-empty events.jsonl was found.");
        }

        var cwd = ReadCwd(fixture);
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
        {
            output.WriteLine($"Fixture cwd is missing or unavailable: {cwd}");
            Assert.Skip("Fixture cwd is missing or unavailable.");
        }

        var repo = GitService.TryResolveGitHubRepo(cwd);
        if (repo == null)
        {
            output.WriteLine($"Fixture cwd is not a GitHub repository: {cwd}");
            Assert.Skip("Fixture cwd is not a GitHub repository.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"cb-ai-real-copilot-{Guid.NewGuid():N}");
        var sessionId = Guid.NewGuid().ToString();
        var copiedSessionDir = Path.Combine(tempRoot, sessionId);
        var toastMessages = new List<string>();

        try
        {
            CopyDirectory(fixture, copiedSessionDir);
            await File.WriteAllTextAsync(Path.Combine(copiedSessionDir, "workspace.yaml"), $"id: {sessionId}\ncwd: {cwd}\nsummary: real copilot fixture\n", TestContext.Current.CancellationToken).ConfigureAwait(false);

            var settings = LauncherSettings.CreateDefault().AiDetection;
            using var poller = new GitHubPollingService(new GitHubApiService(), () => [sessionId]);
            using var service = new AiDetectionService(
                new GitHubApiService(),
                new ProcessRunner(),
                _ => cwd,
                toastMessages.Add,
                poller,
                tempRoot,
                tempRoot,
                settingsGetter: () => settings);

            output.WriteLine($"Running real copilot -p against fixture {fixture}");
            output.WriteLine($"Resolved repository: {repo.Value.Owner}/{repo.Value.Repo}");

            var detectionTask = service.StartDetectionAsync(sessionId);
            var state = await WaitForTerminalStateAsync(service, sessionId, TimeSpan.FromSeconds(settings.TimeoutSeconds + 30)).ConfigureAwait(false);
            await detectionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

            if (state.Status == DetectionStatus.Error
                && state.FailureClass is AiFailureClass.ProcessFailure or AiFailureClass.ProcessSpawn)
            {
                output.WriteLine($"copilot CLI auth or spawn issue: {state.FailureClass}");
                Assert.Skip("copilot CLI not authenticated on this machine");
            }

            var accepted = state.Status == DetectionStatus.Idle
                || state.Status == DetectionStatus.Undecided
                || (state.Status == DetectionStatus.Error && state.FailureClass == AiFailureClass.NoCandidates);
            Assert.True(accepted, $"Unexpected terminal AI detection state: {state.Status} {state.FailureClass}");

            if (state.Status == DetectionStatus.Idle && GitHubTrackingService.Load(sessionId)?.Items.Count > 0)
            {
                Assert.NotEmpty(toastMessages);
            }
        }
        finally
        {
            DeleteDirectory(tempRoot);
            DeleteDirectory(SessionStateService.GetSessionDir(sessionId));
        }
    }

    private static string? FindFixtureSession()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "session-state");
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateDirectories(root)
            .FirstOrDefault(dir => File.Exists(Path.Combine(dir, "workspace.yaml")) && IsNonEmpty(Path.Combine(dir, "events.jsonl")));
    }

    private static bool IsNonEmpty(string path)
    {
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static string? ReadCwd(string sessionDir)
    {
        var workspace = Path.Combine(sessionDir, "workspace.yaml");
        foreach (var line in File.ReadLines(workspace))
        {
            if (line.StartsWith("cwd:", StringComparison.Ordinal))
            {
                return line[4..].Trim();
            }
        }

        return null;
    }

    private static async Task<DetectionState> WaitForTerminalStateAsync(AiDetectionService service, string sessionId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var state = service.TryGetState(sessionId);
            if (state.Status != DetectionStatus.Running)
            {
                return state;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        Assert.Fail("AI detection did not complete before timeout.");
        return DetectionState.Idle;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            File.Copy(file, Path.Combine(destination, relative), overwrite: true);
        }
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
}
