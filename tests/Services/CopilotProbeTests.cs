namespace CopilotBooster.Tests.Services;

public sealed class CopilotProbeTests
{
    [Fact]
    public void IsCopilotAvailable_FirstCall_ProbesConfiguredPath()
    {
        var calls = new List<string>();
        var probe = new CopilotProbe(() => "git", path =>
        {
            calls.Add(path);
            return true;
        });

        var available = probe.IsCopilotAvailable();

        Assert.True(available);
        Assert.Equal(["git"], calls);
    }

    [Fact]
    public void IsCopilotAvailable_SecondCallWithSamePath_ReturnsCachedResult()
    {
        var callCount = 0;
        var probe = new CopilotProbe(() => "git", _ =>
        {
            callCount++;
            return true;
        });

        Assert.True(probe.IsCopilotAvailable());
        Assert.True(probe.IsCopilotAvailable());

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void IsCopilotAvailable_PathChange_InvalidatesCache()
    {
        var path = "git";
        var calls = new List<string>();
        var probe = new CopilotProbe(() => path, probedPath =>
        {
            calls.Add(probedPath);
            return probedPath == "git";
        });

        Assert.True(probe.IsCopilotAvailable());
        path = @"X:\nope.exe";
        Assert.False(probe.IsCopilotAvailable());

        Assert.Equal(["git", @"X:\nope.exe"], calls);
    }

    [Fact]
    public void InvalidateCache_ThenIsCopilotAvailable_Reprobes()
    {
        var callCount = 0;
        var probe = new CopilotProbe(() => "git", _ =>
        {
            callCount++;
            return true;
        });

        Assert.True(probe.IsCopilotAvailable());
        probe.InvalidateCache();
        Assert.True(probe.IsCopilotAvailable());

        Assert.Equal(2, callCount);
    }

    [Fact]
    public void IsCopilotAvailable_BinaryNotFound_ReturnsFalseWithoutThrowing()
    {
        var probe = new CopilotProbe(() => @"X:\nope.exe");

        var available = probe.IsCopilotAvailable();

        Assert.False(available);
    }
}
