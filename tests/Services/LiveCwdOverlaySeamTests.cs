using System.Text;

public sealed class LiveCwdOverlaySeamTests : IDisposable
{
    private readonly string _tempDir;

    public LiveCwdOverlaySeamTests()
    {
        this._tempDir = Path.Combine(AppContext.BaseDirectory, "LiveCwdOverlaySeamTests", Path.GetRandomFileName());
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
    public void ApplyLiveCwdOverlay_WhenLiveCwdDiffersFromWorkspaceYaml_OverlaysLiveCwd()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
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
                HookStart(sessionId, @"D:\new")) + Environment.NewLine,
            Encoding.UTF8);

        var sessionStateFile = Path.Combine(this._tempDir, "session-state.json");
        var aliasFile = Path.Combine(this._tempDir, "aliases.json");
        var overrideFile = Path.Combine(this._tempDir, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        using var eventsJournal = new EventsJournalService(this._tempDir);
        eventsJournal.PrimeCache([sessionId]);

        Assert.True(eventsJournal.TryGetLatestCwd(sessionId, out var liveCwd));
        Assert.Equal(@"D:\new", liveCwd);

        var freshSessions = SessionService.LoadNamedSessions(
            this._tempDir,
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

    [Fact]
    public void ApplyLiveCwdOverlay_WhenLiveCwdMatchesWorkspaceYaml_NoChange()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\same",
                "name: TestSession") + Environment.NewLine,
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(sessionDir, "events.jsonl"),
            string.Join(Environment.NewLine,
                SessionStart(sessionId, @"D:\same"),
                HookStart(sessionId, @"D:\same")) + Environment.NewLine,
            Encoding.UTF8);

        var sessionStateFile = Path.Combine(this._tempDir, "session-state.json");
        var aliasFile = Path.Combine(this._tempDir, "aliases.json");
        var overrideFile = Path.Combine(this._tempDir, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        using var eventsJournal = new EventsJournalService(this._tempDir);
        eventsJournal.PrimeCache([sessionId]);

        var freshSessions = SessionService.LoadNamedSessions(
            this._tempDir,
            pidRegistryFile: null,
            sessionStateFile: sessionStateFile,
            aliasFile: aliasFile,
            overrideFile: overrideFile);

        var session = Assert.Single(freshSessions);
        Assert.Equal(@"D:\same", session.Cwd);

        EventsJournalService.ApplyLiveCwdOverlay(freshSessions, eventsJournal);

        Assert.Equal(@"D:\same", session.Cwd);
        Assert.Equal("same", session.Folder);
    }

    [Fact]
    public void ApplyLiveCwdOverlay_WhenSessionHasNoLiveCwd_NoChange()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
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
            "",
            Encoding.UTF8);

        var sessionStateFile = Path.Combine(this._tempDir, "session-state.json");
        var aliasFile = Path.Combine(this._tempDir, "aliases.json");
        var overrideFile = Path.Combine(this._tempDir, "overrides.json");
        File.WriteAllText(sessionStateFile, "{}");
        File.WriteAllText(aliasFile, "{}");
        File.WriteAllText(overrideFile, "{}");

        using var eventsJournal = new EventsJournalService(this._tempDir);
        eventsJournal.PrimeCache([sessionId]);

        Assert.False(eventsJournal.TryGetLatestCwd(sessionId, out _));

        var freshSessions = SessionService.LoadNamedSessions(
            this._tempDir,
            pidRegistryFile: null,
            sessionStateFile: sessionStateFile,
            aliasFile: aliasFile,
            overrideFile: overrideFile);

        var session = Assert.Single(freshSessions);
        Assert.Equal(@"D:\old", session.Cwd);

        EventsJournalService.ApplyLiveCwdOverlay(freshSessions, eventsJournal);

        Assert.Equal(@"D:\old", session.Cwd);
        Assert.Equal("old", session.Folder);
    }

    [Fact]
    public void ProductionCallsite_MainFormOnDebouncedRefreshAsync_CallsApplyLiveCwdOverlay()
    {
        var mainFormPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Forms", "MainForm.cs");

        if (!File.Exists(mainFormPath))
        {
            mainFormPath = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "..", "..", "..", "..", "src", "Forms", "MainForm.cs"));
        }

        Assert.True(File.Exists(mainFormPath), $"MainForm.cs not found at {mainFormPath}");

        var mainFormContent = File.ReadAllText(mainFormPath);

        Assert.Contains("ApplyLiveCwdOverlay", mainFormContent);

        var debounceMethod = ExtractMethod(mainFormContent, "OnDebouncedRefreshAsync");
        Assert.NotNull(debounceMethod);
        Assert.Contains("ApplyLiveCwdOverlay", debounceMethod);
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
