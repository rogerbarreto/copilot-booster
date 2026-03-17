public sealed class UpdateServiceTests
{
    private const string RealisticTagsHtml = """
        <!DOCTYPE html>
        <html>
        <body>
        <div class="Box-row">
          <a href="/rogerbarreto/copilot-booster/releases/tag/v0.19.1">v0.19.1</a>
          <span class="ml-1">Latest</span>
        </div>
        <div class="Box-row">
          <a href="/rogerbarreto/copilot-booster/releases/tag/v0.19.0">v0.19.0</a>
        </div>
        <div class="Box-row">
          <a href="/rogerbarreto/copilot-booster/releases/tag/v0.18.0">v0.18.0</a>
        </div>
        </body>
        </html>
        """;

    // ── Version extraction from HTML ──────────────────────────────────────

    [Fact]
    public void ParseUpdate_RealisticHtml_ExtractsFirstTag()
    {
        var result = UpdateService.ParseUpdate(RealisticTagsHtml, new Version(0, 1, 0));

        Assert.NotNull(result);
        Assert.Equal(new Version(0, 19, 1), result.Version);
        Assert.Equal("v0.19.1", result.TagName);
        Assert.Equal(
            "https://github.com/rogerbarreto/copilot-booster/releases/download/v0.19.1/CopilotBooster-Setup.exe",
            result.InstallerUrl);
    }

    [Fact]
    public void ParseUpdate_SingleTag_ExtractsVersion()
    {
        var html = """<a href="/rogerbarreto/copilot-booster/releases/tag/v1.2.3">v1.2.3</a>""";

        var result = UpdateService.ParseUpdate(html, new Version(0, 0, 1));

        Assert.NotNull(result);
        Assert.Equal(new Version(1, 2, 3), result.Version);
        Assert.Equal("v1.2.3", result.TagName);
    }

    [Fact]
    public void ParseUpdate_FourPartVersion_ExtractsCorrectly()
    {
        var html = """<a href="/rogerbarreto/copilot-booster/releases/tag/v2.0.1.5">v2.0.1.5</a>""";

        var result = UpdateService.ParseUpdate(html, new Version(0, 0, 1));

        Assert.NotNull(result);
        Assert.Equal(new Version(2, 0, 1, 5), result.Version);
    }

    // ── Version comparison logic ──────────────────────────────────────────

    [Fact]
    public void ParseUpdate_SameVersion_ReturnsNull()
    {
        var html = """<a href="/rogerbarreto/copilot-booster/releases/tag/v0.19.1">v0.19.1</a>""";

        var result = UpdateService.ParseUpdate(html, new Version(0, 19, 1));

        Assert.Null(result);
    }

    [Fact]
    public void ParseUpdate_NewerVersionAvailable_ReturnsUpdateInfo()
    {
        var html = """<a href="/rogerbarreto/copilot-booster/releases/tag/v0.20.0">v0.20.0</a>""";

        var result = UpdateService.ParseUpdate(html, new Version(0, 19, 1));

        Assert.NotNull(result);
        Assert.Equal(new Version(0, 20, 0), result.Version);
    }

    [Fact]
    public void ParseUpdate_OlderVersionOnGitHub_ReturnsNull()
    {
        var html = """<a href="/rogerbarreto/copilot-booster/releases/tag/v0.18.0">v0.18.0</a>""";

        var result = UpdateService.ParseUpdate(html, new Version(0, 19, 1));

        Assert.Null(result);
    }

    [Fact]
    public void ParseUpdate_MinorBump_DetectedAsUpdate()
    {
        var html = """<a href="/rogerbarreto/copilot-booster/releases/tag/v0.19.2">v0.19.2</a>""";

        var result = UpdateService.ParseUpdate(html, new Version(0, 19, 1));

        Assert.NotNull(result);
        Assert.Equal(new Version(0, 19, 2), result.Version);
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void ParseUpdate_EmptyHtml_ReturnsNull()
    {
        var result = UpdateService.ParseUpdate(string.Empty, new Version(0, 19, 1));

        Assert.Null(result);
    }

    [Fact]
    public void ParseUpdate_NoMatchingTags_ReturnsNull()
    {
        var html = """
            <html><body>
            <a href="/some-other/repo/releases/tag/v1.0.0">v1.0.0</a>
            </body></html>
            """;

        var result = UpdateService.ParseUpdate(html, new Version(0, 1, 0));

        Assert.Null(result);
    }

    [Fact]
    public void ParseUpdate_MalformedHtml_ReturnsNull()
    {
        var html = "<div><<<<broken>>></div>not valid html at all %%$#@";

        var result = UpdateService.ParseUpdate(html, new Version(0, 1, 0));

        Assert.Null(result);
    }

    [Fact]
    public void ParseUpdate_TagWithInvalidVersion_ReturnsNull()
    {
        var html = """<a href="/rogerbarreto/copilot-booster/releases/tag/vnot.a.version">vnot.a.version</a>""";

        var result = UpdateService.ParseUpdate(html, new Version(0, 1, 0));

        Assert.Null(result);
    }

    [Fact]
    public void ParseUpdate_InstallerUrlFormat_IsCorrect()
    {
        var html = """<a href="/rogerbarreto/copilot-booster/releases/tag/v1.0.0">v1.0.0</a>""";

        var result = UpdateService.ParseUpdate(html, new Version(0, 0, 1));

        Assert.NotNull(result);
        Assert.Equal(
            "https://github.com/rogerbarreto/copilot-booster/releases/download/v1.0.0/CopilotBooster-Setup.exe",
            result.InstallerUrl);
    }
}
