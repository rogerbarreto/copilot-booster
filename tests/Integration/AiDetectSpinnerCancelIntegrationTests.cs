using CopilotBooster.IntegrationTests.Integration.TestTools;

namespace CopilotBooster.IntegrationTests.Integration;

[Collection(WindowEventHookCollection.Name)]
public sealed class AiDetectSpinnerCancelIntegrationTests : IDisposable
{
    private readonly string _sessionRoot = Path.Combine(Path.GetTempPath(), $"cb-ai-spinner-{Guid.NewGuid():N}");
    private readonly string _sessionId = Guid.NewGuid().ToString();

    public void Dispose()
    {
        DeleteDirectory(this._sessionRoot);
        DeleteDirectory(SessionStateService.GetSessionDir(this._sessionId));
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public async Task Spinner_StartsWhenDetectionRunsAndStopsWhenDetectionEndsAsync()
    {
        using var harness = await Harness.CreateAsync(this._sessionRoot, this._sessionId).ConfigureAwait(true);
        var detectionTask = harness.StartDetectionAsync();
        await harness.WaitForProcessCallAsync().ConfigureAwait(true);

        Assert.Equal(DetectionStatus.Running, harness.Service.TryGetState(this._sessionId).Status);
        Assert.True(harness.Visuals.IsSpinnerVisibleForSession(this._sessionId));
        Assert.True(harness.IsSpinnerTimerEnabled());

        harness.CompleteProcess(new ProcessResult(0, "{\"candidates\":[]}", "", false));
        await detectionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(DetectionStatus.Error, harness.Service.TryGetState(this._sessionId).Status);
        await harness.WaitForSpinnerTimerDisabledAsync().ConfigureAwait(true);

        Assert.Equal(DetectionStatus.Error, harness.Service.TryGetState(this._sessionId).Status);
        Assert.False(harness.Visuals.IsSpinnerVisibleForSession(this._sessionId));
        Assert.False(harness.IsSpinnerTimerEnabled());
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public async Task CornerClick_StopCancelsDetectionAndClearsSpinnerAsync()
    {
        using var harness = await Harness.CreateAsync(this._sessionRoot, this._sessionId).ConfigureAwait(true);
        harness.ConfirmDialog.NextResult = true;
        var detectionTask = harness.StartDetectionAsync();
        var call = await harness.WaitForProcessCallAsync().ConfigureAwait(true);

        harness.ClickStatusCorner();

        Assert.Equal("Cancel detection?", harness.ConfirmDialog.Title);
        Assert.Equal("Stop detecting the GitHub link for this session?", harness.ConfirmDialog.Body);
        Assert.Equal("Stop", harness.ConfirmDialog.YesLabel);
        Assert.Equal("Keep running", harness.ConfirmDialog.NoLabel);
        Assert.True(call.CancellationToken.IsCancellationRequested);

        harness.CompleteProcess(new ProcessResult(-1, "", "", true));
        await detectionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(DetectionStatus.Idle, harness.Service.TryGetState(this._sessionId).Status);
        Assert.False(harness.Visuals.IsSpinnerVisibleForSession(this._sessionId));
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public async Task CornerClick_KeepRunningLeavesDetectionRunningAsync()
    {
        using var harness = await Harness.CreateAsync(this._sessionRoot, this._sessionId).ConfigureAwait(true);
        harness.ConfirmDialog.NextResult = false;
        var detectionTask = harness.StartDetectionAsync();
        var call = await harness.WaitForProcessCallAsync().ConfigureAwait(true);

        harness.ClickStatusCorner();

        Assert.Equal("Cancel detection?", harness.ConfirmDialog.Title);
        Assert.Equal(DetectionStatus.Running, harness.Service.TryGetState(this._sessionId).Status);
        Assert.False(call.CancellationToken.IsCancellationRequested);
        Assert.True(harness.Visuals.IsSpinnerVisibleForSession(this._sessionId));

        harness.Service.CancelDetection(this._sessionId);
        harness.CompleteProcess(new ProcessResult(-1, "", "", true));
        await detectionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public async Task NonCornerClickFallsThroughToExistingGitHubHandlerAsync()
    {
        using var harness = await Harness.CreateAsync(this._sessionRoot, this._sessionId).ConfigureAwait(true);
        var detectionTask = harness.StartDetectionAsync();
        await harness.WaitForProcessCallAsync().ConfigureAwait(true);

        harness.ClickOutsideStatusCorner();

        Assert.Equal(1, harness.FallthroughClicks);
        Assert.Equal(this._sessionId, harness.FallthroughSessionId);
        Assert.False(harness.ConfirmDialog.WasInvoked);
        Assert.Equal(DetectionStatus.Running, harness.Service.TryGetState(this._sessionId).Status);

        harness.Service.CancelDetection(this._sessionId);
        harness.CompleteProcess(new ProcessResult(-1, "", "", true));
        await detectionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _sessionId;
        private Harness(string sessionId, DataGridView grid, FakeProcessRunner processRunner, AiDetectionService service, SessionGridVisuals visuals, RecordingConfirmDialog confirmDialog)
        {
            this._sessionId = sessionId;
            this.Grid = grid;
            this.ProcessRunner = processRunner;
            this.Service = service;
            this.Visuals = visuals;
            this.ConfirmDialog = confirmDialog;
        }

        internal DataGridView Grid { get; }

        internal FakeProcessRunner ProcessRunner { get; }

        internal AiDetectionService Service { get; }

        internal SessionGridVisuals Visuals { get; }

        internal RecordingConfirmDialog ConfirmDialog { get; }

        internal int FallthroughClicks { get; private set; }

        internal string? FallthroughSessionId { get; private set; }

        internal static async Task<Harness> CreateAsync(string sessionRoot, string sessionId)
        {
            var repoRoot = FindRepoRoot();
            var sessionDir = Path.Combine(sessionRoot, sessionId);
            Directory.CreateDirectory(sessionDir);
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "workspace.yaml"), $"id: {sessionId}\ncwd: {repoRoot}\nsummary: spinner cancel fixture\n", TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "events.jsonl"), "{\"type\":\"user.message\",\"message\":\"find github link\"}\n", TestContext.Current.CancellationToken).ConfigureAwait(true);
            GitHubTrackingService.Save(sessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster" });

            var grid = CreateGrid();
            AddRow(grid, sessionId);
            var processRunner = new FakeProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false))
            {
                Completion = new TaskCompletionSource<ProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            using var poller = new GitHubPollingService(CreateFakeApi(), () => [sessionId]);
            var service = new AiDetectionService(CreateFakeApi(), processRunner, _ => repoRoot, _ => { }, null, sessionRoot);
            var confirmDialog = new RecordingConfirmDialog();
            var visuals = new SessionGridVisuals(grid, new ActiveStatusTracker(), CreateTestSettings())
            {
                AiDetectionService = service,
                ConfirmDialog = confirmDialog
            };

            var harness = new Harness(sessionId, grid, processRunner, service, visuals, confirmDialog);
            visuals.OnGitHubColumnClick += (sid, _, _) =>
            {
                harness.FallthroughClicks++;
                harness.FallthroughSessionId = sid;
            };

            return harness;
        }

        internal Task StartDetectionAsync()
        {
            return this.Service.StartDetectionAsync(this._sessionId);
        }

        internal async Task<FakeProcessRunnerCall> WaitForProcessCallAsync()
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (this.ProcessRunner.Calls.Count > 0)
                {
                    return this.ProcessRunner.Calls[0];
                }

                await Task.Delay(25, TestContext.Current.CancellationToken).ConfigureAwait(true);
            }

            Assert.Fail("AI detection did not start the process runner before timeout.");
            return null!;
        }

        internal void CompleteProcess(ProcessResult result)
        {
            Assert.NotNull(this.ProcessRunner.Completion);
            this.ProcessRunner.Completion.TrySetResult(result);
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

        internal bool IsSpinnerTimerEnabled()
        {
            var field = typeof(SessionGridVisuals).GetField("_spinnerTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var timer = Assert.IsType<System.Windows.Forms.Timer>(field?.GetValue(this.Visuals));
            return timer.Enabled;
        }

        internal async Task WaitForSpinnerTimerDisabledAsync()
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                if (this.Service.TryGetState(this._sessionId).Status != DetectionStatus.Running && this.IsSpinnerTimerEnabled())
                {
                    this.InvokeSpinnerTick();
                }

                if (!this.IsSpinnerTimerEnabled())
                {
                    return;
                }

                await Task.Delay(25, TestContext.Current.CancellationToken).ConfigureAwait(true);
            }

            Assert.Fail("Spinner timer did not stop before timeout.");
        }

        internal void InvokeSpinnerTick()
        {
            var method = typeof(SessionGridVisuals).GetMethod("OnSpinnerTimerTick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(this.Visuals, [null, EventArgs.Empty]);
        }

        public void Dispose()
        {
            this.Visuals.Dispose();
            this.Service.Dispose();
            this.Grid.Dispose();
        }

        private static Rectangle CellBounds() => new(0, 0, 100, 30);
    }

    private sealed class RecordingConfirmDialog : IConfirmDialog
    {
        internal bool NextResult { get; set; }

        internal bool WasInvoked { get; private set; }

        internal string? Title { get; private set; }

        internal string? Body { get; private set; }

        internal string? YesLabel { get; private set; }

        internal string? NoLabel { get; private set; }

        public bool Confirm(string title, string body, string yesLabel, string noLabel)
        {
            this.WasInvoked = true;
            this.Title = title;
            this.Body = body;
            this.YesLabel = yesLabel;
            this.NoLabel = noLabel;
            return this.NextResult;
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
        return new GitHubApiService(processRunner: (_, _) => Task.FromResult((1, "", "unexpected gh call")));
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
