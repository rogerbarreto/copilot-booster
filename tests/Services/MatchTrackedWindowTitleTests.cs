namespace CopilotBooster.Tests.Services;

public class MatchTrackedWindowTitleTests
{
    [Fact]
    public void CopilotCliPattern_ReturnsSessionId()
    {
        var result = WindowFocusService.MatchTrackedWindowTitle("Copilot CLI - abc-123", null);

        Assert.NotNull(result);
        Assert.Equal("abc-123", result.Value.SessionId);
        Assert.Equal("Copilot CLI", result.Value.Label);
    }

    [Fact]
    public void TerminalPattern_ReturnsSessionId()
    {
        var result = WindowFocusService.MatchTrackedWindowTitle("Terminal - abc-123", null);

        Assert.NotNull(result);
        Assert.Equal("abc-123", result.Value.SessionId);
        Assert.Equal("Terminal", result.Value.Label);
    }

    [Fact]
    public void TerminalNumberPattern_ReturnsSessionId()
    {
        var result = WindowFocusService.MatchTrackedWindowTitle("Terminal #2 - abc-123", null);

        Assert.NotNull(result);
        Assert.Equal("abc-123", result.Value.SessionId);
        Assert.Equal("Terminal #2", result.Value.Label);
    }

    [Fact]
    public void SessionSummaryMatch_ReturnsSessionId()
    {
        var summaries = new Dictionary<string, string> { { "Fix auth bug", "session-1" } };

        var result = WindowFocusService.MatchTrackedWindowTitle("Fix auth bug", summaries);

        Assert.NotNull(result);
        Assert.Equal("session-1", result.Value.SessionId);
        Assert.Equal("Copilot CLI", result.Value.Label);
    }

    [Fact]
    public void SessionSummaryWithEmojiPrefix_MatchesAfterStripping()
    {
        var summaries = new Dictionary<string, string> { { "Fix auth bug", "session-1" } };

        var result = WindowFocusService.MatchTrackedWindowTitle("🤖 Fix auth bug", summaries);

        Assert.NotNull(result);
        Assert.Equal("session-1", result.Value.SessionId);
        Assert.Equal("Copilot CLI", result.Value.Label);
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        var result = WindowFocusService.MatchTrackedWindowTitle("Notepad", null);

        Assert.Null(result);
    }

    [Fact]
    public void EmptyTitle_ReturnsNull()
    {
        var result = WindowFocusService.MatchTrackedWindowTitle("", null);

        Assert.Null(result);
    }

    [Fact]
    public void NullSummaries_NoSummaryMatching()
    {
        var result = WindowFocusService.MatchTrackedWindowTitle("Fix auth bug", null);

        Assert.Null(result);
    }

    [Fact]
    public void CaseInsensitive_CopilotCliPattern()
    {
        var result = WindowFocusService.MatchTrackedWindowTitle("copilot cli - ABC", null);

        Assert.NotNull(result);
        Assert.Equal("ABC", result.Value.SessionId);
        Assert.Equal("Copilot CLI", result.Value.Label);
    }

    [Fact]
    public void TerminalNumberWithoutDash_ReturnsNull()
    {
        var result = WindowFocusService.MatchTrackedWindowTitle("Terminal #2abc", null);

        Assert.Null(result);
    }
}
