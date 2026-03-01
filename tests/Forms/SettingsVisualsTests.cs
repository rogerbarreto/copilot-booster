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

    [Fact]
    public void GetBrowseInitialDirectory_ExistingDirectory_ReturnsSamePath()
    {
        var tempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        var result = SettingsVisuals.GetBrowseInitialDirectory(tempDir);

        Assert.Equal(tempDir, result);
    }

    [Fact]
    public void GetBrowseInitialDirectory_NonExistentDirectory_ReturnsEmpty()
    {
        var result = SettingsVisuals.GetBrowseInitialDirectory(@"Z:\nonexistent\path\that\does\not\exist");

        Assert.Equal("", result);
    }

    [Fact]
    public void GetBrowseInitialDirectory_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", SettingsVisuals.GetBrowseInitialDirectory(null));
        Assert.Equal("", SettingsVisuals.GetBrowseInitialDirectory(""));
        Assert.Equal("", SettingsVisuals.GetBrowseInitialDirectory("   "));
    }

    [Fact]
    public void GetBrowseInitialDirectory_TrailingBackslash_ReturnsTrimmedPath()
    {
        var tempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var withTrailing = tempDir + Path.DirectorySeparatorChar;

        var result = SettingsVisuals.GetBrowseInitialDirectory(withTrailing);

        Assert.Equal(tempDir, result);
    }
}
