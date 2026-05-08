using CopilotBooster.IntegrationTests.Integration.TestTools;

namespace CopilotBooster.IntegrationTests.Integration;

public sealed class AiDetectIntegrationTests : IDisposable
{
    private readonly string _sessionRoot = Path.Combine(Path.GetTempPath(), $"cb-ai-e2e-{Guid.NewGuid():N}");
    private readonly string _sessionId = Guid.NewGuid().ToString();

    public void Dispose()
    {
        DeleteDirectory(this._sessionRoot);
        DeleteDirectory(SessionStateService.GetSessionDir(this._sessionId));
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public void Ai_auto_detect_happy_path_adds_pr_to_tracking_data_and_renders_in_cell_and_emits_toast()
    {
        this.RunAiAutoDetectHappyPathAsync().GetAwaiter().GetResult();
    }

    private async Task RunAiAutoDetectHappyPathAsync()
    {
        var repoRoot = FindRepoRoot();
        var sessionDir = Path.Combine(this._sessionRoot, this._sessionId);
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "workspace.yaml"), $"id: {this._sessionId}\ncwd: {repoRoot}\nsummary: AI detect fixture\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "events.jsonl"), "{\"type\":\"user.message\",\"message\":\"please inspect PR #42\"}\n{\"type\":\"assistant.message\",\"message\":\"checking rogerbarreto/copilot-booster pull 42\"}\n").ConfigureAwait(false);

        var grid = CreateGrid();
        var api = CreateFakeApi();
        using var poller = new GitHubPollingService(api, () => [this._sessionId]);
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings())
        {
            GetGitHubValue = BuildGitHubValue
        };
        var sessions = new List<NamedSession>
        {
            new()
            {
                Id = this._sessionId,
                Cwd = repoRoot,
                Folder = Path.GetFileName(repoRoot),
                Summary = "AI detect fixture",
                IsGitRepo = true,
                LastModified = DateTime.UtcNow
            }
        };

        AddRow(grid, this._sessionId);
        var stdout = "{\"candidates\":[{\"type\":\"pr\",\"number\":42,\"confidence\":0.9,\"reasoning\":\"explicitly mentioned in latest user turn\"}]}";
        var processRunner = new FakeProcessRunner(new ProcessResult(0, stdout, "", false));
        var toastMessages = new List<string>();
        using var service = new AiDetectionService(api, processRunner, _ => repoRoot, toastMessages.Add, poller, this._sessionRoot);
        service.DetectionStateChanged += (sid, _, _) =>
        {
            if (sid == this._sessionId)
            {
                var snapshot = tracker.IncrementalRefresh(sessions);
                visuals.UpdateGridIncremental(snapshot);
                grid.InvalidateCell(grid.Rows[0].Cells["GitHub"]);
            }
        };

        var detectionTask = service.StartDetectionAsync(this._sessionId);
        await WaitUntilIdleAsync(service, this._sessionId, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await detectionTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        var call = Assert.Single(processRunner.Calls);
        Assert.Equal("copilot", call.FileName);
        Assert.Equal(repoRoot, call.Cwd);
        Assert.Equal(300, call.TimeoutSeconds);
        AssertArgumentValue(call.Args, "-p", prompt =>
        {
            Assert.Contains("rogerbarreto/copilot-booster", prompt);
            Assert.Contains(sessionDir, prompt);
        });
        Assert.Contains("-s", call.Args);
        Assert.Contains("--no-ask-user", call.Args);
        Assert.Contains("--allow-all-tools", call.Args);
        AssertArgumentValue(call.Args, "--add-dir", value => Assert.Equal(sessionDir, value));
        AssertUrlAllowed(call.Args, "github.com");
        AssertUrlAllowed(call.Args, "api.github.com");
        AssertArgumentValue(call.Args, "-C", value => Assert.Equal(repoRoot, value));
        AssertArgumentValue(call.Args, "--log-dir", value => Assert.False(string.IsNullOrWhiteSpace(value)));

        var data = GitHubTrackingService.Load(this._sessionId);
        Assert.NotNull(data);
        var item = Assert.Single(data.Items);
        Assert.Equal("pr", item.Type);
        Assert.Equal(42, item.Number);
        Assert.Equal("PR#42", grid.Rows[0].Cells["GitHub"].Value?.ToString());
        Assert.Equal(["✅ AI added PR #42 to session"], toastMessages);

        var trackingPath = Path.Combine(SessionStateService.GetSessionDir(this._sessionId), "github-tracking.json");
        Assert.True(File.Exists(trackingPath));
        Assert.Contains("\"number\": 42", await File.ReadAllTextAsync(trackingPath).ConfigureAwait(false));
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
        return new GitHubApiService(processRunner: (command, args) =>
        {
            if (command == "gh" && args == "api repos/rogerbarreto/copilot-booster/pulls/42")
            {
                return Task.FromResult((0, "{\"number\":42,\"title\":\"AI detect fixture PR\",\"state\":\"open\",\"draft\":false,\"merged\":false,\"user\":{\"login\":\"tester\"},\"head\":{\"ref\":\"feature/ai-detect\",\"sha\":\"abc123\"},\"updated_at\":\"2026-05-08T00:00:00Z\"}", ""));
            }

            return Task.FromResult((1, "", $"Unexpected command: {command} {args}"));
        });
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

    private static async Task WaitUntilIdleAsync(AiDetectionService service, string sessionId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (service.TryGetState(sessionId).Status == DetectionStatus.Idle)
            {
                return;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        Assert.Fail("AI detection did not return to idle before timeout.");
    }

    private static void AssertArgumentValue(string[] args, string name, Action<string> assertValue)
    {
        var index = Array.IndexOf(args, name);
        Assert.True(index >= 0, $"Missing argument {name}");
        Assert.True(index + 1 < args.Length, $"Missing value for {name}");
        assertValue(args[index + 1]);
    }

    private static void AssertUrlAllowed(string[] args, string url)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--allow-url" && args[i + 1] == url)
            {
                return;
            }
        }

        Assert.Fail($"Missing --allow-url {url}");
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
