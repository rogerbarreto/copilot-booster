namespace CopilotBooster.Tests.Services;

public sealed class HostKindClassifierTests
{
    [Fact]
    public void Classify_Null_ReturnsUnknown()
    {
        var result = HostKindClassifier.Classify(null);
        Assert.Equal("Unknown", result);
    }

    [Fact]
    public void Classify_Empty_ReturnsUnknown()
    {
        var result = HostKindClassifier.Classify("");
        Assert.Equal("Unknown", result);
    }

    [Fact]
    public void Classify_Whitespace_ReturnsUnknown()
    {
        var result = HostKindClassifier.Classify("   ");
        Assert.Equal("Unknown", result);
    }

    [Theory]
    [InlineData("WindowsTerminal", "Windows Terminal")]
    [InlineData("windowsterminal", "Windows Terminal")]
    [InlineData("WINDOWSTERMINAL", "Windows Terminal")]
    public void Classify_WindowsTerminal_ReturnsWindowsTerminal(string input, string expected)
    {
        var result = HostKindClassifier.Classify(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("warp", "Warp")]
    [InlineData("warpterminal", "Warp")]
    [InlineData("warp-terminal", "Warp")]
    [InlineData("WARP", "Warp")]
    [InlineData("WarpTerminal", "Warp")]
    public void Classify_Warp_ReturnsWarp(string input, string expected)
    {
        var result = HostKindClassifier.Classify(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("wezterm-gui", "WezTerm")]
    [InlineData("wezterm", "WezTerm")]
    [InlineData("WEZTERM", "WezTerm")]
    public void Classify_WezTerm_ReturnsWezTerm(string input, string expected)
    {
        var result = HostKindClassifier.Classify(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("alacritty", "Alacritty")]
    [InlineData("ALACRITTY", "Alacritty")]
    public void Classify_Alacritty_ReturnsAlacritty(string input, string expected)
    {
        var result = HostKindClassifier.Classify(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("conhost", "Console")]
    [InlineData("CONHOST", "Console")]
    public void Classify_Conhost_ReturnsConsole(string input, string expected)
    {
        var result = HostKindClassifier.Classify(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("powershell", "PowerShell")]
    [InlineData("pwsh", "PowerShell")]
    [InlineData("PWSH", "PowerShell")]
    [InlineData("  PWSH  ", "PowerShell")]
    public void Classify_PowerShell_ReturnsPowerShell(string input, string expected)
    {
        var result = HostKindClassifier.Classify(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("cmd", "Command Prompt")]
    [InlineData("CMD", "Command Prompt")]
    public void Classify_Cmd_ReturnsCommandPrompt(string input, string expected)
    {
        var result = HostKindClassifier.Classify(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("code", "VS Code")]
    [InlineData("CODE", "VS Code")]
    public void Classify_Code_ReturnsVSCode(string input, string expected)
    {
        var result = HostKindClassifier.Classify(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("code - insiders", "VS Code Insiders")]
    [InlineData("CODE - INSIDERS", "VS Code Insiders")]
    public void Classify_CodeInsiders_ReturnsVSCodeInsiders(string input, string expected)
    {
        var result = HostKindClassifier.Classify(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("cursor", "Cursor")]
    [InlineData("CURSOR", "Cursor")]
    public void Classify_Cursor_ReturnsCursor(string input, string expected)
    {
        var result = HostKindClassifier.Classify(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("devenv", "Visual Studio")]
    [InlineData("DEVENV", "Visual Studio")]
    public void Classify_Devenv_ReturnsVisualStudio(string input, string expected)
    {
        var result = HostKindClassifier.Classify(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Classify_Explorer_ReturnsUnknown()
    {
        var result = HostKindClassifier.Classify("explorer");
        Assert.Equal("Unknown", result);
    }
}
