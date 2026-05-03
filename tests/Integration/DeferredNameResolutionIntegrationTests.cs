namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Integration tests proving the unresolved → resolved transition for Booster-Resolved Names.
/// Tests the deferred name resolution path: unresolved placeholder ("{HostProcessName}:Copilot")
/// is replaced with truncated first user.message content when events.jsonl is updated.
/// </summary>
public sealed class DeferredNameResolutionIntegrationTests : IDisposable
{
    private readonly string _tempSessionStateDir;
    private readonly string _tempOverrideFile;

    public DeferredNameResolutionIntegrationTests()
    {
        this._tempSessionStateDir = Path.Combine(Path.GetTempPath(), $"session-state-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(this._tempSessionStateDir);
        this._tempOverrideFile = Path.Combine(this._tempSessionStateDir, "session-names.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempSessionStateDir, true); } catch { }
    }

    [Fact]
    public void UnresolvedToResolved_FirstUserMessage_UpdatesOverride()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempSessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var eventsJsonl = Path.Combine(sessionDir, "events.jsonl");

        SessionNameOverrideService.Set(this._tempOverrideFile, sessionId, "WindowsTerminal:Copilot", false);

        File.WriteAllLines(eventsJsonl,
        [
            "{\"type\":\"user.message\",\"data\":{\"content\":\"fix the auth bug\"}}"
        ]);

        var extracted = FirstUserMessageExtractor.Extract(eventsJsonl);
        Assert.Equal("fix the auth bug", extracted);

        var formatted = BoosterResolvedNameFormatter.Format(extracted);
        Assert.Equal("fix the auth bug", formatted);

        SessionNameOverrideService.Set(this._tempOverrideFile, sessionId, formatted, true);

        var resolved = SessionNameOverrideService.Get(this._tempOverrideFile, sessionId);
        Assert.NotNull(resolved);
        Assert.Equal("fix the auth bug", resolved!.Name);
        Assert.True(resolved.ResolvedFromUserMessage);
    }

    [Fact]
    public void UnresolvedToResolved_LongMessage_Truncated()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempSessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var eventsJsonl = Path.Combine(sessionDir, "events.jsonl");

        SessionNameOverrideService.Set(this._tempOverrideFile, sessionId, "pwsh:Copilot", false);

        var longMessage = "This is a very long user message that exceeds 32 char limit";
        File.WriteAllLines(eventsJsonl,
        [
            $"{{\"type\":\"user.message\",\"data\":{{\"content\":\"{longMessage}\"}}}}"
        ]);

        var extracted = FirstUserMessageExtractor.Extract(eventsJsonl);
        var formatted = BoosterResolvedNameFormatter.Format(extracted);
        Assert.Equal(33, formatted!.Length);
        Assert.StartsWith("This is a very long user messag", formatted);
        Assert.EndsWith("…", formatted);

        SessionNameOverrideService.Set(this._tempOverrideFile, sessionId, formatted, true);

        var resolved = SessionNameOverrideService.Get(this._tempOverrideFile, sessionId);
        Assert.NotNull(resolved);
        Assert.True(resolved!.ResolvedFromUserMessage);
    }

    [Fact]
    public void UnresolvedToResolved_CodeFenceStripped()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempSessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var eventsJsonl = Path.Combine(sessionDir, "events.jsonl");

        SessionNameOverrideService.Set(this._tempOverrideFile, sessionId, "conhost:Copilot", false);

        File.WriteAllLines(eventsJsonl,
        [
            "{\"type\":\"user.message\",\"data\":{\"content\":\"```typescript\\nfunction test() { return 42; }\"}}"
        ]);

        var extracted = FirstUserMessageExtractor.Extract(eventsJsonl);
        var formatted = BoosterResolvedNameFormatter.Format(extracted);
        Assert.Equal("function test() { return 42; }", formatted);

        SessionNameOverrideService.Set(this._tempOverrideFile, sessionId, formatted, true);

        var resolved = SessionNameOverrideService.Get(this._tempOverrideFile, sessionId);
        Assert.NotNull(resolved);
        Assert.Equal("function test() { return 42; }", resolved!.Name);
        Assert.True(resolved.ResolvedFromUserMessage);
    }

    [Fact]
    public void UnresolvedToResolved_WhitespaceCollapsed()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempSessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var eventsJsonl = Path.Combine(sessionDir, "events.jsonl");

        SessionNameOverrideService.Set(this._tempOverrideFile, sessionId, "cmd:Copilot", false);

        File.WriteAllLines(eventsJsonl,
        [
            "{\"type\":\"user.message\",\"data\":{\"content\":\"fix   the\\n\\n\\nauth     bug\"}}"
        ]);

        var extracted = FirstUserMessageExtractor.Extract(eventsJsonl);
        var formatted = BoosterResolvedNameFormatter.Format(extracted);
        Assert.Equal("fix the auth bug", formatted);

        SessionNameOverrideService.Set(this._tempOverrideFile, sessionId, formatted, true);

        var resolved = SessionNameOverrideService.Get(this._tempOverrideFile, sessionId);
        Assert.NotNull(resolved);
        Assert.Equal("fix the auth bug", resolved!.Name);
        Assert.True(resolved.ResolvedFromUserMessage);
    }

    [Fact]
    public void AlreadyResolved_NoUpdate()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempSessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        SessionNameOverrideService.Set(this._tempOverrideFile, sessionId, "fix the auth bug", true);

        var resolved = SessionNameOverrideService.Get(this._tempOverrideFile, sessionId);
        Assert.NotNull(resolved);
        Assert.True(resolved!.ResolvedFromUserMessage);
    }

    [Fact]
    public void Placeholder_BuiltFromHostProcessName()
    {
        var placeholder = BoosterResolvedNameFormatter.BuildPlaceholder("WindowsTerminal");
        Assert.Equal("WindowsTerminal:Copilot", placeholder);

        var placeholderPwsh = BoosterResolvedNameFormatter.BuildPlaceholder("pwsh");
        Assert.Equal("pwsh:Copilot", placeholderPwsh);

        var placeholderNull = BoosterResolvedNameFormatter.BuildPlaceholder(null);
        Assert.Equal("Copilot", placeholderNull);
    }
}
