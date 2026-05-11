namespace CopilotBooster.Tests.Services;

/// <summary>
/// Tests for SessionService fallback scenarios, especially for sessions
/// that lack both workspace.yaml summary and session-name-override entries.
/// </summary>
public sealed class SessionServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    /// <summary>
    /// Test 1: When a session has no summary in workspace.yaml and no override entry,
    /// the service must produce a non-empty fallback display name.
    /// This test MUST FAIL on the current code (which returns "").
    /// </summary>
    [Fact]
    public void LoadNamedSessions_NoSummaryNoOverride_FallbackProducesNonEmptyDisplayName()
    {
        // Arrange: a session-state directory with a workspace.yaml lacking
        // 'summary:' and an empty override sidecar.
        var sid = "11111111-2222-3333-4444-555555555555";
        var sessionDir = Path.Combine(this._tempDir, sid);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            "id: 11111111-2222-3333-4444-555555555555\ncwd: C:\\repo\\example\nlast_modified: 2026-05-09T10:58:00Z\n");
        var overrideFile = Path.Combine(this._tempDir, "session-name-overrides.json");
        File.WriteAllText(overrideFile, "{}");

        // Act
        var sessions = SessionService.LoadNamedSessions(this._tempDir, overrideFile: overrideFile);

        // Assert: today this fails because Summary == "". Post-fix, it must be
        // a non-empty fallback display name (e.g. "Session 11111111", "(unnamed)", etc.)
        var session = Assert.Single(sessions, s => s.Id == sid);
        Assert.False(string.IsNullOrWhiteSpace(session.Summary),
            "Summary must never render empty when both workspace summary and override are missing.");
    }

    /// <summary>
    /// Test 2: When a session has a summary in workspace.yaml,
    /// that summary must win over any override.
    /// This test MUST PASS on current code AND post-fix (regression guard).
    /// </summary>
    [Fact]
    public void LoadNamedSessions_WorkspaceSummary_WinsOverOverride()
    {
        // Arrange: workspace.yaml with summary, override with different value
        var sid = "22222222-3333-4444-5555-666666666666";
        var sessionDir = Path.Combine(this._tempDir, sid);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sid}\ncwd: C:\\repo\\test\nsummary: Workspace Summary\n");

        var overrideFile = Path.Combine(this._tempDir, "session-name-overrides.json");
        File.WriteAllText(overrideFile,
            $$"""
            {
              "{{sid}}": {
                "Name": "Override Name",
                "ResolvedFromUserMessage": true
              }
            }
            """);

        // Act
        var sessions = SessionService.LoadNamedSessions(this._tempDir, overrideFile: overrideFile);

        // Assert: workspace summary must win
        var session = Assert.Single(sessions, s => s.Id == sid);
        Assert.Equal("Workspace Summary", session.Summary);
    }

    /// <summary>
    /// Test 3: When a session has no workspace summary but HAS an override,
    /// the override must be used as the display name.
    /// This test should PASS on current code AND post-fix.
    /// </summary>
    [Fact]
    public void LoadNamedSessions_NoSummary_UsesOverride()
    {
        // Arrange: no summary in workspace.yaml, but override exists
        var sid = "33333333-4444-5555-6666-777777777777";
        var sessionDir = Path.Combine(this._tempDir, sid);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sid}\ncwd: C:\\repo\\test\n");

        var overrideFile = Path.Combine(this._tempDir, "session-name-overrides.json");
        File.WriteAllText(overrideFile,
            $$"""
            {
              "{{sid}}": {
                "Name": "Override Display Name",
                "ResolvedFromUserMessage": true
              }
            }
            """);

        // Act
        var sessions = SessionService.LoadNamedSessions(this._tempDir, overrideFile: overrideFile);

        // Assert: override name must be used
        var session = Assert.Single(sessions, s => s.Id == sid);
        Assert.Equal("Override Display Name", session.Summary);
    }

    /// <summary>
    /// Test 4: Placeholder upgrade flow simulation.
    /// When a session starts with a placeholder override and later receives
    /// a first user message, the override should be upgraded to the formatted message.
    /// This test exercises the placeholder → resolved transition.
    /// </summary>
    [Fact]
    public void LoadNamedSessions_PlaceholderUpgrade_UsesResolvedMessage()
    {
        // Arrange: Session with placeholder override initially
        var sid = "44444444-5555-6666-7777-888888888888";
        var sessionDir = Path.Combine(this._tempDir, sid);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            $"id: {sid}\ncwd: C:\\repo\\test\n");

        var overrideFile = Path.Combine(this._tempDir, "session-name-overrides.json");

        // Initial state: placeholder
        var overrides = new Dictionary<string, SessionNameOverride>(StringComparer.OrdinalIgnoreCase)
        {
            [sid] = new SessionNameOverride("cli placeholder", false)
        };
        SessionNameOverrideService.Save(overrideFile, overrides);

        // Verify placeholder is used initially
        var sessions1 = SessionService.LoadNamedSessions(this._tempDir, overrideFile: overrideFile);
        var session1 = Assert.Single(sessions1, s => s.Id == sid);
        Assert.Equal("cli placeholder", session1.Summary);

        // Simulate events.jsonl with first user message
        File.WriteAllText(
            Path.Combine(sessionDir, "events.jsonl"),
            """
            {"type":"user.message","data":{"content":"What is the architecture of this project?"}}
            """);

        // Simulate the upgrade: extract message and update override
        var firstMessage = FirstUserMessageExtractor.Extract(Path.Combine(sessionDir, "events.jsonl"));
        Assert.NotNull(firstMessage);

        var formatted = BoosterResolvedNameFormatter.Format(firstMessage);
        Assert.NotNull(formatted);

        overrides = SessionNameOverrideService.Load(overrideFile);
        overrides[sid] = new SessionNameOverride(formatted, true);
        SessionNameOverrideService.Save(overrideFile, overrides);

        // Act: reload sessions after upgrade
        var sessions2 = SessionService.LoadNamedSessions(this._tempDir, overrideFile: overrideFile);

        // Assert: upgraded override is now used
        var session2 = Assert.Single(sessions2, s => s.Id == sid);
        Assert.Equal(formatted, session2.Summary);
        Assert.NotEqual("cli placeholder", session2.Summary);
    }
}
