public class SessionInfoTests
{
    [Fact]
    public void SessionInfo_DefaultPropertyValues()
    {
        var info = new SessionInfo();

        Assert.Equal("", info.Id);
        Assert.Equal("", info.Cwd);
        Assert.Equal("", info.Summary);
        Assert.Equal(0, info.Pid);
    }

    [Fact]
    public void SessionInfo_PropertyGettersSetters()
    {
        var info = new SessionInfo
        {
            Id = "test-id",
            Cwd = @"C:\test",
            Summary = "Test summary",
            Pid = 42
        };

        Assert.Equal("test-id", info.Id);
        Assert.Equal(@"C:\test", info.Cwd);
        Assert.Equal("Test summary", info.Summary);
        Assert.Equal(42, info.Pid);
    }
}
