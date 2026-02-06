public class IdeEntryTests
{
    [Fact]
    public void ToString_WithDescription_ReturnsDescriptionAndPath()
    {
        var entry = new IdeEntry { Description = "VS Code", Path = @"C:\code.exe" };
        Assert.Equal(@"VS Code  —  C:\code.exe", entry.ToString());
    }

    [Fact]
    public void ToString_WithoutDescription_ReturnsPathOnly()
    {
        var entry = new IdeEntry { Description = "", Path = @"C:\code.exe" };
        Assert.Equal(@"C:\code.exe", entry.ToString());
    }

    [Fact]
    public void ToString_EmptyDescriptionAndPath_ReturnsEmptyPath()
    {
        var entry = new IdeEntry { Description = "", Path = "" };
        Assert.Equal("", entry.ToString());
    }
}
