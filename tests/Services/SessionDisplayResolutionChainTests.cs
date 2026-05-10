namespace CopilotBooster.Tests.Services;

/// <summary>
/// Tests for display name resolution chain in SessionService.LoadNamedSessions:
/// alias > workspace.yaml summary > SessionNameOverride > cwd folder > "(no summary)"
/// </summary>
public sealed class SessionDisplayResolutionChainTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _aliasFile;
    private readonly string _overrideFile;

    public SessionDisplayResolutionChainTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
        this._aliasFile = Path.Combine(this._tempDir, "aliases.json");
        this._overrideFile = Path.Combine(this._tempDir, "session-names.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void Resolution_AliasPresent_BeatsAllOthers()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject\nsummary: WS Summary");

        SessionAliasService.SetAlias(this._aliasFile, sessionId, "Alias Name");
        SessionNameOverrideService.Set(this._overrideFile, sessionId, "Override Name", false);

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal("Alias Name", result[0].Summary);
    }

    [Fact]
    public void Resolution_NoAlias_WorkspaceSummaryPresent_BeatsOverrideAndFolder()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject\nsummary: WS Summary");

        SessionNameOverrideService.Set(this._overrideFile, sessionId, "Override Name", false);

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal("WS Summary", result[0].Summary);
    }

    [Fact]
    public void Resolution_NoAlias_NoWorkspaceSummary_OverridePresent_BeatsFolder()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject");

        SessionNameOverrideService.Set(this._overrideFile, sessionId, "Override Name", false);

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal("Override Name", result[0].Summary);
    }

    [Fact]
    public void Resolution_NoAlias_NoSummary_NoOverride_FolderUsed()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject");

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal($"Session {sessionId.Substring(0, 8)}", result[0].Summary);
        Assert.Equal("myproject", result[0].Folder);
    }

    [Fact]
    public void Resolution_NoAlias_NoSummary_NoOverride_NoFolder_NoSummaryLiteral()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: ");

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal($"Session {sessionId.Substring(0, 8)}", result[0].Summary);
    }

    [Fact]
    public void Resolution_OverrideEntry_ResolvedFromUserMessageFalse_StillUsedAsName()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject");

        SessionNameOverrideService.Set(this._overrideFile, sessionId, "Unresolved Override", false);

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal("Unresolved Override", result[0].Summary);
    }

    [Fact]
    public void Resolution_OverrideEntry_ResolvedFromUserMessageTrue_UsedAsName()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject");

        SessionNameOverrideService.Set(this._overrideFile, sessionId, "Resolved Override", true);

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal("Resolved Override", result[0].Summary);
    }

    [Fact]
    public void Resolution_EmptyWorkspaceSummary_TreatedAsAbsent()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject\nsummary: \"\"");

        SessionNameOverrideService.Set(this._overrideFile, sessionId, "Override Name", false);

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal("Override Name", result[0].Summary);
    }

    [Fact]
    public void Resolution_WhitespaceWorkspaceSummary_TreatedAsAbsent()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject\nsummary: \"   \"");

        SessionNameOverrideService.Set(this._overrideFile, sessionId, "Override Name", false);

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal("Override Name", result[0].Summary);
    }

    [Fact]
    public void Resolution_WorkspaceNamePresent_BeatsSummaryAndOverride()
    {
        // Copilot CLI v1.0.44 schema: workspace.yaml may carry `name:` (current title)
        // alongside the legacy `summary:`. The `name:` field is authoritative.
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject\nname: WS Name\nuser_named: false\nsummary: WS Summary");

        SessionNameOverrideService.Set(this._overrideFile, sessionId, "Stale Override", true);

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal("WS Name", result[0].Summary);
    }

    [Fact]
    public void Resolution_WorkspaceUserNamedTrue_NoSummary_BeatsStaleOverride()
    {
        // Reproduces the bug: user renames the session via Copilot CLI, which writes
        // `name: <new>` + `user_named: true` and drops `summary:`. The stored override
        // (booster-resolved from first user message) is now stale and must be ignored.
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject\nname: Install and Verify\nuser_named: true");

        SessionNameOverrideService.Set(this._overrideFile, sessionId, "please install, I need to check", true);

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal("Install and Verify", result[0].Summary);
    }

    [Fact]
    public void Resolution_AliasStillBeatsWorkspaceName()
    {
        // Roger's manual alias must win even when CLI has set `name:`.
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject\nname: WS Name\nuser_named: true");

        SessionAliasService.SetAlias(this._aliasFile, sessionId, "Alias Name");

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal("Alias Name", result[0].Summary);
    }

    [Fact]
    public void Resolution_EmptyWorkspaceName_FallsBackToSummary()
    {
        // Defensive: a present-but-empty `name:` should not block summary fallback.
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sessionId}\ncwd: C:\\myproject\nname: \"\"\nsummary: WS Summary");

        var result = SessionService.LoadNamedSessions(this._tempDir, null, null, this._aliasFile, this._overrideFile);

        Assert.Single(result);
        Assert.Equal("WS Summary", result[0].Summary);
    }
}
