using CopilotBooster.IntegrationTests.Integration.TestTools;

namespace CopilotBooster.IntegrationTests.Integration;

public sealed class AiDetectMenuGatingIntegrationTests : IDisposable
{
    private readonly string _fixtureRoot = Path.Combine(Path.GetTempPath(), $"cb-ai-menu-gating-{Guid.NewGuid():N}");
    private readonly string _sessionRoot;
    private readonly List<string> _sessionIds = [];

    public AiDetectMenuGatingIntegrationTests()
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
    public void Ai_auto_detect_menu_gating_evaluates_repo_preconditions_and_prior_tracking_source()
    {
        var githubUpstream = this.CreateGitRepo("upstream", "https://github.com/upstream/repo.git");
        var githubOrigin = this.CreateGitRepo("origin", "https://github.com/origin/repo.git");
        var gitLabOrigin = this.CreateGitRepo("origin", "https://gitlab.com/gitlab/repo.git");
        var plainFolder = this.CreateFolder("plain");
        var changedCwd = this.CreateGitRepo("origin", "https://github.com/current/repo.git");

        var rows = new[]
        {
            new FixtureRow("upstream", githubUpstream, true, ""),
            new FixtureRow("origin", githubOrigin, true, ""),
            new FixtureRow("gitlab", gitLabOrigin, false, AiDetectionTooltips.NonGitHubRemote),
            new FixtureRow("plain", plainFolder, false, AiDetectionTooltips.NoRepo),
            new FixtureRow("prior", changedCwd, true, "", "prior", "repo")
        };

        var cwdBySession = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sessions = new List<NamedSession>();
        foreach (var row in rows)
        {
            var sessionId = this.CreateSession(row.Name, row.Cwd);
            row.SessionId = sessionId;
            cwdBySession[sessionId] = row.Cwd;
            sessions.Add(new NamedSession
            {
                Id = sessionId,
                Cwd = row.Cwd,
                Folder = Path.GetFileName(row.Cwd),
                Summary = row.Name,
                IsGitRepo = Directory.Exists(Path.Combine(row.Cwd, ".git")),
                LastModified = DateTime.UtcNow
            });

            if (row.PriorOwner != null && row.PriorRepo != null)
            {
                GitHubTrackingService.Save(sessionId, new GitHubTrackingData { Owner = row.PriorOwner, Repo = row.PriorRepo });
            }
        }

        using var panel = new Panel();
        var tracker = new ActiveStatusTracker();
        using var visuals = new VisualsScope(panel, tracker);
        var processRunner = new FakeProcessRunner(new ProcessResult(0, "{\"candidates\":[{\"type\":\"pr\",\"number\":42,\"confidence\":0.9,\"reasoning\":\"mentioned\"}]}", "", false));
        var toastMessages = new List<string>();
        using var poller = new GitHubPollingService(CreateFakeApi(), () => rows.Select(r => r.SessionId!).ToList());
        using var service = new AiDetectionService(CreateFakeApi(), processRunner, sid => cwdBySession[sid], toastMessages.Add, poller, this._sessionRoot);
        visuals.Instance.AiDetectionService = service;
        visuals.Instance.GetSessionPaths = sid => (cwdBySession[sid], SessionService.FindGitRoot(cwdBySession[sid]));
        visuals.Instance.GridVisuals.GetGitHubValue = BuildGitHubValue;

        service.DetectionStateChanged += (sid, _, _) =>
        {
            var snapshot = tracker.IncrementalRefresh(sessions);
            visuals.Instance.GridVisuals.UpdateGridIncremental(snapshot);
            var rowIndex = rows.ToList().FindIndex(row => row.SessionId == sid);
            if (rowIndex >= 0 && visuals.Instance.SessionGrid.Rows.Count > rowIndex)
            {
                visuals.Instance.SessionGrid.InvalidateCell(visuals.Instance.SessionGrid.Rows[rowIndex].Cells["GitHub"]);
            }
        };

        foreach (var row in rows)
        {
            AddRow(visuals.Instance.SessionGrid, row.SessionId!, row.Cwd);
            var menuItem = visuals.Instance.GetEvaluatedAiMenuItem(row.SessionId!, row.Cwd);
            Assert.Equal(row.ExpectedEnabled, menuItem.Enabled);
            Assert.Equal(row.ExpectedTooltip, menuItem.ToolTipText ?? "");
        }

        var priorRow = rows.Single(row => row.Name == "prior");
        service.StartDetectionAsync(priorRow.SessionId!).Wait(TimeSpan.FromSeconds(10));

        var call = Assert.Single(processRunner.Calls);
        AssertArgumentValue(call.Args, "-p", prompt =>
        {
            Assert.Contains("prior/repo", prompt);
            Assert.DoesNotContain("current/repo", prompt);
        });
    }

    private string CreateSession(string name, string cwd)
    {
        var sessionId = Guid.NewGuid().ToString();
        this._sessionIds.Add(sessionId);
        var sessionDir = Path.Combine(this._sessionRoot, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), $"id: {sessionId}\ncwd: {cwd}\nsummary: {name}\n");
        File.WriteAllText(Path.Combine(sessionDir, "events.jsonl"), "{\"type\":\"user.message\",\"message\":\"please inspect PR #42\"}\n");
        return sessionId;
    }

    private string CreateGitRepo(string remoteName, string remoteUrl)
    {
        var repoPath = this.CreateFolder("repo");
        RunGitCmd(repoPath, "init -q");
        RunGitCmd(repoPath, $"remote add {remoteName} {remoteUrl}");
        return repoPath;
    }

    private string CreateFolder(string prefix)
    {
        var path = Path.Combine(this._fixtureRoot, $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static GitHubApiService CreateFakeApi()
    {
        return new GitHubApiService(processRunner: (command, args) =>
        {
            if (command == "gh" && args == "api repos/prior/repo/pulls/42")
            {
                return Task.FromResult((0, "{\"number\":42,\"title\":\"Prior PR\",\"state\":\"open\",\"draft\":false,\"merged\":false,\"user\":{\"login\":\"tester\"},\"head\":{\"ref\":\"feature/prior\"},\"updated_at\":\"2026-05-08T00:00:00Z\"}", ""));
            }

            return Task.FromResult((1, "", $"Unexpected command: {command} {args}"));
        });
    }

    private static LauncherSettings CreateTestSettings()
    {
        var settings = LauncherSettings.CreateDefault();
        settings.SuppressSave = true;
        return settings;
    }

    private static void AddRow(DataGridView grid, string sessionId, string cwd)
    {
        var rowIndex = grid.Rows.Add("", sessionId, cwd, "", "", "", "");
        grid.Rows[rowIndex].Tag = sessionId;
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

    private static void AssertArgumentValue(string[] args, string name, Action<string> assertValue)
    {
        var index = Array.IndexOf(args, name);
        Assert.True(index >= 0, $"Missing argument {name}");
        Assert.True(index + 1 < args.Length, $"Missing value for {name}");
        assertValue(args[index + 1]);
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

    private sealed class VisualsScope : IDisposable
    {
        internal VisualsScope(Control parent, ActiveStatusTracker tracker)
        {
            this.Instance = new ExistingSessionsVisuals(parent, tracker, CreateTestSettings());
        }

        internal ExistingSessionsVisuals Instance { get; }

        public void Dispose()
        {
            this.Instance.SessionGrid.Dispose();
            this.Instance.SearchBox.Dispose();
            this.Instance.SessionTabs.Dispose();
            this.Instance.LoadingOverlay.Dispose();
        }
    }

    private sealed class FixtureRow
    {
        internal FixtureRow(string name, string cwd, bool expectedEnabled, string expectedTooltip, string? priorOwner = null, string? priorRepo = null)
        {
            this.Name = name;
            this.Cwd = cwd;
            this.ExpectedEnabled = expectedEnabled;
            this.ExpectedTooltip = expectedTooltip;
            this.PriorOwner = priorOwner;
            this.PriorRepo = priorRepo;
        }

        internal string Name { get; }

        internal string Cwd { get; }

        internal bool ExpectedEnabled { get; }

        internal string ExpectedTooltip { get; }

        internal string? PriorOwner { get; }

        internal string? PriorRepo { get; }

        internal string? SessionId { get; set; }
    }
}
