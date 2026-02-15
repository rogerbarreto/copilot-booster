public sealed class ThemeServiceTests
{
    [Theory]
    [InlineData("system", 0)]
    [InlineData("light", 1)]
    [InlineData("dark", 2)]
    [InlineData("unknown", 0)]
    public void ThemeToIndex_ReturnsCorrectIndex(string theme, int expected)
    {
        Assert.Equal(expected, ThemeService.ThemeToIndex(theme));
    }

    [Theory]
    [InlineData(0, "system")]
    [InlineData(1, "light")]
    [InlineData(2, "dark")]
    [InlineData(99, "system")]
    [InlineData(-1, "system")]
    public void IndexToTheme_ReturnsCorrectTheme(int index, string expected)
    {
        Assert.Equal(expected, ThemeService.IndexToTheme(index));
    }
}
