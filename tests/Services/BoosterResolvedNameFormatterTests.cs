namespace CopilotBooster.Tests.Services;

public sealed class BoosterResolvedNameFormatterTests
{
    [Fact]
    public void Format_Null_ReturnsNull()
    {
        var result = BoosterResolvedNameFormatter.Format(null);
        Assert.Null(result);
    }

    [Fact]
    public void Format_Empty_ReturnsNull()
    {
        var result = BoosterResolvedNameFormatter.Format("");
        Assert.Null(result);
    }

    [Fact]
    public void Format_Whitespace_ReturnsNull()
    {
        var result = BoosterResolvedNameFormatter.Format("   \t\n  ");
        Assert.Null(result);
    }

    [Fact]
    public void Format_ShortString_ReturnsUnchanged()
    {
        var result = BoosterResolvedNameFormatter.Format("hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Format_Exactly32Chars_NoTruncation()
    {
        var input = "12345678901234567890123456789012";
        Assert.Equal(32, input.Length);
        var result = BoosterResolvedNameFormatter.Format(input);
        Assert.Equal(input, result);
        Assert.Equal(32, result!.Length);
    }

    [Fact]
    public void Format_33Chars_TruncatesWithEllipsis()
    {
        var input = "123456789012345678901234567890123";
        Assert.Equal(33, input.Length);
        var result = BoosterResolvedNameFormatter.Format(input);
        Assert.Equal("12345678901234567890123456789012…", result);
        Assert.Equal(33, result!.Length);
    }

    [Fact]
    public void Format_64Chars_TruncatesTo32PlusEllipsis()
    {
        var input = new string('x', 64);
        var result = BoosterResolvedNameFormatter.Format(input);
        Assert.Equal(new string('x', 32) + "…", result);
        Assert.Equal(33, result!.Length);
    }

    [Fact]
    public void Format_LeadingAndTrailingSpaces_Trims()
    {
        var result = BoosterResolvedNameFormatter.Format("  spaces around  ");
        Assert.Equal("spaces around", result);
    }

    [Fact]
    public void Format_MultipleInternalSpaces_CollapsesToSingleSpace()
    {
        var result = BoosterResolvedNameFormatter.Format("multi   internal\tspaces\nhere");
        Assert.Equal("multi internal spaces here", result);
    }

    [Fact]
    public void Format_LeadingCodeFence_StripsLeadingFence()
    {
        var result = BoosterResolvedNameFormatter.Format("```ts\nlet x = 1\n```");
        Assert.Equal("let x = 1 ```", result);
    }

    [Fact]
    public void Format_PureLeadingFence_StripsIt()
    {
        var result = BoosterResolvedNameFormatter.Format("```\nsome body text");
        Assert.Equal("some body text", result);
    }

    [Theory]
    [InlineData("WindowsTerminal", "WindowsTerminal:Copilot")]
    [InlineData("pwsh", "pwsh:Copilot")]
    [InlineData("warp", "warp:Copilot")]
    public void BuildPlaceholder_WithProcessName_ReturnsFormattedPlaceholder(string processName, string expected)
    {
        var result = BoosterResolvedNameFormatter.BuildPlaceholder(processName);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildPlaceholder_Null_ReturnsCopilot()
    {
        var result = BoosterResolvedNameFormatter.BuildPlaceholder(null);
        Assert.Equal("Copilot", result);
    }

    [Fact]
    public void BuildPlaceholder_Empty_ReturnsCopilot()
    {
        var result = BoosterResolvedNameFormatter.BuildPlaceholder("");
        Assert.Equal("Copilot", result);
    }

    [Fact]
    public void BuildPlaceholder_Whitespace_ReturnsCopilot()
    {
        var result = BoosterResolvedNameFormatter.BuildPlaceholder("   ");
        Assert.Equal("Copilot", result);
    }
}
