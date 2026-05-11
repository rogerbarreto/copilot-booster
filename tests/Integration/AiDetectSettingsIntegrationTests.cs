using CopilotBooster.IntegrationTests.Integration.TestTools;

namespace CopilotBooster.IntegrationTests.Integration;

public sealed class AiDetectSettingsIntegrationTests : IDisposable
{
    private readonly string _fixtureRoot = Path.Combine(Path.GetTempPath(), $"cb-ai-settings-{Guid.NewGuid():N}");
    private readonly string _sessionRoot;
    private readonly List<string> _sessionIds = [];

    public AiDetectSettingsIntegrationTests()
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
    public void SettingsDisabled_BuildAiMenuItem_IsDisabledWithTooltip()
    {
        using var harness = this.CreateHarness(new AiDetectionSettings { Enabled = false }, new FakeCopilotProbe(true));

        var item = harness.Visuals.GetEvaluatedAiMenuItem(harness.SessionId, harness.Cwd);

        Assert.False(item.Enabled);
        Assert.Equal("AI auto-detect is disabled in Settings.", item.ToolTipText);
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public void ProbeUnavailable_BuildAiMenuItem_IsDisabledWithTooltip()
    {
        using var harness = this.CreateHarness(new AiDetectionSettings { Enabled = true }, new FakeCopilotProbe(false));

        var item = harness.Visuals.GetEvaluatedAiMenuItem(harness.SessionId, harness.Cwd);

        Assert.False(item.Enabled);
        Assert.Equal("Copilot CLI not found. Install via WinGet or ensure 'copilot' is on PATH.", item.ToolTipText);
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public async Task CustomSettings_TriggerDetection_PropagatesToRunnerAsync()
    {
        var settings = new AiDetectionSettings
        {
            Enabled = true,
            TimeoutSeconds = 30,
            Model = "gpt-5.2"
        };
        using var harness = this.CreateHarness(settings, new FakeCopilotProbe(true));

        await harness.Service.StartDetectionAsync(harness.SessionId).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        var call = Assert.Single(harness.ProcessRunner.Calls);
        Assert.Equal(CopilotLocator.FindCopilotExe(), call.FileName);
        Assert.Equal(30, call.TimeoutSeconds);
        var modelIndex = Array.IndexOf(call.Args, "--model");
        Assert.True(modelIndex >= 0);
        Assert.Equal("gpt-5.2", call.Args[modelIndex + 1]);
    }

    [StaFact]
    [Trait("Category", "Integration")]
    public async Task SettingsChangedInFlight_DoesNotDisturbRunningDetectionAndNextDetectionUsesNewSettingsAsync()
    {
        var settings = new AiDetectionSettings { Enabled = true, TimeoutSeconds = 300 };
        using var harness = this.CreateHarness(settings, new FakeCopilotProbe(true));
        var secondSessionId = this.CreateSession("second", harness.Cwd);
        GitHubTrackingService.Save(secondSessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster" });
        harness.ProcessRunner.Completion = new TaskCompletionSource<ProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = harness.Service.StartDetectionAsync(harness.SessionId);
        await WaitForCallCountAsync(harness.ProcessRunner, 1).ConfigureAwait(false);
        settings.TimeoutSeconds = 60;

        Assert.Equal(300, harness.ProcessRunner.Calls[0].TimeoutSeconds);
        harness.ProcessRunner.Completion.SetResult(new ProcessResult(0, "{\"candidates\":[]}", "", false));
        await firstTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        harness.ProcessRunner.Completion = null;
        await harness.Service.StartDetectionAsync(secondSessionId).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(60, harness.ProcessRunner.Calls[1].TimeoutSeconds);
    }

    private Harness CreateHarness(AiDetectionSettings settings, ICopilotProbe probe)
    {
        var cwd = this.CreateFolder("cwd");
        var sessionId = this.CreateSession("settings", cwd);
        GitHubTrackingService.Save(sessionId, new GitHubTrackingData { Owner = "rogerbarreto", Repo = "copilot-booster" });

        var panel = new Panel();
        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, CreateTestSettings())
        {
            GetSessionPaths = _ => (cwd, cwd)
        };
        AddRow(visuals.SessionGrid, sessionId, cwd);
        var processRunner = new FakeProcessRunner(new ProcessResult(0, "{\"candidates\":[]}", "", false));
        var service = new AiDetectionService(CreateFakeApi(), processRunner, _ => cwd, _ => { }, null, this._sessionRoot, settingsGetter: () => settings, copilotProbe: probe);
        visuals.AiDetectionService = service;

        return new Harness(sessionId, cwd, panel, visuals, service, processRunner);
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

    private string CreateFolder(string prefix)
    {
        var path = Path.Combine(this._fixtureRoot, $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static LauncherSettings CreateTestSettings()
    {
        var settings = LauncherSettings.CreateDefault();
        settings.SuppressSave = true;
        return settings;
    }

    private static GitHubApiService CreateFakeApi()
    {
        return new GitHubApiService(processRunner: (_, _) => Task.FromResult((1, "", "Unexpected gh call")));
    }

    private static void AddRow(DataGridView grid, string sessionId, string cwd)
    {
        var rowIndex = grid.Rows.Add("", sessionId, cwd, "", "", "", "");
        grid.Rows[rowIndex].Tag = sessionId;
    }

    private static async Task WaitForCallCountAsync(FakeProcessRunner processRunner, int expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (processRunner.Calls.Count >= expected)
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        Assert.Fail($"Expected {expected} process calls before timeout.");
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

    private sealed class Harness : IDisposable
    {
        internal Harness(string sessionId, string cwd, Panel panel, ExistingSessionsVisuals visuals, AiDetectionService service, FakeProcessRunner processRunner)
        {
            this.SessionId = sessionId;
            this.Cwd = cwd;
            this.Panel = panel;
            this.Visuals = visuals;
            this.Service = service;
            this.ProcessRunner = processRunner;
        }

        internal string SessionId { get; }

        internal string Cwd { get; }

        internal Panel Panel { get; }

        internal ExistingSessionsVisuals Visuals { get; }

        internal AiDetectionService Service { get; }

        internal FakeProcessRunner ProcessRunner { get; }

        public void Dispose()
        {
            this.Service.Dispose();
            this.Visuals.SessionGrid.Dispose();
            this.Visuals.SearchBox.Dispose();
            this.Visuals.SessionTabs.Dispose();
            this.Visuals.LoadingOverlay.Dispose();
            this.Panel.Dispose();
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
}
