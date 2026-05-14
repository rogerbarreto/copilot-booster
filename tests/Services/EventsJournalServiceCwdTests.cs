using System.Reflection;
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
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void ExtractLatestCwd_HookEventWithInputCwd_ReturnsHookCwd()
    {
        using var reader = ReaderFor(
            SessionStart(@"D:\old"),
            HookStart(@"D:\new"),
            HookStart(@"D:\new"));

        var result = ExtractLatestCwd(reader);

        Assert.Equal(@"D:\new", result);
    }

    [Fact]
    public void ExtractLatestCwd_OnlySessionStart_ReturnsContextCwd()
    {
        using var reader = ReaderFor(SessionStart(@"D:\old"));

        var result = ExtractLatestCwd(reader);

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

        var result = ExtractLatestCwd(reader);

        Assert.Equal(@"D:\cwd3", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json\r\nalso not json")]
    public void ExtractLatestCwd_EmptyOrMalformedFile_ReturnsNull(string content)
    {
        using var reader = new StringReader(content);

        var result = ExtractLatestCwd(reader);

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

        var result = ExtractLatestCwd(reader);

        Assert.Equal(@"D:\valid2", result);
    }

    [Fact]
    public async Task EventsJournalService_RaisesCwdChangedEvent_WhenLatestCwdChangesAcrossWatcherFire()
    {
        const string sessionId = "98845667-7e51-422e-80e9-05becdb6e5e5";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        await File.WriteAllTextAsync(eventsPath, SessionStart(@"D:\old") + Environment.NewLine, Encoding.UTF8, TestContext.Current.CancellationToken);

        using var service = CreateServiceForRoot(this._tempDir);
        service.SuppressEvents = false;

        var changed = new TaskCompletionSource<(string SessionId, string Cwd)>(TaskCreationOptions.RunContinuationsAsynchronously);
        AddLatestCwdChangedHandler(service, (raisedSessionId, cwd) => changed.TrySetResult((raisedSessionId, cwd)));

        service.StartWatching();
        await File.AppendAllTextAsync(eventsPath, HookStart(@"D:\new") + Environment.NewLine, Encoding.UTF8, TestContext.Current.CancellationToken);

        var result = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(@"D:\new", result.Cwd);
    }

    private static string? ExtractLatestCwd(TextReader reader)
    {
        var method = typeof(EventsJournalService).GetMethod(
            "ExtractLatestCwd",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(TextReader)],
            modifiers: null);

        Assert.NotNull(method);
        return (string?)method.Invoke(null, [reader]);
    }

    private static EventsJournalService CreateServiceForRoot(string sessionsRoot)
    {
        var ctor = typeof(EventsJournalService).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: [typeof(string)],
            modifiers: null);

        Assert.NotNull(ctor);
        return (EventsJournalService)ctor.Invoke([sessionsRoot]);
    }

    private static void AddLatestCwdChangedHandler(EventsJournalService service, Action<string, string> handler)
    {
        var evt = typeof(EventsJournalService).GetEvent("LatestCwdChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(evt);
        var addMethod = evt.GetAddMethod(nonPublic: true);
        Assert.NotNull(addMethod);
        addMethod.Invoke(service, [handler]);
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
