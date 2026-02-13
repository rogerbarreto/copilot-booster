public sealed class SearchSessionsTests
{
    private static List<NamedSession> CreateTestSessions() =>
    [
        new NamedSession { Id = "abc-123", Cwd = @"C:\projects\webapp", Folder = "webapp", Summary = "Fix login bug", LastModified = DateTime.Now },
        new NamedSession { Id = "def-456", Cwd = @"C:\projects\api", Folder = "api", Summary = "Add REST endpoints", LastModified = DateTime.Now.AddMinutes(-10) },
        new NamedSession { Id = "ghi-789", Cwd = @"C:\projects\login-service", Folder = "login-service", Summary = "Refactor auth", LastModified = DateTime.Now.AddMinutes(-20) },
    ];

    [Fact]
    public void SearchSessions_EmptyQuery_ReturnsAll()
    {
        var sessions = CreateTestSessions();
        var result = SessionService.SearchSessions(sessions, "");
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SearchSessions_NullQuery_ReturnsAll()
    {
        var sessions = CreateTestSessions();
        var result = SessionService.SearchSessions(sessions, null!);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SearchSessions_WhitespaceQuery_ReturnsAll()
    {
        var sessions = CreateTestSessions();
        var result = SessionService.SearchSessions(sessions, "   ");
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SearchSessions_MatchesSummary_ReturnsTitleMatches()
    {
        var sessions = CreateTestSessions();
        var result = SessionService.SearchSessions(sessions, "login");

        Assert.Equal(2, result.Count);
        Assert.Equal("abc-123", result[0].Id); // "Fix login bug" - summary match
        Assert.Equal("ghi-789", result[1].Id); // "login-service" - folder match
    }

    [Fact]
    public void SearchSessions_MatchesCwdOnly_ReturnsMetadataMatch()
    {
        var sessions = CreateTestSessions();
        var result = SessionService.SearchSessions(sessions, "webapp");

        // "webapp" appears in Folder (title match) and Cwd
        Assert.Single(result);
        Assert.Equal("abc-123", result[0].Id);
    }

    [Fact]
    public void SearchSessions_MatchesIdOnly_ReturnsMetadataMatch()
    {
        var sessions = CreateTestSessions();
        var result = SessionService.SearchSessions(sessions, "def-456");

        Assert.Single(result);
        Assert.Equal("def-456", result[0].Id);
    }

    [Fact]
    public void SearchSessions_CaseInsensitive()
    {
        var sessions = CreateTestSessions();
        var result = SessionService.SearchSessions(sessions, "REST");

        Assert.Single(result);
        Assert.Equal("def-456", result[0].Id);
    }

    [Fact]
    public void SearchSessions_NoMatch_ReturnsEmpty()
    {
        var sessions = CreateTestSessions();
        var result = SessionService.SearchSessions(sessions, "zzzzz");
        Assert.Empty(result);
    }

    [Fact]
    public void SearchSessions_TitleMatchesBeforeMetadata()
    {
        var sessions = new List<NamedSession>
        {
            new() { Id = "session-api", Cwd = @"C:\code", Folder = "code", Summary = "Database work" },
            new() { Id = "session-db", Cwd = @"C:\api-project", Folder = "api-project", Summary = "Fix api tests" },
        };

        var result = SessionService.SearchSessions(sessions, "api");

        Assert.Equal(2, result.Count);
        // Title match comes first
        Assert.Equal("session-db", result[0].Id);
        // Metadata (id) match comes second
        Assert.Equal("session-api", result[1].Id);
    }

    [Fact]
    public void SearchSessions_EmptyList_ReturnsEmpty()
    {
        var result = SessionService.SearchSessions([], "test");
        Assert.Empty(result);
    }
}
