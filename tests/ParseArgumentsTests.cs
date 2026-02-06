using System;

public class ParseArgumentsTests
{
    [Fact]
    public void ParseArguments_EmptyArgs_AllDefaults()
    {
        var result = Program.ParseArguments(Array.Empty<string>());

        Assert.Null(result.ResumeSessionId);
        Assert.False(result.OpenExisting);
        Assert.False(result.ShowSettings);
        Assert.Null(result.OpenIdeSessionId);
        Assert.Null(result.WorkDir);
    }

    [Fact]
    public void ParseArguments_Resume_SetsSessionId()
    {
        var result = Program.ParseArguments(new[] { "--resume", "session-123" });

        Assert.Equal("session-123", result.ResumeSessionId);
        Assert.False(result.OpenExisting);
    }

    [Fact]
    public void ParseArguments_OpenExisting_SetsFlag()
    {
        var result = Program.ParseArguments(new[] { "--open-existing" });

        Assert.True(result.OpenExisting);
        Assert.Null(result.ResumeSessionId);
    }

    [Fact]
    public void ParseArguments_Settings_SetsFlag()
    {
        var result = Program.ParseArguments(new[] { "--settings" });

        Assert.True(result.ShowSettings);
    }

    [Fact]
    public void ParseArguments_OpenIde_SetsSessionId()
    {
        var result = Program.ParseArguments(new[] { "--open-ide", "ide-session-1" });

        Assert.Equal("ide-session-1", result.OpenIdeSessionId);
    }

    [Fact]
    public void ParseArguments_WorkDir_SetsPath()
    {
        var result = Program.ParseArguments(new[] { @"D:\repo\work" });

        Assert.Equal(@"D:\repo\work", result.WorkDir);
    }

    [Fact]
    public void ParseArguments_ResumeWithoutValue_IgnoresFlag()
    {
        var result = Program.ParseArguments(new[] { "--resume" });

        Assert.Null(result.ResumeSessionId);
    }

    [Fact]
    public void ParseArguments_MultipleArgs_AllParsed()
    {
        var result = Program.ParseArguments(new[] { "--resume", "s1", "--settings", @"C:\work" });

        Assert.Equal("s1", result.ResumeSessionId);
        Assert.True(result.ShowSettings);
        Assert.Equal(@"C:\work", result.WorkDir);
    }
}
