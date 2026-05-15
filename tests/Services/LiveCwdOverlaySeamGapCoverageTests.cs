using System.Text;

public sealed class LiveCwdOverlaySeamGapCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public LiveCwdOverlaySeamGapCoverageTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), "copilot-booster-live-cwd-gap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this._tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void ApplyLiveCwdOverlay_WhenLiveCwdDiffersOnlyByCase_PreservesWorkspaceCwdAndFolder()
    {
        var sessionId = Guid.NewGuid().ToString();
        const string workspaceCwd = @"d:\CaseRoot\Project";
        this.WriteSession(sessionId, workspaceCwd, @"D:\CaseRoot\Project");

        using var eventsJournal = new EventsJournalService(this._tempDir);
        eventsJournal.PrimeCache([sessionId]);
        var freshSessions = this.LoadSessions();
        var session = Assert.Single(freshSessions);

        EventsJournalService.ApplyLiveCwdOverlay(freshSessions, eventsJournal);

        Assert.Equal(workspaceCwd, session.Cwd);
        Assert.Equal("Project", session.Folder);
    }

    [Fact]
    public void ApplyLiveCwdOverlay_WhenMultipleSessionsHaveMixedLiveCwdStates_OverlaysEachIndependently()
    {
        var firstSessionId = Guid.NewGuid().ToString();
        var secondSessionId = Guid.NewGuid().ToString();
        var thirdSessionId = Guid.NewGuid().ToString();
        this.WriteSession(firstSessionId, @"D:\old-one", @"D:\new-one");
        this.WriteSession(secondSessionId, @"D:\old-two");
        this.WriteSession(thirdSessionId, @"D:\old-three", @"D:\new-three");

        using var eventsJournal = new EventsJournalService(this._tempDir);
        eventsJournal.PrimeCache([firstSessionId, secondSessionId, thirdSessionId]);
        var freshSessions = this.LoadSessions();

        EventsJournalService.ApplyLiveCwdOverlay(freshSessions, eventsJournal);

        var byId = freshSessions.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(@"D:\new-one", byId[firstSessionId].Cwd);
        Assert.Equal("new-one", byId[firstSessionId].Folder);
        Assert.Equal(@"D:\old-two", byId[secondSessionId].Cwd);
        Assert.Equal("old-two", byId[secondSessionId].Folder);
        Assert.Equal(@"D:\new-three", byId[thirdSessionId].Cwd);
        Assert.Equal("new-three", byId[thirdSessionId].Folder);
    }

    [Fact]
    public void ApplyLiveCwdOverlay_WhenLiveCwdHasTrailingForwardSlash_ComputesFolderFromLastSegment()
    {
        var sessionId = Guid.NewGuid().ToString();
        const string liveCwd = "D:/repo/work/agent-framework/";
        this.WriteSession(sessionId, @"D:\stale", liveCwd);

        using var eventsJournal = new EventsJournalService(this._tempDir);
        eventsJournal.PrimeCache([sessionId]);
        var freshSessions = this.LoadSessions();
        var session = Assert.Single(freshSessions);

        EventsJournalService.ApplyLiveCwdOverlay(freshSessions, eventsJournal);

        Assert.Equal(liveCwd, session.Cwd);
        Assert.Equal("agent-framework", session.Folder);
    }

    [Fact]
    public void ProductionCallsite_MainFormOnDebouncedRefreshAsync_CallsOverlayBeforeCachingAndApplyingStates()
    {
        var mainFormPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Forms", "MainForm.cs"));
        if (!File.Exists(mainFormPath))
        {
            mainFormPath = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "src", "Forms", "MainForm.cs"));
        }

        Assert.True(File.Exists(mainFormPath), $"MainForm.cs not found at {mainFormPath}");
        var mainFormContent = File.ReadAllText(mainFormPath);
        var debounceMethod = ExtractMethod(mainFormContent, "OnDebouncedRefreshAsync");
        Assert.NotNull(debounceMethod);

        var loadIndex = debounceMethod.IndexOf("this._refreshCoordinator.LoadSessions()", StringComparison.Ordinal);
        var overlayIndex = debounceMethod.IndexOf("EventsJournalService.ApplyLiveCwdOverlay(sessions, this._eventsJournal)", StringComparison.Ordinal);
        var cacheIndex = debounceMethod.IndexOf("this._cachedSessions = sessions;", StringComparison.Ordinal);
        var applyStatesIndex = debounceMethod.IndexOf("this.ApplySessionStates(this._cachedSessions);", StringComparison.Ordinal);

        Assert.True(loadIndex >= 0, "Expected OnDebouncedRefreshAsync to load fresh sessions.");
        Assert.True(overlayIndex > loadIndex, "Expected live CWD overlay after LoadSessions.");
        Assert.True(cacheIndex > overlayIndex, "Expected _cachedSessions assignment after live CWD overlay.");
        Assert.True(applyStatesIndex > cacheIndex, "Expected ApplySessionStates after caching overlaid sessions.");
    }

    [Fact]
    public void ProductionCallsite_RefreshBackgroundCoreAsync_CallsOverlayBeforeCachingAndApplyingStates()
    {
        var mainFormPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Forms", "MainForm.cs"));
        if (!File.Exists(mainFormPath))
        {
            mainFormPath = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "src", "Forms", "MainForm.cs"));
        }

        Assert.True(File.Exists(mainFormPath), $"MainForm.cs not found at {mainFormPath}");
        var mainFormContent = File.ReadAllText(mainFormPath);
        var refreshMethod = ExtractMethod(mainFormContent, "RefreshBackgroundCoreAsync");
        Assert.NotNull(refreshMethod);

        var loadIndex = refreshMethod.IndexOf("this._refreshCoordinator.LoadSessions()", StringComparison.Ordinal);
        var overlayIndex = refreshMethod.IndexOf("EventsJournalService.ApplyLiveCwdOverlay(sessions, this._eventsJournal)", StringComparison.Ordinal);
        var cacheIndex = refreshMethod.IndexOf("this._cachedSessions = sessions;", StringComparison.Ordinal);
        var applyStatesIndex = refreshMethod.IndexOf("this.ApplySessionStates(this._cachedSessions);", StringComparison.Ordinal);

        Assert.True(loadIndex >= 0, "Expected RefreshBackgroundCoreAsync to load fresh sessions.");
        Assert.True(overlayIndex > loadIndex, "Expected live CWD overlay after LoadSessions.");
        Assert.True(cacheIndex > overlayIndex, "Expected _cachedSessions assignment after live CWD overlay.");
        Assert.True(applyStatesIndex > cacheIndex, "Expected ApplySessionStates after caching overlaid sessions.");
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

    private void WriteSession(string sessionId, string workspaceCwd, params string[] liveCwds)
    {
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                $"cwd: {workspaceCwd}",
                "name: TestSession") + Environment.NewLine,
            Encoding.UTF8);

        var eventLines = liveCwds.Select(cwd => HookStart(sessionId, cwd));
        File.WriteAllText(
            Path.Combine(sessionDir, "events.jsonl"),
            string.Join(Environment.NewLine, eventLines) + Environment.NewLine,
            Encoding.UTF8);
    }

    private static string? ExtractMethod(string source, string methodName)
    {
        var methodStart = source.IndexOf($"void {methodName}", StringComparison.Ordinal);
        if (methodStart == -1)
        {
            methodStart = source.IndexOf($"async void {methodName}", StringComparison.Ordinal);
        }
        if (methodStart == -1)
        {
            methodStart = source.IndexOf($"Task {methodName}", StringComparison.Ordinal);
        }
        if (methodStart == -1)
        {
            methodStart = source.IndexOf($"async Task {methodName}", StringComparison.Ordinal);
        }
        if (methodStart == -1)
        {
            return null;
        }

        var braceCount = 0;
        var methodBodyStart = source.IndexOf('{', methodStart);
        if (methodBodyStart == -1)
        {
            return null;
        }

        for (var i = methodBodyStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                braceCount++;
            }
            else if (source[i] == '}')
            {
                braceCount--;
                if (braceCount == 0)
                {
                    return source.Substring(methodBodyStart, i - methodBodyStart + 1);
                }
            }
        }

        return null;
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
