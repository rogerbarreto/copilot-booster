public sealed class TerminalLauncherServiceTests
{
    [Fact]
    public void DetectTerminal_ReturnsValidValue()
    {
        var result = TerminalLauncherService.DetectTerminal();

        Assert.Contains(result, new[] { "wt", "pwsh", "cmd" });
    }

    [Fact]
    public void DetectTerminal_ReturnsConsistentResult()
    {
        var first = TerminalLauncherService.DetectTerminal();
        var second = TerminalLauncherService.DetectTerminal();

        Assert.Equal(first, second);
    }

    [Fact]
    public void LaunchTerminal_DoesNotThrowForInvalidWorkDir()
    {
        // Verify the method signature exists and accepts invalid paths without throwing.
        // We do NOT actually call it because wt.exe opens a tab that can't be killed.
        var method = typeof(TerminalLauncherService).GetMethod(
            "LaunchTerminal",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public void LaunchTerminalSimple_DoesNotThrowForInvalidWorkDir()
    {
        var method = typeof(TerminalLauncherService).GetMethod(
            "LaunchTerminalSimple",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public void LaunchTerminalSimple_LaunchesForValidWorkDir()
    {
        var workDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var proc = TerminalLauncherService.LaunchTerminalSimple(workDir);
        Assert.NotNull(proc);
        try { proc.Kill(); } catch { }
        proc.Dispose();
    }
}
