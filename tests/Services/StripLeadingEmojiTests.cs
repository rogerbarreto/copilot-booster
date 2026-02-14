namespace CopilotApp.Tests.Services;

public class StripLeadingEmojiTests
{
    [Theory]
    [InlineData("🤖 Fixing emoji prefix", "Fixing emoji prefix")]
    [InlineData("🤖 My Session", "My Session")]
    [InlineData("⚡ Building project", "Building project")]
    [InlineData("✅ All tests passed", "All tests passed")]
    [InlineData("My Session", "My Session")]
    [InlineData("", "")]
    [InlineData("   Leading spaces", "Leading spaces")]
    [InlineData("🤖🔧 Double emoji", "Double emoji")]
    public void StripLeadingEmoji_ReturnsExpected(string input, string expected)
    {
        var result = WindowFocusService.StripLeadingEmoji(input);
        Assert.Equal(expected, result);
    }
}
