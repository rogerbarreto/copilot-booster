using System.Collections.Concurrent;

namespace CopilotBooster.IntegrationTests.Integration;

public sealed class AiDetectConcurrencyIntegrationTests : IDisposable
{
    private readonly string _fixtureRoot = Path.Combine(Path.GetTempPath(), $"cb-ai-concurrency-{Guid.NewGuid():N}");
    private readonly string _sessionRoot;
    private readonly List<string> _sessionIds = [];

    public AiDetectConcurrencyIntegrationTests()
    {
        this._sessionRoot = Path.Combine(this._fixtureRoot, "sessions");
        Directory.CreateDirectory(this._sessionRoot);
    }

    public void Dispose()
    {
        foreach (var sessionId in this._sessionIds)
        {
            DeleteDirectory(SessionStateService.GetSessionDir(sessionId));
        }

        DeleteDirectory(this._fixtureRoot);
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public void Ai_auto_detect_5_sessions_in_parallel_all_run_without_queueing()
    {
        this.RunConcurrencyScenarioAsync().GetAwaiter().GetResult();
    }

    private async Task RunConcurrencyScenarioAsync()
    {
        var repoRoot = FindRepoRoot();
        var grid = CreateGrid();
        using var processRunner = new ConcurrentProcessRunner();
        var api = CreateFakeApi();
        using var poller = new GitHubPollingService(api, () => this._sessionIds);
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings())
        {
            GetGitHubValue = BuildGitHubValue
        };
        try
        {
            for (var index = 0; index < 5; index++)
            {
                var sessionId = this.CreateSession(repoRoot, $"parallel {index}");
                GitHubTrackingService.Save(sessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster" });
                AddRow(grid, sessionId);
            }

            using var service = new AiDetectionService(api, processRunner, _ => repoRoot, _ => { }, poller, this._sessionRoot);
            var tasks = this._sessionIds.Select(service.StartDetectionAsync).ToArray();
            await WaitForAllRunningAsync(service, processRunner, this._sessionIds, TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            for (var index = 0; index < this._sessionIds.Count; index++)
            {
                var sessionId = this._sessionIds[index];
                var prNumber = 200 + index;
                processRunner.Complete(sessionId, new ProcessResult(0, CandidatesJson(prNumber), "", false));
                await WaitForStatusAsync(service, sessionId, DetectionStatus.Idle, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                var tracking = GitHubTrackingService.Load(sessionId);
                Assert.NotNull(tracking);
                Assert.Contains(tracking.Items, item => item.Type == "pr" && item.Number == prNumber);

                foreach (var remainingSessionId in this._sessionIds.Skip(index + 1))
                {
                    Assert.Equal(DetectionStatus.Running, service.TryGetState(remainingSessionId).Status);
                }
            }

            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);
            Assert.Equal(5, processRunner.Calls.Count);
        }
        finally
        {
            visuals.Dispose();
            grid.Dispose();
        }
    }
    private string CreateSession(string cwd, string summary)
    {
        var sessionId = Guid.NewGuid().ToString();
        this._sessionIds.Add(sessionId);
        var sessionDir = Path.Combine(this._sessionRoot, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), $"id: {sessionId}\ncwd: {cwd}\nsummary: {summary}\n");
        File.WriteAllText(Path.Combine(sessionDir, "events.jsonl"), "{\"type\":\"user.message\",\"message\":\"please inspect PR #200\"}\n");
        return sessionId;
    }

    private static LauncherSettings CreateTestSettings()
    {
        var settings = LauncherSettings.CreateDefault();
        settings.SuppressSave = true;
        return settings;
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        grid.Columns.Add("Status", "");
        grid.Columns.Add("Name", "Name");
        grid.Columns.Add("CWD", "CWD");
        grid.Columns.Add("LastModified", "LastModified");
        grid.Columns.Add("Context", "Context");
        grid.Columns.Add("Active", "Active");
        grid.Columns.Add("GitHub", "GitHub");
        return grid;
    }

    private static void AddRow(DataGridView grid, string sessionId)
    {
        var rowIndex = grid.Rows.Add("", sessionId, "", "", "", "", "");
        grid.Rows[rowIndex].Tag = sessionId;
    }

    private static GitHubApiService CreateFakeApi()
    {
        return new GitHubApiService(processRunner: (command, args) =>
        {
            var number = TryParsePullNumber(args);
            if (command == "gh" && number.HasValue)
            {
                return Task.FromResult((0, $"{{\"number\":{number.Value},\"title\":\"Concurrent PR {number.Value}\",\"state\":\"open\",\"draft\":false,\"merged\":false,\"user\":{{\"login\":\"tester\"}},\"head\":{{\"ref\":\"feature/concurrent-{number.Value}\",\"sha\":\"abc{number.Value}\"}},\"updated_at\":\"2026-05-08T00:00:00Z\"}}", ""));
            }

            return Task.FromResult((1, "", $"Unexpected command: {command} {args}"));
        });
    }

    private static int? TryParsePullNumber(string? args)
    {
        const string Prefix = "repos/rogerbarreto/copilot-booster/pulls/";
        var index = (args ?? string.Empty).IndexOf(Prefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = index + Prefix.Length;
        var end = start;
        while (end < args!.Length && char.IsDigit(args[end]))
        {
            end++;
        }

        return int.TryParse(args[start..end], out var number) ? number : null;
    }

    private static string BuildGitHubValue(string sessionId)
    {
        var data = GitHubTrackingService.Load(sessionId);
        if (data == null || data.Items.Count == 0)
        {
            return "";
        }

        return string.Join(" ", data.Items.Select(item => $"{(item.IsPr ? "PR" : "I")}#{item.Number}"));
    }

    private static string CandidatesJson(int number)
    {
        return $"{{\"candidates\":[{{\"type\":\"pr\",\"number\":{number},\"confidence\":0.9,\"reasoning\":\"concurrency fixture\"}}]}}";
    }

    private static async Task WaitForAllRunningAsync(AiDetectionService service, ConcurrentProcessRunner runner, IReadOnlyList<string> sessionIds, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (sessionIds.All(sessionId => service.TryGetState(sessionId).Status == DetectionStatus.Running)
                && sessionIds.All(runner.HasStarted))
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        Assert.Fail("Expected all AI detections to be running concurrently before timeout.");
    }

    private static async Task WaitForStatusAsync(AiDetectionService service, string sessionId, DetectionStatus status, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (service.TryGetState(sessionId).Status == status)
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        Assert.Fail($"Expected session {sessionId} to reach {status} before timeout.");
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

    private sealed class ConcurrentProcessRunner : IProcessRunner, IDisposable
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ProcessResult>> _pending = new(StringComparer.OrdinalIgnoreCase);

        internal ConcurrentBag<string> Calls { get; } = [];

        internal bool HasStarted(string sessionId) => this._pending.ContainsKey(sessionId);

        internal void Complete(string sessionId, ProcessResult result)
        {
            Assert.True(this._pending.TryGetValue(sessionId, out var completion), $"No pending process for {sessionId}.");
            completion.TrySetResult(result);
        }

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string cwd, int timeoutSeconds, CancellationToken ct)
        {
            var sessionId = GetSessionId(args);
            this.Calls.Add(sessionId);
            var completion = new TaskCompletionSource<ProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(this._pending.TryAdd(sessionId, completion), $"Duplicate process for {sessionId}.");
            ct.Register(() => completion.TrySetResult(new ProcessResult(-1, "", "", true)));
            return completion.Task;
        }

        public void Dispose()
        {
            foreach (var completion in this._pending.Values)
            {
                completion.TrySetResult(new ProcessResult(-1, "", "", true));
            }
        }

        private static string GetSessionId(IReadOnlyList<string> args)
        {
            var index = args.ToList().IndexOf("--add-dir");
            Assert.True(index >= 0 && index + 1 < args.Count, "Missing --add-dir argument.");
            return Path.GetFileName(args[index + 1]);
        }
    }
}
