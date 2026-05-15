using System.Text;

public sealed class EventsJournalServiceCwdTests : IDisposable
{
    private readonly string _tempDir;

    public EventsJournalServiceCwdTests()
    {
        this._tempDir = Path.Combine(AppContext.BaseDirectory, "EventsJournalServiceCwdTests", Path.GetRandomFileName());
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
    public void ExtractLatestCwd_HookEventWithInputCwd_ReturnsHookCwd()
    {
        using var reader = ReaderFor(
            SessionStart(@"D:\old"),
            HookStart(@"D:\new"),
            HookStart(@"D:\new"));

        var result = EventsJournalService.ExtractLatestCwd(reader);

        Assert.Equal(@"D:\new", result);
    }

    [Fact]
    public void ExtractLatestCwd_OnlySessionStart_ReturnsContextCwd()
    {
        using var reader = ReaderFor(SessionStart(@"D:\old"));

        var result = EventsJournalService.ExtractLatestCwd(reader);

        Assert.Equal(@"D:\old", result);
    }

    [Fact]
    public void ExtractLatestCwd_MultipleHooksWithDifferentCwds_ReturnsLatest()
    {
        using var reader = ReaderFor(
            SessionStart(@"D:\old"),
            HookStart(@"D:\cwd1"),
            HookStart(@"D:\cwd2"),
            HookStart(@"D:\cwd3"));

        var result = EventsJournalService.ExtractLatestCwd(reader);

        Assert.Equal(@"D:\cwd3", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json\r\nalso not json")]
    public void ExtractLatestCwd_EmptyOrMalformedFile_ReturnsNull(string content)
    {
        using var reader = new StringReader(content);

        var result = EventsJournalService.ExtractLatestCwd(reader);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractLatestCwd_TruncatedFinalLine_StillReturnsLatestValidCwd()
    {
        using var reader = ReaderForRaw(
            SessionStart(@"D:\old") + Environment.NewLine +
            HookStart(@"D:\valid1") + Environment.NewLine +
            HookStart(@"D:\valid2") + Environment.NewLine +
            "{\"type\":\"hook.start\",\"data\":{");

        var result = EventsJournalService.ExtractLatestCwd(reader);

        Assert.Equal(@"D:\valid2", result);
    }

    [Fact]
    public void TryGetLatestCwd_AfterReadAndCacheLatestCwd_ReturnsLatestCwd()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(
            Path.Combine(sessionDir, "events.jsonl"),
            string.Join(Environment.NewLine,
                SessionStart(@"D:\old"),
                HookStart(@"D:\new")) + Environment.NewLine,
            Encoding.UTF8);

        using var service = new EventsJournalService(this._tempDir);
        service.PrimeCache([sessionId]);

        Assert.True(service.TryGetLatestCwd(sessionId, out var cwd));
        Assert.Equal(@"D:\new", cwd);
    }

    [Fact]
    public async Task EventsJournalService_RaisesCwdChangedEvent_WhenLatestCwdChangesAcrossWatcherFire()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        await File.WriteAllTextAsync(
            eventsPath,
            SessionStart(@"D:\old") + Environment.NewLine,
            Encoding.UTF8,
            TestContext.Current.CancellationToken);

        using var service = new EventsJournalService(this._tempDir);
        service.PrimeCache([sessionId]);
        service.SuppressEvents = false;

        var changed = new TaskCompletionSource<(string SessionId, string Cwd)>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.LatestCwdChanged += (raisedSessionId, cwd) => changed.TrySetResult((raisedSessionId, cwd));

        service.StartWatching();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        using (var writer = new StreamWriter(eventsPath, append: true, Encoding.UTF8))
        {
            await writer.WriteLineAsync(HookStart(@"D:\new"));
        }

        var result = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(@"D:\new", result.Cwd);
    }

    private static StringReader ReaderFor(params string[] lines)
    {
        return ReaderForRaw(string.Join(Environment.NewLine, lines));
    }

    private static StringReader ReaderForRaw(string content)
    {
        return new StringReader(content);
    }

    private static string SessionStart(string cwd)
    {
        return "{\"type\":\"session.start\",\"data\":{\"sessionId\":\"98845667-7e51-422e-80e9-05becdb6e5e5\",\"context\":{\"cwd\":\"" + Escape(cwd) + "\"}}}";
    }

    private static string HookStart(string cwd)
    {
        return "{\"type\":\"hook.start\",\"data\":{\"hookInvocationId\":\"hook-1\",\"hookType\":\"preToolUse\",\"input\":{\"sessionId\":\"98845667-7e51-422e-80e9-05becdb6e5e5\",\"cwd\":\"" + Escape(cwd) + "\",\"toolCalls\":[]}}}";
    }

    private static string Escape(string value)
    {
        return value.Replace(@"\", @"\\", StringComparison.Ordinal);
    }
}
