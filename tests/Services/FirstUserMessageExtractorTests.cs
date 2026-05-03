namespace CopilotBooster.Tests.Services;

public sealed class FirstUserMessageExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public FirstUserMessageExtractorTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void Extract_FileMissing_ReturnsNull()
    {
        var eventsFile = Path.Combine(this._tempDir, "missing.jsonl");
        var result = FirstUserMessageExtractor.Extract(eventsFile);
        Assert.Null(result);
    }

    [Fact]
    public void Extract_EmptyFile_ReturnsNull()
    {
        var eventsFile = Path.Combine(this._tempDir, "empty.jsonl");
        File.WriteAllText(eventsFile, "");
        var result = FirstUserMessageExtractor.Extract(eventsFile);
        Assert.Null(result);
    }

    [Fact]
    public void Extract_NoUserMessageEvents_ReturnsNull()
    {
        var eventsFile = Path.Combine(this._tempDir, "no-user-message.jsonl");
        File.WriteAllLines(eventsFile,
        [
            "{\"type\":\"assistant.message\",\"data\":{\"content\":\"hello\"}}",
            "{\"type\":\"tool.execution\",\"data\":{\"name\":\"bash\"}}"
        ]);
        var result = FirstUserMessageExtractor.Extract(eventsFile);
        Assert.Null(result);
    }

    [Fact]
    public void Extract_FirstEventIsUserMessage_ReturnsContent()
    {
        var eventsFile = Path.Combine(this._tempDir, "first-user.jsonl");
        File.WriteAllLines(eventsFile,
        [
            "{\"type\":\"user.message\",\"data\":{\"content\":\"hello world\"}}",
            "{\"type\":\"assistant.message\",\"data\":{\"content\":\"hi\"}}"
        ]);
        var result = FirstUserMessageExtractor.Extract(eventsFile);
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Extract_UserMessageNotFirst_ReturnsFirstUserMessage()
    {
        var eventsFile = Path.Combine(this._tempDir, "user-second.jsonl");
        File.WriteAllLines(eventsFile,
        [
            "{\"type\":\"assistant.message\",\"data\":{\"content\":\"hi\"}}",
            "{\"type\":\"tool.execution\",\"data\":{\"name\":\"bash\"}}",
            "{\"type\":\"user.message\",\"data\":{\"content\":\"first user message\"}}",
            "{\"type\":\"user.message\",\"data\":{\"content\":\"second user message\"}}"
        ]);
        var result = FirstUserMessageExtractor.Extract(eventsFile);
        Assert.Equal("first user message", result);
    }

    [Fact]
    public void Extract_MalformedJsonFollowedByValid_ReturnsValidContent()
    {
        var eventsFile = Path.Combine(this._tempDir, "malformed.jsonl");
        File.WriteAllLines(eventsFile,
        [
            "{\"type\":\"invalid\",\"data",
            "{\"type\":\"user.message\",\"data\":{\"content\":\"valid content\"}}"
        ]);
        var result = FirstUserMessageExtractor.Extract(eventsFile);
        Assert.Equal("valid content", result);
    }

    [Fact]
    public void Extract_MultiLineContent_ReturnsUnescapedContent()
    {
        var eventsFile = Path.Combine(this._tempDir, "multiline.jsonl");
        File.WriteAllText(eventsFile, "{\"type\":\"user.message\",\"data\":{\"content\":\"line one\\nline two\\nline three\"}}");
        var result = FirstUserMessageExtractor.Extract(eventsFile);
        Assert.Equal("line one\nline two\nline three", result);
    }

    [Fact]
    public void Extract_TrailingPartialLine_ReturnsNull()
    {
        var eventsFile = Path.Combine(this._tempDir, "partial.jsonl");
        File.WriteAllText(eventsFile, "{\"type\":\"user.message\",\"data\":{\"con");
        var result = FirstUserMessageExtractor.Extract(eventsFile);
        Assert.Null(result);
    }

    [Fact]
    public void Extract_TwoUserMessages_ReturnsFirstOnly()
    {
        var eventsFile = Path.Combine(this._tempDir, "two-users.jsonl");
        File.WriteAllLines(eventsFile,
        [
            "{\"type\":\"user.message\",\"data\":{\"content\":\"first message\"}}",
            "{\"type\":\"user.message\",\"data\":{\"content\":\"second message\"}}"
        ]);
        var result = FirstUserMessageExtractor.Extract(eventsFile);
        Assert.Equal("first message", result);
    }
}
