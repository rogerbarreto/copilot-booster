using System.Globalization;
using CopilotBooster.IntegrationTests.Integration.TestTools;

namespace CopilotBooster.IntegrationTests.Integration;

[Collection(WindowEventHookCollection.Name)]
public sealed class AiDetectIconsAndDismissIntegrationTests : IDisposable
{
    private readonly string _sessionRoot = Path.Combine(Path.GetTempPath(), $"cb-ai-icons-{Guid.NewGuid():N}");
    private readonly string _sessionId = Guid.NewGuid().ToString();

    public void Dispose()
    {
        DeleteDirectory(this._sessionRoot);
        DeleteDirectory(SessionStateService.GetSessionDir(this._sessionId));
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public async Task UndecidedLowConfidenceCornerClick_ShowsCandidatesAndClearsIconAsync()
    {
        using var harness = await Harness.CreateAsync(this._sessionRoot, this._sessionId, new ProcessResult(0, CandidatesJson(("pr", 42, 0.3, "weak match")), "", false)).ConfigureAwait(true);

        await harness.StartDetectionAsync().ConfigureAwait(true);

        Assert.Equal(DetectionStatus.Undecided, harness.Service.TryGetState(this._sessionId).Status);
        Assert.NotNull(harness.Visuals.GetCornerIconForSession(this._sessionId));

        harness.ClickStatusCorner();

        Assert.Contains("PR #42", harness.MessageBox.Body, StringComparison.Ordinal);
        Assert.Contains("0.30", harness.MessageBox.Body, StringComparison.Ordinal);
        Assert.Equal(DetectionStatus.Idle, harness.Service.TryGetState(this._sessionId).Status);
        Assert.Null(harness.Visuals.GetCornerIconForSession(this._sessionId));
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public async Task UndecidedAllAlreadyLinkedCornerClick_ShowsAllLinkedTextAndClearsIconAsync()
    {
        using var harness = await Harness.CreateAsync(
            this._sessionRoot,
            this._sessionId,
            new ProcessResult(0, CandidatesJson(("pr", 42, 0.9, "already linked")), "", false),
            [Tracked("pr", 42)]).ConfigureAwait(true);

        await harness.StartDetectionAsync().ConfigureAwait(true);

        Assert.Equal(DetectionStatus.Undecided, harness.Service.TryGetState(this._sessionId).Status);
        Assert.NotNull(harness.Visuals.GetCornerIconForSession(this._sessionId));

        harness.ClickStatusCorner();

        Assert.Equal("All matches were already linked to this session.", harness.MessageBox.Body);
        Assert.Equal(DetectionStatus.Idle, harness.Service.TryGetState(this._sessionId).Status);
        Assert.Null(harness.Visuals.GetCornerIconForSession(this._sessionId));
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public async Task ErrorProcessFailureCornerClick_ShowsFailureTextAndClearsIconAsync()
    {
        using var harness = await Harness.CreateAsync(this._sessionRoot, this._sessionId, new ProcessResult(1, "", "failed", false)).ConfigureAwait(true);

        await harness.StartDetectionAsync().ConfigureAwait(true);

        Assert.Equal(DetectionStatus.Error, harness.Service.TryGetState(this._sessionId).Status);
        Assert.NotNull(harness.Visuals.GetCornerIconForSession(this._sessionId));

        harness.ClickStatusCorner();

        Assert.Equal("Detection failed: Copilot exited with error. See app log for details.", harness.MessageBox.Body);
        Assert.Equal(DetectionStatus.Idle, harness.Service.TryGetState(this._sessionId).Status);
        Assert.Null(harness.Visuals.GetCornerIconForSession(this._sessionId));
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public async Task ManualAddPrAfterUndecided_ClearsIconAsync()
    {
        using var harness = await Harness.CreateAsync(this._sessionRoot, this._sessionId, new ProcessResult(0, CandidatesJson(("pr", 42, 0.3, "weak match")), "", false)).ConfigureAwait(true);
        await harness.StartDetectionAsync().ConfigureAwait(true);
        Assert.Equal(DetectionStatus.Undecided, harness.Service.TryGetState(this._sessionId).Status);

        GitHubTrackingService.AddItem(this._sessionId, "rogerbarreto", "copilot-booster", Tracked("pr", 123));
        harness.Service.Reset(this._sessionId);

        Assert.Equal(DetectionStatus.Idle, harness.Service.TryGetState(this._sessionId).Status);
        Assert.Null(harness.Visuals.GetCornerIconForSession(this._sessionId));
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public async Task NonCornerClickDuringUndecidedOrError_FallsThroughToGitHubHandlerAsync()
    {
        using (var undecided = await Harness.CreateAsync(this._sessionRoot, this._sessionId, new ProcessResult(0, CandidatesJson(("pr", 42, 0.3, "weak match")), "", false)).ConfigureAwait(true))
        {
            await undecided.StartDetectionAsync().ConfigureAwait(true);
            undecided.ClickOutsideStatusCorner();
            Assert.Equal(1, undecided.FallthroughClicks);
            Assert.Equal(this._sessionId, undecided.FallthroughSessionId);
            Assert.False(undecided.MessageBox.WasInvoked);
        }

        var errorSessionId = Guid.NewGuid().ToString();
        using var error = await Harness.CreateAsync(this._sessionRoot, errorSessionId, new ProcessResult(1, "", "failed", false)).ConfigureAwait(true);
        await error.StartDetectionAsync().ConfigureAwait(true);
        error.ClickOutsideStatusCorner();
        Assert.Equal(1, error.FallthroughClicks);
        Assert.Equal(errorSessionId, error.FallthroughSessionId);
        Assert.False(error.MessageBox.WasInvoked);
        DeleteDirectory(SessionStateService.GetSessionDir(errorSessionId));
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _sessionId;

        private Harness(string sessionId, DataGridView grid, FakeProcessRunner processRunner, AiDetectionService service, SessionGridVisuals visuals, RecordingMessageBox messageBox)
        {
            this._sessionId = sessionId;
            this.Grid = grid;
            this.ProcessRunner = processRunner;
            this.Service = service;
            this.Visuals = visuals;
            this.MessageBox = messageBox;
        }

        internal DataGridView Grid { get; }

        internal FakeProcessRunner ProcessRunner { get; }

        internal AiDetectionService Service { get; }

        internal SessionGridVisuals Visuals { get; }

        internal RecordingMessageBox MessageBox { get; }

        internal int FallthroughClicks { get; private set; }

        internal string? FallthroughSessionId { get; private set; }

        internal static async Task<Harness> CreateAsync(string sessionRoot, string sessionId, ProcessResult result, IReadOnlyList<GitHubTrackedItem>? existingItems = null)
        {
            var repoRoot = FindRepoRoot();
            var sessionDir = Path.Combine(sessionRoot, sessionId);
            Directory.CreateDirectory(sessionDir);
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "workspace.yaml"), $"id: {sessionId}\ncwd: {repoRoot}\nsummary: icons fixture\n", TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "events.jsonl"), "{\"type\":\"user.message\",\"message\":\"find github link\"}\n", TestContext.Current.CancellationToken).ConfigureAwait(true);
            GitHubTrackingService.Save(sessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster", Items = existingItems?.ToList() ?? [] });

            var grid = CreateGrid();
            AddRow(grid, sessionId);
            var processRunner = new FakeProcessRunner(result);
            var service = new AiDetectionService(CreateFakeApi(), processRunner, _ => repoRoot, _ => { }, null, sessionRoot);
            var messageBox = new RecordingMessageBox();
            var visuals = new SessionGridVisuals(grid, new ActiveStatusTracker(), CreateTestSettings())
            {
                AiDetectionService = service,
                MessageBox = messageBox,
                GetGitHubValue = BuildGitHubValue
            };

            var harness = new Harness(sessionId, grid, processRunner, service, visuals, messageBox);
            visuals.OnGitHubColumnClick += (sid, _, _) =>
            {
                harness.FallthroughClicks++;
                harness.FallthroughSessionId = sid;
            };

            return harness;
        }

        internal Task StartDetectionAsync()
        {
            return this.Service.StartDetectionAsync(this._sessionId).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        internal void ClickStatusCorner()
        {
            var bounds = CellBounds();
            var region = SessionGridVisuals.GetStatusIconRegion(bounds);
            this.Visuals.HandleGitHubCellClick(0, new Point(region.Left + 1, region.Top + 1), bounds);
        }

        internal void ClickOutsideStatusCorner()
        {
            var bounds = CellBounds();
            this.Visuals.HandleGitHubCellClick(0, new Point(1, bounds.Height - 1), bounds);
        }

        public void Dispose()
        {
            this.Visuals.Dispose();
            this.Service.Dispose();
            this.Grid.Dispose();
        }

        private static Rectangle CellBounds() => new(0, 0, 100, 30);
    }

    private sealed class RecordingMessageBox : IMessageBox
    {
        internal bool WasInvoked { get; private set; }

        internal string? Title { get; private set; }

        internal string? Body { get; private set; }

        public void Show(string title, string body)
        {
            this.WasInvoked = true;
            this.Title = title;
            this.Body = body;
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
        var rowIndex = grid.Rows.Add("", sessionId, "", "", "", "", "");
        grid.Rows[rowIndex].Tag = sessionId;
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

    private static string BuildGitHubValue(string sessionId)
    {
        var data = GitHubTrackingService.Load(sessionId);
        if (data == null || data.Items.Count == 0)
        {
            return "";
        }

        return string.Join(" ", data.Items.Select(item => $"{(item.IsPr ? "PR" : "I")}#{item.Number}"));
    }

    private static GitHubTrackedItem Tracked(string type, int number)
    {
        return new GitHubTrackedItem { Type = type, Number = number, State = "open", Title = $"{type} {number}" };
    }

    private static string CandidatesJson(params (string Type, int Number, double Confidence, string Reasoning)[] candidates)
    {
        return "{\"candidates\":[" + string.Join(",", candidates.Select(candidate => $"{{\"type\":\"{candidate.Type}\",\"number\":{candidate.Number},\"confidence\":{candidate.Confidence.ToString(CultureInfo.InvariantCulture)},\"reasoning\":\"{candidate.Reasoning}\"}}")) + "]}";
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
