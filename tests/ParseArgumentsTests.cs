public class ParseArgumentsTests
{
    [Fact]
    public void ParseArguments_EmptyArgs_AllDefaults()
    {
        var result = Program.ParseArguments([]);

        Assert.Null(result.ResumeSessionId);
        Assert.False(result.OpenExisting);
        Assert.False(result.ShowSettings);
        Assert.False(result.NewSession);
        Assert.Null(result.OpenIdeSessionId);
        Assert.Null(result.WorkDir);
    }

    [Fact]
    public void ParseArguments_Resume_SetsSessionId()
    {
        var result = Program.ParseArguments(["--resume", "session-123"]);

        Assert.Equal("session-123", result.ResumeSessionId);
        Assert.False(result.OpenExisting);
    }

    [Fact]
    public void ParseArguments_OpenExisting_SetsFlag()
    {
        var result = Program.ParseArguments(["--open-existing"]);

        Assert.True(result.OpenExisting);
        Assert.Null(result.ResumeSessionId);
    }

    [Fact]
    public void ParseArguments_Settings_SetsFlag()
    {
        var result = Program.ParseArguments(["--settings"]);

        Assert.True(result.ShowSettings);
    }

    [Fact]
    public void ParseArguments_NewSession_SetsFlag()
    {
        var result = Program.ParseArguments(["--new-session"]);

        Assert.True(result.NewSession);
        Assert.False(result.OpenExisting);
    }

    [Fact]
    public void ParseArguments_OpenIde_SetsSessionId()
    {
        var result = Program.ParseArguments(["--open-ide", "ide-session-1"]);

        Assert.Equal("ide-session-1", result.OpenIdeSessionId);
    }

    [Fact]
    public void ParseArguments_WorkDir_SetsPath()
    {
        var result = Program.ParseArguments([@"D:\repo\work"]);

        Assert.Equal(@"D:\repo\work", result.WorkDir);
    }

    [Fact]
    public void ParseArguments_ResumeWithoutValue_IgnoresFlag()
    {
        var result = Program.ParseArguments(["--resume"]);

        Assert.Null(result.ResumeSessionId);
    }

    [Fact]
    public void ParseArguments_MultipleArgs_AllParsed()
    {
        var result = Program.ParseArguments(["--resume", "s1", "--settings", @"C:\work"]);

        Assert.Equal("s1", result.ResumeSessionId);
        Assert.True(result.ShowSettings);
        Assert.Equal(@"C:\work", result.WorkDir);
    }
}
