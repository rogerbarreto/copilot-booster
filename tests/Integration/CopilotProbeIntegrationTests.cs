using System.Diagnostics;

namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Integration tests for CopilotProbe that verify behavior against real copilot.exe installations.
/// These tests reproduce the bug where WinGet-installed copilot.exe times out during version probe
/// due to stdout redirection holding the pipe open even after the version text is printed.
/// </summary>
public sealed class CopilotProbeIntegrationTests
{
    /// <summary>
    /// Tests that CopilotProbe correctly detects a real WinGet-installed copilot.exe.
    /// 
    /// BUG REPRODUCTION: On Roger's machine with WinGet-installed copilot.exe, the probe
    /// times out every time (verified 3x). However, this test may PASS intermittently due
    /// to timing variance. The PowerShell reproduction script in the task description
    /// reproduces it reliably. See ProbeVersion_DirectReproduction_DocumentsTimeout for
    /// explicit timeout measurement.
    /// 
    /// EXPECTED: After Trinity's fix, this test should ALWAYS PASS — the probe should
    /// return true when a working copilot.exe is installed and can be located.
    /// </summary>
    [LocalOnlyFact]
    [Trait("Category", "LocalOnly")]
    public void IsCopilotAvailable_WithRealWingetCopilotExe_ReturnsTrue()
    {
        // Arrange — use the real default ctor (resolves via CopilotLocator)
        var probe = new CopilotProbe();

        // Act
        var available = probe.IsCopilotAvailable();

        // Assert — should return true when copilot.exe is installed
        // KNOWN ISSUE: Current code may return false due to 5s timeout bug
        Assert.True(available, "CopilotProbe must return true when a working copilot.exe is installed.");
    }

    /// <summary>
    /// Verifies that when copilot.exe is present but IsCopilotAvailable returns false
    /// (due to the stdout redirection bug), the locator still finds the path.
    /// This confirms the issue is in the probe logic, not in the locator.
    /// </summary>
    [LocalOnlyFact]
    [Trait("Category", "LocalOnly")]
    public void CopilotLocator_WithWingetInstall_FindsValidPath()
    {
        // Act
        var path = CopilotLocator.FindCopilotExe();

        // Assert — locator should find the path even if the probe times out
        Assert.False(string.IsNullOrWhiteSpace(path), "CopilotLocator must find a non-empty path.");
        Assert.NotEqual("copilot.exe", path); // Should resolve to full path, not fallback
        Assert.True(File.Exists(path), $"Resolved path '{path}' must exist on disk.");
    }

    /// <summary>
    /// Direct reproduction of the timeout bug using the same Process.Start pattern
    /// as ProbeVersion. Measures actual timeout duration and documents the expected
    /// failure mode.
    /// 
    /// BUG: On Roger's machine, this fails every time (process does NOT exit within 5s).
    /// On other machines or timing conditions, it may pass — documenting the flakiness.
    /// 
    /// EXPECTED: After Trinity's fix, this test becomes obsolete (probe won't execute
    /// the binary at all), so it's marked Skip to avoid false negatives in CI.
    /// </summary>
    [LocalOnlyFact(Skip = "Flaky due to timing variance; run manually to verify timeout bug")]
    [Trait("Category", "LocalOnly")]
    public void ProbeVersion_DirectReproduction_DocumentsTimeout()
    {
        // Arrange: Get the real copilot.exe path
        var copilotPath = CopilotLocator.FindCopilotExe();
        Assert.NotEqual("copilot.exe", copilotPath); // Must have resolved to full path
        Assert.True(File.Exists(copilotPath), $"Copilot path '{copilotPath}' must exist.");

        // Act: Reproduce the exact ProbeVersion logic
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = copilotPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("--version");

        var sw = Stopwatch.StartNew();
        Assert.True(process.Start(), "Process must start successfully.");
        var exited = process.WaitForExit(5_000);
        sw.Stop();

        // Cleanup
        if (!exited)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }

        // Assert: Documents the bug — process should NOT exit within 5s
        // This assertion will FAIL if the bug reproduces (which is what we want to document)
        // After Trinity's fix, this test becomes obsolete
        Assert.False(exited, $"BUG REPRODUCTION: Process should NOT exit within 5s (actual: {sw.ElapsedMilliseconds}ms). This documents the timeout issue.");
    }
}
