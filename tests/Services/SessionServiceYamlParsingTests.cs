using System.Text;

public sealed class SessionServiceYamlParsingTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceYamlParsingTests()
    {
        this._tempDir = Path.Combine(AppContext.BaseDirectory, "SessionServiceYamlParsingTests", Path.GetRandomFileName());
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

    /// <summary>
    /// BUG B (BLOCKING): workspace.yaml name parser doesn't handle nested quotes.
    /// Copilot CLI wrote: name: '"Hosted Agents V2 Questions"' (single quotes wrapping double-quoted text).
    /// Current parser: line[5..].Trim().Trim('"') strips one layer, leaving: 'Hosted Agents V2 Questions'
    /// EXPECTED: Parser should strip BOTH quote wrappers, yielding: Hosted Agents V2 Questions
    /// ACTUAL (bug): UI displays literal single quotes around the name.
    /// </summary>
    [Fact]
    public void LoadNamedSessions_NameFieldWithSingleQuotedDoubleQuotedValue_StripsAllQuoteWrappers()
    {
        const string sessionId = "d1277063-ce93-44b0-95cc-7deee25b676a";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        // Simulate exactly what Copilot CLI wrote
        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\repo\\work\\agent-framework",
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

        // EXPECTED: Summary should have NO quotes (neither single nor double)
        // ACTUAL (bug): Summary will be 'Hosted Agents V2 Questions' with single quotes intact
        Assert.Equal("Hosted Agents V2 Questions", session.Summary);
    }

    /// <summary>
    /// Edge case: Double-quoted value (normal case) should work correctly.
    /// </summary>
    [Fact]
    public void LoadNamedSessions_NameFieldWithDoubleQuotes_StripsQuotes()
    {
        const string sessionId = "test-session-1";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\repo\\work",
                "name: \"Normal Session Name\"") + Environment.NewLine,
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
        Assert.Equal("Normal Session Name", session.Summary);
    }

    /// <summary>
    /// Edge case: Single-quoted value should strip single quotes.
    /// </summary>
    [Fact]
    public void LoadNamedSessions_NameFieldWithSingleQuotes_StripsQuotes()
    {
        const string sessionId = "test-session-2";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\repo\\work",
                "name: 'Single Quoted Name'") + Environment.NewLine,
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
        Assert.Equal("Single Quoted Name", session.Summary);
    }

    /// <summary>
    /// Edge case: summary field should also handle nested quotes.
    /// </summary>
    [Fact]
    public void LoadNamedSessions_SummaryFieldWithSingleQuotedDoubleQuotedValue_StripsAllQuoteWrappers()
    {
        const string sessionId = "test-session-3";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        File.WriteAllText(
            Path.Combine(sessionDir, "workspace.yaml"),
            string.Join(Environment.NewLine,
                $"id: {sessionId}",
                "cwd: D:\\repo\\work",
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

}
