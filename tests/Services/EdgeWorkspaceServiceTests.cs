namespace CopilotBooster.Tests.Services;

public class EdgeWorkspaceServiceTests
{
    // ── ExtractSessionId ──────────────────────────────────────────────

    [Fact]
    public void ExtractSessionId_NamedTitle_ReturnsGuid()
    {
        var result = EdgeWorkspaceService.ExtractSessionId(
            "CB Session [My Session] - [abc-123-def] - Microsoft\u200b Edge");
        Assert.Equal("abc-123-def", result);
    }

    [Fact]
    public void ExtractSessionId_UnnamedTitle_ReturnsGuid()
    {
        var result = EdgeWorkspaceService.ExtractSessionId(
            "CB Session [abc-123-def] - Microsoft\u200b Edge");
        Assert.Equal("abc-123-def", result);
    }

    [Fact]
    public void ExtractSessionId_NoPrefix_ReturnsNull()
    {
        var result = EdgeWorkspaceService.ExtractSessionId("Some Other Tab");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractSessionId_NoBrackets_ReturnsNull()
    {
        var result = EdgeWorkspaceService.ExtractSessionId("CB Session no brackets");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractSessionId_CaseInsensitive_ReturnsGuid()
    {
        var result = EdgeWorkspaceService.ExtractSessionId(
            "cb session [my-guid-123]");
        Assert.Equal("my-guid-123", result);
    }

    [Fact]
    public void ExtractSessionId_MultipleBrackets_ReturnsLastBracketContent()
    {
        var result = EdgeWorkspaceService.ExtractSessionId(
            "CB Session [Name With [Nested]] - [actual-id]");
        Assert.Equal("actual-id", result);
    }

    // ── BuildSessionUrl ───────────────────────────────────────────────

    [Fact]
    public void BuildSessionUrl_WithoutName_ReturnsUrlWithIdOnly()
    {
        var result = EdgeWorkspaceService.BuildSessionUrl(
            @"C:\app\session.html", "abc-123", null);
        Assert.Equal("file:///C:/app/session.html#abc-123", result);
    }

    [Fact]
    public void BuildSessionUrl_WithName_ReturnsUrlWithEncodedName()
    {
        var result = EdgeWorkspaceService.BuildSessionUrl(
            @"C:\app\session.html", "abc-123", "My Session");
        Assert.Equal("file:///C:/app/session.html#abc-123/My%20Session", result);
    }

    [Fact]
    public void BuildSessionUrl_WithSpecialChars_UrlEncodesName()
    {
        var result = EdgeWorkspaceService.BuildSessionUrl(
            @"C:\app\session.html", "abc-123", "PR #42 (test/fix)");
        Assert.Contains("abc-123/", result);
        Assert.Contains("PR%20%2342%20%28test%2Ffix%29", result);
    }

    [Fact]
    public void BuildSessionUrl_EmptyName_ReturnsUrlWithIdOnly()
    {
        var result = EdgeWorkspaceService.BuildSessionUrl(
            @"C:\app\session.html", "abc-123", "");
        Assert.Equal("file:///C:/app/session.html#abc-123", result);
    }

    [Fact]
    public void BuildSessionUrl_WhitespaceName_ReturnsUrlWithIdOnly()
    {
        var result = EdgeWorkspaceService.BuildSessionUrl(
            @"C:\app\session.html", "abc-123", "   ");
        Assert.Equal("file:///C:/app/session.html#abc-123", result);
    }

    [Fact]
    public void BuildSessionUrl_ForwardSlashPath_PreservesSlashes()
    {
        var result = EdgeWorkspaceService.BuildSessionUrl(
            "C:/app/session.html", "id-1", null);
        Assert.Equal("file:///C:/app/session.html#id-1", result);
    }
}
