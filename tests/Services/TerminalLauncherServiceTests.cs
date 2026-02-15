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
    public void LaunchTerminal_ReturnsNullForInvalidWorkDir()
    {
        var bogusDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var ex = Record.Exception(() =>
        {
            var proc = TerminalLauncherService.LaunchTerminal(bogusDir, "test-session");
            if (proc is not null)
            {
                try { proc.Kill(); } catch { }
                proc.Dispose();
            }
        });

        Assert.Null(ex);
    }

    [Fact]
    public void LaunchTerminalSimple_DoesNotThrowForInvalidWorkDir()
    {
        var bogusDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var ex = Record.Exception(() =>
        {
            var proc = TerminalLauncherService.LaunchTerminalSimple(bogusDir);
            if (proc is not null)
            {
                try { proc.Kill(); } catch { }
                proc.Dispose();
            }
        });

        Assert.Null(ex);
    }

    [Fact]
    public void LaunchTerminalSimple_LaunchesForValidWorkDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var proc = TerminalLauncherService.LaunchTerminalSimple(tempDir);
            Assert.NotNull(proc);
            try { proc.Kill(); } catch { }
            proc.Dispose();
        }
        finally
        {
            try { Directory.Delete(tempDir); } catch { }
        }
    }
}
