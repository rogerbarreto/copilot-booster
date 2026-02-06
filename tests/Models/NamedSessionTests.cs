using System;

public class NamedSessionTests
{
    [Fact]
    public void NamedSession_DefaultPropertyValues()
    {
        var session = new NamedSession();

        Assert.Equal("", session.Id);
        Assert.Equal("", session.Cwd);
        Assert.Equal("", session.Summary);
        Assert.Equal(default(DateTime), session.LastModified);
    }

    [Fact]
    public void NamedSession_PropertyGettersSetters()
    {
        var now = DateTime.Now;
        var session = new NamedSession
        {
            Id = "ns-1",
            Cwd = @"D:\work",
            Summary = "[work] Fix bug",
            LastModified = now
        };

        Assert.Equal("ns-1", session.Id);
        Assert.Equal(@"D:\work", session.Cwd);
        Assert.Equal("[work] Fix bug", session.Summary);
        Assert.Equal(now, session.LastModified);
    }
}
