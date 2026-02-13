public class NamedSessionTests
{
    [Fact]
    public void NamedSession_DefaultPropertyValues()
    {
        var session = new NamedSession();

        Assert.Equal("", session.Id);
        Assert.Equal("", session.Cwd);
        Assert.Equal("", session.Folder);
        Assert.Equal("", session.Summary);
        Assert.False(session.IsGitRepo);
        Assert.Equal(default, session.LastModified);
    }

    [Fact]
    public void NamedSession_PropertyGettersSetters()
    {
        var now = DateTime.Now;
        var session = new NamedSession
        {
            Id = "ns-1",
            Cwd = @"D:\work",
            Folder = "work",
            Summary = "Fix bug",
            IsGitRepo = true,
            LastModified = now
        };

        Assert.Equal("ns-1", session.Id);
        Assert.Equal(@"D:\work", session.Cwd);
        Assert.Equal("work", session.Folder);
        Assert.Equal("Fix bug", session.Summary);
        Assert.True(session.IsGitRepo);
        Assert.Equal(now, session.LastModified);
    }
}
