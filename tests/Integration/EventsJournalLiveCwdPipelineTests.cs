using System.Collections.Concurrent;
using System.Text;

namespace CopilotBooster.IntegrationTests.Integration;

[Trait("Category", "LocalOnly")]
public sealed class EventsJournalLiveCwdPipelineTests : IDisposable
{
    private readonly string _tempRoot;

    public EventsJournalLiveCwdPipelineTests()
    {
        this._tempRoot = Path.Combine(Path.GetTempPath(), "copilot-booster-live-cwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this._tempRoot))
            {
                Directory.Delete(this._tempRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    [LocalOnlyFact]
    public void ProductionRefreshPipeline_AfterLiveCwdChange_OverlaysLiveCwdOntoStaleWorkspaceYaml()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempRoot, sessionId);
        Directory.CreateDirectory(sessionDir);

        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\old",
                "name: TestSession") + Environment.NewLine,
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(sessionDir, "events.jsonl"),
            string.Join(Environment.NewLine,
                SessionStart(sessionId, @"D:\old"),
                HookStart(sessionId, @"D:\old"),
                HookStart(sessionId, @"D:\new")) + Environment.NewLine,
            Encoding.UTF8);

        var sessionStateFile = Path.Combine(this._tempRoot, "session-state.json");
        var aliasFile = Path.Combine(this._tempRoot, "aliases.json");
        var overrideFile = Path.Combine(this._tempRoot, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        using var eventsJournal = new EventsJournalService(this._tempRoot);
        eventsJournal.PrimeCache([sessionId]);

        Assert.True(eventsJournal.TryGetLatestCwd(sessionId, out var liveCwd));
        Assert.Equal(@"D:\new", liveCwd);

        var freshSessions = SessionService.LoadNamedSessions(
            this._tempRoot,
            pidRegistryFile: null,
            sessionStateFile: sessionStateFile,
            aliasFile: aliasFile,
            overrideFile: overrideFile);

        var session = Assert.Single(freshSessions);
        Assert.Equal(@"D:\old", session.Cwd);

        EventsJournalService.ApplyLiveCwdOverlay(freshSessions, eventsJournal);

        Assert.Equal(@"D:\new", session.Cwd);
        Assert.Equal("new", session.Folder);
    }

    [LocalOnlyFact]
    public void ExtractLatestCwd_RealBrokenSession_ReturnsAgentFrameworkCwd()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var eventsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot",
            "session-state",
            sessionId,
            "events.jsonl");

        if (!File.Exists(eventsPath))
        {
            Assert.Skip($"Real session fixture not found: {eventsPath}");
        }

        using var stream = new FileStream(eventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        var latestCwd = EventsJournalService.ExtractLatestCwd(reader);

        Assert.Equal(@"D:\repo\work\agent-framework", latestCwd);
    }

    private static string SessionStart(string sessionId, string cwd)
    {
        return "{\"type\":\"session.start\",\"data\":{\"sessionId\":\"" + sessionId + "\",\"context\":{\"cwd\":\"" + Escape(cwd) + "\"}}}";
    }

    private static string HookStart(string sessionId, string cwd)
    {
        return "{\"type\":\"hook.start\",\"data\":{\"hookInvocationId\":\"hook-1\",\"hookType\":\"preToolUse\",\"input\":{\"sessionId\":\"" + sessionId + "\",\"cwd\":\"" + Escape(cwd) + "\",\"toolCalls\":[]}}}";
    }

    private static string Escape(string value)
    {
        return value.Replace(@"\", @"\\", StringComparison.Ordinal);
    }
}
