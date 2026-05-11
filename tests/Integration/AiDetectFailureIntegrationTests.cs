using System.ComponentModel;
using CopilotBooster.IntegrationTests.Integration.TestTools;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.IntegrationTests.Integration;

public sealed class AiDetectFailureIntegrationTests
{
    [StaFact]
    [Trait("Category", "Integration")]
    public void Ai_auto_detect_timeout_logs_class_and_does_not_apply_items()
    {
        RunFailureCase(new ProcessResult(-1, "", "", true), null, AiFailureClass.Timeout, LogLevel.Warning);
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public void Ai_auto_detect_process_spawn_logs_class_and_does_not_apply_items()
    {
        RunFailureCase(new ProcessResult(0, "", "", false), new Win32Exception("copilot not found"), AiFailureClass.ProcessSpawn, LogLevel.Error);
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public void Ai_auto_detect_process_failure_logs_class_and_does_not_apply_items()
    {
        RunFailureCase(new ProcessResult(1, "{\"candidates\":[]}", "auth failed", false), null, AiFailureClass.ProcessFailure, LogLevel.Error);
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public void Ai_auto_detect_malformed_json_logs_class_and_does_not_apply_items()
    {
        RunFailureCase(new ProcessResult(0, "```json\n{\"candidates\":[]}\n```", "", false), null, AiFailureClass.MalformedJson, LogLevel.Error);
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public void Ai_auto_detect_schema_violation_logs_class_and_does_not_apply_items()
    {
        RunFailureCase(new ProcessResult(0, "{\"candidates\":[{\"type\":\"pr\",\"number\":42,\"confidence\":1.5,\"reasoning\":\"x\"}]}", "", false), null, AiFailureClass.SchemaViolation, LogLevel.Error);
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public void Ai_auto_detect_no_candidates_logs_class_and_does_not_apply_items()
    {
        RunFailureCase(new ProcessResult(0, "{\"candidates\":[]}", "", false), null, AiFailureClass.NoCandidates, LogLevel.Warning);
    }

    private static void RunFailureCase(ProcessResult result, Exception? exception, AiFailureClass expectedClass, LogLevel expectedLevel)
    {
        using var harness = FailureHarness.Create(result);
        if (exception != null)
        {
            harness.ProcessRunner.ThrowOnNextCall(exception);
        }

        harness.RunAsync().GetAwaiter().GetResult();

        var tracking = GitHubTrackingService.Load(harness.SessionId);
        Assert.True(tracking == null || tracking.Items.Count == 0);
        Assert.True(!File.Exists(harness.TrackingPath) || GitHubTrackingService.Load(harness.SessionId)?.Items.Count == 0);
        Assert.Empty(harness.ToastMessages);
        Assert.Equal(expectedClass, harness.Service.TryGetState(harness.SessionId).FailureClass);
        Assert.Contains(harness.Logger.Entries, entry => entry.Level == expectedLevel && entry.Message.Contains(expectedClass.ToString(), StringComparison.Ordinal));
    }

    private sealed class FailureHarness : IDisposable
    {
        private readonly string _repoRoot;
        private readonly List<NamedSession> _sessions;
        private readonly ActiveStatusTracker _tracker;
        private readonly SessionGridVisuals _visuals;
        private readonly ILogger _previousLogger;

        private FailureHarness(
            string repoRoot,
            string sessionRoot,
            string sessionId,
            DataGridView grid,
            FakeProcessRunner processRunner,
            CapturingLogger logger,
            List<string> toastMessages,
            AiDetectionService service,
            List<NamedSession> sessions,
            ActiveStatusTracker tracker,
            SessionGridVisuals visuals,
            ILogger previousLogger)
        {
            this._repoRoot = repoRoot;
            this.SessionRoot = sessionRoot;
            this.SessionId = sessionId;
            this.Grid = grid;
            this.ProcessRunner = processRunner;
            this.Logger = logger;
            this.ToastMessages = toastMessages;
            this.Service = service;
            this._sessions = sessions;
            this._tracker = tracker;
            this._visuals = visuals;
            this._previousLogger = previousLogger;
            this.TrackingPath = Path.Combine(SessionStateService.GetSessionDir(sessionId), "github-tracking.json");
        }

        internal string SessionRoot { get; }

        internal string SessionId { get; }

        internal string TrackingPath { get; }

        internal DataGridView Grid { get; }

        internal FakeProcessRunner ProcessRunner { get; }

        internal CapturingLogger Logger { get; }

        internal List<string> ToastMessages { get; }

        internal AiDetectionService Service { get; }

        internal static FailureHarness Create(ProcessResult result)
        {
            var repoRoot = FindRepoRoot();
            var sessionRoot = Path.Combine(Path.GetTempPath(), $"cb-ai-failure-e2e-{Guid.NewGuid():N}");
            var sessionId = Guid.NewGuid().ToString();
            var sessionDir = Path.Combine(sessionRoot, sessionId);
            Directory.CreateDirectory(sessionDir);
            File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), $"id: {sessionId}\ncwd: {repoRoot}\nsummary: AI failure fixture\n");
            File.WriteAllText(Path.Combine(sessionDir, "events.jsonl"), "{\"type\":\"user.message\",\"message\":\"please inspect PR #42\"}\n");
            GitHubTrackingService.Save(sessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster" });

            var grid = CreateGrid();
            AddRow(grid, sessionId);
            var api = CreateFakeApi();
            using var poller = new GitHubPollingService(api, () => [sessionId]);
            var tracker = new ActiveStatusTracker();
            var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings())
            {
                GetGitHubValue = BuildGitHubValue
            };
            var sessions = new List<NamedSession>
            {
                new()
                {
                    Id = sessionId,
                    Cwd = repoRoot,
                    Folder = Path.GetFileName(repoRoot),
                    Summary = "AI failure fixture",
                    IsGitRepo = true,
                    LastModified = DateTime.UtcNow
                }
            };
            var processRunner = new FakeProcessRunner(result);
            var toastMessages = new List<string>();
            var logger = new CapturingLogger();
            var previousLogger = Program.Logger;
            Program.Logger = logger;
            var service = new AiDetectionService(api, processRunner, _ => repoRoot, toastMessages.Add, poller, sessionRoot);
            var harness = new FailureHarness(repoRoot, sessionRoot, sessionId, grid, processRunner, logger, toastMessages, service, sessions, tracker, visuals, previousLogger);
            service.DetectionStateChanged += (sid, _, _) =>
            {
                if (sid == sessionId)
                {
                    var snapshot = tracker.IncrementalRefresh(sessions);
                    visuals.UpdateGridIncremental(snapshot);
                    grid.InvalidateCell(grid.Rows[0].Cells["GitHub"]);
                }
            };

            return harness;
        }

        internal async Task RunAsync()
        {
            _ = this._sessions.Count;
            _ = this._tracker;
            _ = this._visuals;
            await this.Service.StartDetectionAsync(this.SessionId).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.Equal(DetectionStatus.Error, this.Service.TryGetState(this.SessionId).Status);
            Assert.Equal(this._repoRoot, Assert.Single(this.ProcessRunner.Calls).Cwd);
        }

        public void Dispose()
        {
            Program.Logger = this._previousLogger;
            this.Service.Dispose();
            this.Grid.Dispose();
            DeleteDirectory(this.SessionRoot);
            DeleteDirectory(SessionStateService.GetSessionDir(this.SessionId));
        }
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
        var rowIndex = grid.Rows.Add("", sessionId, "", "", "", "");
        grid.Rows[rowIndex].Tag = sessionId;
    }

    private static GitHubApiService CreateFakeApi()
    {
        return new GitHubApiService(processRunner: (command, args) => Task.FromResult((1, "", $"Unexpected command: {command} {args}")));
    }

    private static string BuildGitHubValue(string sessionId)
    {
        var data = GitHubTrackingService.Load(sessionId);
        if (data == null || data.Items.Count == 0)
        {
            return "";
        }

        var parts = new List<string>();
        foreach (var item in data.Items)
        {
            var prefix = item.IsPr ? "PR" : "I";
            parts.Add($"{prefix}#{item.Number}");
        }

        return string.Join(" ", parts);
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
}
