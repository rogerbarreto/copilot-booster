using System.Text;

public sealed class SessionServiceCwdResolutionTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceCwdResolutionTests()
    {
        this._tempDir = Path.Combine(AppContext.BaseDirectory, "SessionServiceCwdResolutionTests", Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this._tempDir, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void LoadNamedSessions_WhenEventsJsonlNewerThanWorkspaceYaml_PrefersEventsCwd()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var yamlPath = Path.Combine(sessionDir, "workspace.yaml");
        File.WriteAllText(
            yamlPath,
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\old",
                "name: TestSession") + Environment.NewLine,
            Encoding.UTF8);

        var t0 = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(yamlPath, t0);

        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        File.WriteAllText(
            eventsPath,
            string.Join(Environment.NewLine,
                SessionStart(sessionId, @"D:\old"),
                HookStart(sessionId, @"D:\new")) + Environment.NewLine,
            Encoding.UTF8);

        File.SetLastWriteTimeUtc(eventsPath, t0.AddSeconds(10));

        var sessionStateFile = Path.Combine(this._tempDir, "session-state.json");
        var aliasFile = Path.Combine(this._tempDir, "aliases.json");
        var overrideFile = Path.Combine(this._tempDir, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        var sessions = SessionService.LoadNamedSessions(
            this._tempDir,
            pidRegistryFile: null,
            sessionStateFile: sessionStateFile,
            aliasFile: aliasFile,
            overrideFile: overrideFile);

        var session = Assert.Single(sessions);
        Assert.Equal(@"D:\new", session.Cwd);
        Assert.Equal("new", session.Folder);
    }

    [Fact]
    public void LoadNamedSessions_WhenWorkspaceYamlNewerThanEventsJsonl_PrefersYamlCwd()
    {
        const string sessionId = "d1277063-ce93-44b0-95cc-7deee25b676a";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var t0 = DateTime.UtcNow;

        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        File.WriteAllText(
            eventsPath,
            string.Join(Environment.NewLine,
                SessionStart(sessionId, @"D:\repo\work"),
                HookStart(sessionId, @"D:\repo\work")) + Environment.NewLine,
            Encoding.UTF8);

        File.SetLastWriteTimeUtc(eventsPath, t0);

        var yamlPath = Path.Combine(sessionDir, "workspace.yaml");
        File.WriteAllText(
            yamlPath,
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\repo\\work\\agent-framework",
                "name: \"Hosted Agents V2 Questions\"") + Environment.NewLine,
            Encoding.UTF8);

        File.SetLastWriteTimeUtc(yamlPath, t0.AddSeconds(10));

        var sessionStateFile = Path.Combine(this._tempDir, "session-state.json");
        var aliasFile = Path.Combine(this._tempDir, "aliases.json");
        var overrideFile = Path.Combine(this._tempDir, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        var sessions = SessionService.LoadNamedSessions(
            this._tempDir,
            pidRegistryFile: null,
            sessionStateFile: sessionStateFile,
            aliasFile: aliasFile,
            overrideFile: overrideFile);

        var session = Assert.Single(sessions);
        Assert.Equal(@"D:\repo\work\agent-framework", session.Cwd);
        Assert.Equal("agent-framework", session.Folder);
    }

    [Fact]
    public void LoadNamedSessions_WhenEventsJsonlHasNoHookStart_FallsBackToYamlCwd()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var t0 = DateTime.UtcNow;

        var yamlPath = Path.Combine(sessionDir, "workspace.yaml");
        File.WriteAllText(
            yamlPath,
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\yaml",
                "name: TestSession") + Environment.NewLine,
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(yamlPath, t0);

        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        File.WriteAllText(
            eventsPath,
            SessionStart(sessionId, @"D:\old") + Environment.NewLine,
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(eventsPath, t0.AddSeconds(-10)); // events older than yaml

        var sessionStateFile = Path.Combine(this._tempDir, "session-state.json");
        var aliasFile = Path.Combine(this._tempDir, "aliases.json");
        var overrideFile = Path.Combine(this._tempDir, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        var sessions = SessionService.LoadNamedSessions(
            this._tempDir,
            pidRegistryFile: null,
            sessionStateFile: sessionStateFile,
            aliasFile: aliasFile,
            overrideFile: overrideFile);

        var session = Assert.Single(sessions);
        Assert.Equal(@"D:\yaml", session.Cwd);
        Assert.Equal("yaml", session.Folder);
    }

    [Fact]
    public void LoadNamedSessions_WhenEventsJsonlNewerWithOnlySessionStart_PrefersSessionStartCwd()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var t0 = DateTime.UtcNow;

        var yamlPath = Path.Combine(sessionDir, "workspace.yaml");
        File.WriteAllText(
            yamlPath,
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\yaml",
                "name: TestSession") + Environment.NewLine,
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(yamlPath, t0);

        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        File.WriteAllText(
            eventsPath,
            SessionStart(sessionId, @"D:\events-session-start") + Environment.NewLine,
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(eventsPath, t0.AddSeconds(10));

        var sessionStateFile = Path.Combine(this._tempDir, "session-state.json");
        var aliasFile = Path.Combine(this._tempDir, "aliases.json");
        var overrideFile = Path.Combine(this._tempDir, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        var sessions = SessionService.LoadNamedSessions(
            this._tempDir,
            pidRegistryFile: null,
            sessionStateFile: sessionStateFile,
            aliasFile: aliasFile,
            overrideFile: overrideFile);

        var session = Assert.Single(sessions);
        Assert.Equal(@"D:\events-session-start", session.Cwd);
        Assert.Equal("events-session-start", session.Folder);
    }

    [Fact]
    public void LoadNamedSessions_WhenEventsAndYamlMtimesAreEqual_PrefersWorkspaceYamlCwd()
    {
        const string sessionId = "8b0be6d5-3e0a-4a3f-9e99-68d690ab2ec1";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var yamlPath = Path.Combine(sessionDir, "workspace.yaml");
        File.WriteAllText(
            yamlPath,
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\yaml-tie",
                "name: TestSession") + Environment.NewLine,
            Encoding.UTF8);

        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        File.WriteAllText(
            eventsPath,
            HookStart(sessionId, @"D:\events-tie") + Environment.NewLine,
            Encoding.UTF8);

        var sameMtime = new DateTime(2026, 5, 15, 19, 45, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(yamlPath, sameMtime);
        File.SetLastWriteTimeUtc(eventsPath, sameMtime);

        var session = Assert.Single(this.LoadSessions());
        Assert.Equal(@"D:\yaml-tie", session.Cwd);
        Assert.Equal("yaml-tie", session.Folder);
    }

    [Fact]
    public void LoadNamedSessions_WhenEventsJsonlHookStartCwdIsEmptyString_FallsBackToYamlCwd()
    {
        const string sessionId = "053d33d3-7597-4059-829d-8c6c5df54768";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var t0 = DateTime.UtcNow;

        var yamlPath = Path.Combine(sessionDir, "workspace.yaml");
        File.WriteAllText(
            yamlPath,
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\yaml",
                "name: TestSession") + Environment.NewLine,
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(yamlPath, t0);

        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        File.WriteAllText(
            eventsPath,
            HookStart(sessionId, string.Empty) + Environment.NewLine,
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(eventsPath, t0.AddSeconds(10));

        var session = Assert.Single(this.LoadSessions());
        Assert.Equal(@"D:\yaml", session.Cwd);
        Assert.Equal("yaml", session.Folder);
    }

    [Fact]
    public void LoadNamedSessions_WhenWorkspaceYamlCwdIsMissingOrEmpty_FallsBackToEventsCwd()
    {
        const string missingCwdSessionId = "1d284d78-493e-4d18-9af9-4024a5d9373b";
        const string emptyCwdSessionId = "1bc426c7-dc2d-44d9-8d9e-758037396bf6";
        var missingCwdSessionDir = Path.Combine(this._tempDir, missingCwdSessionId);
        var emptyCwdSessionDir = Path.Combine(this._tempDir, emptyCwdSessionId);
        Directory.CreateDirectory(missingCwdSessionDir);
        Directory.CreateDirectory(emptyCwdSessionDir);

        File.WriteAllText(
            Path.Combine(missingCwdSessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {missingCwdSessionId}",
                "name: MissingCwd") + Environment.NewLine,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(missingCwdSessionDir, "events.jsonl"),
            HookStart(missingCwdSessionId, @"D:\events-missing") + Environment.NewLine,
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(emptyCwdSessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {emptyCwdSessionId}",
                "cwd:",
                "name: EmptyCwd") + Environment.NewLine,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(emptyCwdSessionDir, "events.jsonl"),
            HookStart(emptyCwdSessionId, @"D:\events-empty") + Environment.NewLine,
            Encoding.UTF8);

        var sessions = this.LoadSessions();

        Assert.Equal(@"D:\events-missing", sessions.Single(s => s.Id == missingCwdSessionId).Cwd);
        Assert.Equal(@"D:\events-empty", sessions.Single(s => s.Id == emptyCwdSessionId).Cwd);
    }

    [Fact]
    public void LoadNamedSessions_MultiSession_ResolvesEachIndependently()
    {
        const string yamlWinsSessionId = "7a79bbfb-09f6-4c84-99ed-548f6e9343a1";
        const string eventsWinsSessionId = "cc76c8fe-bb4d-4f54-bf00-09841e989c90";
        var yamlWinsDir = Path.Combine(this._tempDir, yamlWinsSessionId);
        var eventsWinsDir = Path.Combine(this._tempDir, eventsWinsSessionId);
        Directory.CreateDirectory(yamlWinsDir);
        Directory.CreateDirectory(eventsWinsDir);

        var t0 = DateTime.UtcNow;

        var yamlWinsEventsPath = Path.Combine(yamlWinsDir, "events.jsonl");
        File.WriteAllText(
            yamlWinsEventsPath,
            HookStart(yamlWinsSessionId, @"D:\yaml-wins-events-old") + Environment.NewLine,
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(yamlWinsEventsPath, t0);

        var yamlWinsYamlPath = Path.Combine(yamlWinsDir, "workspace.yaml");
        File.WriteAllText(
            yamlWinsYamlPath,
            string.Join(Environment.NewLine,
                $"id: {yamlWinsSessionId}",
                "cwd: D:\\yaml-wins",
                "name: YamlWins") + Environment.NewLine,
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(yamlWinsYamlPath, t0.AddSeconds(10));

        var eventsWinsYamlPath = Path.Combine(eventsWinsDir, "workspace.yaml");
        File.WriteAllText(
            eventsWinsYamlPath,
            string.Join(Environment.NewLine,
                $"id: {eventsWinsSessionId}",
                "cwd: D:\\events-wins-yaml-old",
                "name: EventsWins") + Environment.NewLine,
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(eventsWinsYamlPath, t0);

        var eventsWinsEventsPath = Path.Combine(eventsWinsDir, "events.jsonl");
        File.WriteAllText(
            eventsWinsEventsPath,
            HookStart(eventsWinsSessionId, @"D:\events-wins") + Environment.NewLine,
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(eventsWinsEventsPath, t0.AddSeconds(10));

        var sessions = this.LoadSessions();

        Assert.Equal(@"D:\yaml-wins", sessions.Single(s => s.Id == yamlWinsSessionId).Cwd);
        Assert.Equal(@"D:\events-wins", sessions.Single(s => s.Id == eventsWinsSessionId).Cwd);
    }

    [Fact]
    public void LoadNamedSessions_TailRead_HandlesPartiallyWrittenFinalLine_WithoutThrowing()
    {
        const string sessionId = "f3b904c0-52d0-4a5f-a31f-d35a49b2982d";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var t0 = DateTime.UtcNow;

        var yamlPath = Path.Combine(sessionDir, "workspace.yaml");
        File.WriteAllText(
            yamlPath,
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\yaml",
                "name: TestSession") + Environment.NewLine,
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(yamlPath, t0);

        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        File.WriteAllText(
            eventsPath,
            HookStart(sessionId, @"D:\valid-before-partial") + Environment.NewLine +
            "{\"type\":\"hook.start\",\"data\":{\"input\":{\"sessionId\":\"" + sessionId + "\",\"cwd\":\"D:",
            Encoding.UTF8);
        File.SetLastWriteTimeUtc(eventsPath, t0.AddSeconds(10));

        var session = Assert.Single(this.LoadSessions());
        Assert.Equal(@"D:\valid-before-partial", session.Cwd);
        Assert.Equal("valid-before-partial", session.Folder);
    }

    [Fact]
    public void LoadNamedSessions_NameWithDoubleWrappedSingleQuotes_StripsAllWrappers()
    {
        const string nameSessionId = "d83f11d4-7ab7-43ab-a04d-6308fa68e7b2";
        const string summarySessionId = "ce4f55fe-fd62-48f8-96cd-e2f543d42528";
        var nameSessionDir = Path.Combine(this._tempDir, nameSessionId);
        var summarySessionDir = Path.Combine(this._tempDir, summarySessionId);
        Directory.CreateDirectory(nameSessionDir);
        Directory.CreateDirectory(summarySessionDir);

        File.WriteAllText(
            Path.Combine(nameSessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {nameSessionId}",
                "cwd: D:\\name-session",
                "name: \"'Hosted Agents'\"") + Environment.NewLine,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(summarySessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {summarySessionId}",
                "cwd: D:\\summary-session",
                "summary: \"'Hosted Agents Summary'\"") + Environment.NewLine,
            Encoding.UTF8);

        var sessions = this.LoadSessions();

        Assert.Equal("Hosted Agents", sessions.Single(s => s.Id == nameSessionId).Summary);
        Assert.Equal("Hosted Agents Summary", sessions.Single(s => s.Id == summarySessionId).Summary);
    }

    [Fact]
    public void LoadNamedSessions_WhenEventsJsonlMissing_ReturnsYamlCwd()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\yaml",
                "name: TestSession") + Environment.NewLine,
            Encoding.UTF8);

        var sessionStateFile = Path.Combine(this._tempDir, "session-state.json");
        var aliasFile = Path.Combine(this._tempDir, "aliases.json");
        var overrideFile = Path.Combine(this._tempDir, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        var sessions = SessionService.LoadNamedSessions(
            this._tempDir,
            pidRegistryFile: null,
            sessionStateFile: sessionStateFile,
            aliasFile: aliasFile,
            overrideFile: overrideFile);

        var session = Assert.Single(sessions);
        Assert.Equal(@"D:\yaml", session.Cwd);
        Assert.Equal("yaml", session.Folder);
    }

    [Fact]
    public void ExtractLatestCwdFromTail_OnLargeEventsJsonl_AllocatesLessThan500KB()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var largePayload = new string('x', 10_000);
        var largeLines = new List<string>();
        for (var i = 0; i < 800; i++)
        {
            largeLines.Add("{\"type\":\"assistant.message\",\"data\":{\"content\":\"" + largePayload + "\"}}");
        }
        largeLines.Add(HookStart(sessionId, @"D:\new"));

        var eventsJsonlPath = Path.Combine(sessionDir, "events.jsonl");
        File.WriteAllText(eventsJsonlPath, string.Join(Environment.NewLine, largeLines) + Environment.NewLine, Encoding.UTF8);

        _ = EventsJournalService.ExtractLatestCwdFromTail(eventsJsonlPath);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
        var result = EventsJournalService.ExtractLatestCwdFromTail(eventsJsonlPath);
        var afterBytes = GC.GetTotalAllocatedBytes(precise: true);

        var allocatedBytes = afterBytes - beforeBytes;

        Assert.Equal(@"D:\new", result);
        Assert.True(
            allocatedBytes < 500_000,
            $"tail-read allocated {allocatedBytes / 1024.0:F1}KB, expected <500KB");
    }

    [Fact]
    public void LoadNamedSessions_NameWithQuoteInQuote_StripsAllQuoteWrappers()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\work",
                "name: '\"Hosted Agents V2 Questions\"'") + Environment.NewLine,
            Encoding.UTF8);

        var sessionStateFile = Path.Combine(this._tempDir, "session-state.json");
        var aliasFile = Path.Combine(this._tempDir, "aliases.json");
        var overrideFile = Path.Combine(this._tempDir, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        var sessions = SessionService.LoadNamedSessions(
            this._tempDir,
            pidRegistryFile: null,
            sessionStateFile: sessionStateFile,
            aliasFile: aliasFile,
            overrideFile: overrideFile);

        var session = Assert.Single(sessions);
        Assert.Equal("Hosted Agents V2 Questions", session.Summary);
    }

    [Fact]
    public void LoadNamedSessions_SummaryWithQuoteInQuote_StripsAllQuoteWrappers()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\work",
                "summary: '\"Some summary with quotes\"'") + Environment.NewLine,
            Encoding.UTF8);

        var sessionStateFile = Path.Combine(this._tempDir, "session-state.json");
        var aliasFile = Path.Combine(this._tempDir, "aliases.json");
        var overrideFile = Path.Combine(this._tempDir, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        var sessions = SessionService.LoadNamedSessions(
            this._tempDir,
            pidRegistryFile: null,
            sessionStateFile: sessionStateFile,
            aliasFile: aliasFile,
            overrideFile: overrideFile);

        var session = Assert.Single(sessions);
        Assert.Equal("Some summary with quotes", session.Summary);
    }

    private static string SessionStart(string sessionId, string cwd)
    {
        return "{\"type\":\"session.start\",\"data\":{\"sessionId\":\"" + sessionId + "\",\"context\":{\"cwd\":\"" + Escape(cwd) + "\"}}}";
    }

    private static string HookStart(string sessionId, string cwd)
    {
        return "{\"type\":\"hook.start\",\"data\":{\"hookInvocationId\":\"hook-1\",\"hookType\":\"preToolUse\",\"input\":{\"sessionId\":\"" + sessionId + "\",\"cwd\":\"" + Escape(cwd) + "\",\"toolCalls\":[]}}}";
    }

    private List<NamedSession> LoadSessions()
    {
        var sessionStateFile = Path.Combine(this._tempDir, "session-state.json");
        var aliasFile = Path.Combine(this._tempDir, "aliases.json");
        var overrideFile = Path.Combine(this._tempDir, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        return SessionService.LoadNamedSessions(
            this._tempDir,
            pidRegistryFile: null,
            sessionStateFile: sessionStateFile,
            aliasFile: aliasFile,
            overrideFile: overrideFile);
    }

    private static string Escape(string value)
    {
        return value.Replace(@"\", @"\\", StringComparison.Ordinal);
    }
}
