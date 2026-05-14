namespace CopilotBooster.Tests.Services;

public sealed class Win32ProcessCwdTests
{
    [Fact]
    public void Get_WithInvalidPid_ReturnsNull()
    {
        var result = Win32ProcessCwd.Get(int.MaxValue);

        Assert.Null(result);
    }

    [Fact]
    public void Get_WithNegativePid_ReturnsNull()
    {
        var result = Win32ProcessCwd.Get(-1);

        Assert.Null(result);
    }

    [Fact]
    public void Get_CachesResultForSameProcessStartTime()
    {
        var nonExistentPid = int.MaxValue;

        var firstCall = Win32ProcessCwd.Get(nonExistentPid);
        var secondCall = Win32ProcessCwd.Get(nonExistentPid);

        Assert.Equal(firstCall, secondCall);
        Assert.Null(firstCall);
    }
}
