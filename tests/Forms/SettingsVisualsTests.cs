public sealed class SettingsVisualsTests
{
    [Fact]
    public void StripNotFoundPrefix_WithPrefix_ReturnsRawPath()
    {
        var result = SettingsVisuals.StripNotFoundPrefix("(not found) G:\\MyGames");

        Assert.Equal("G:\\MyGames", result);
    }

    [Fact]
    public void StripNotFoundPrefix_WithoutPrefix_ReturnsOriginal()
    {
        var result = SettingsVisuals.StripNotFoundPrefix("C:\\Users\\roger");

        Assert.Equal("C:\\Users\\roger", result);
    }

    [Fact]
    public void StripNotFoundPrefix_EmptyString_ReturnsEmpty()
    {
        var result = SettingsVisuals.StripNotFoundPrefix("");

        Assert.Equal("", result);
    }
}
