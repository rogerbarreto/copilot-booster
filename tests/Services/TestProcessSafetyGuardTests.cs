/// <summary>
/// Source-contract guards that enforce a hard rule across the integration test
/// suite: a test may only destroy processes whose creation it tracked itself.
///
/// Discovery-based fallbacks (e.g. <c>Process.GetProcessesByName("WindowsTerminal")</c>
/// followed by tracking + Kill) are forbidden, because they will kill processes
/// that belong to the developer's running Copilot CLI / Windows Terminal session
/// the moment a test's own UIA discovery hiccups.
///
/// These tests read the integration test source files from disk and assert the
/// dangerous patterns are absent. They run inside the unit test project so the
/// destructive integration tests never execute.
/// </summary>
public sealed class TestProcessSafetyGuardTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var gitPath = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                $"Could not locate repo root (no .git found) starting from {AppContext.BaseDirectory}");
        }
    }

    private static string ReadTestFile(string relative)
    {
        var path = Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Expected test file at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void WindowsTerminalMultiPaneE2ETests_DoesNotEnumerateAndTrackAllWtWindowsAsFallback()
    {
        var src = ReadTestFile("tests/Integration/WindowsTerminalMultiPaneE2ETests.cs");

        // The broad fallback at the old lines 411-416 enumerated ALL WindowsTerminal
        // HWNDs on the developer's machine and added them to _wtWindowHwnds without
        // any matching/filter condition. CleanupProcessesAndWindows would then kill
        // the WT process behind each HWND — destroying WT windows the test did not
        // create. A safe usage of EnumerateWindowsTerminalHwnds must filter to the
        // test's own session (via pane name match) BEFORE adding to _wtWindowHwnds.
        var dangerousBlock = string.Join(
            "\n",
            "foreach (var hwnd in EnumerateWindowsTerminalHwnds())",
            "            {",
            "                this._wtWindowHwnds.Add(hwnd);",
            "            }");

        // Normalize line endings so the assertion is robust on CRLF and LF.
        var normalized = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.DoesNotContain(dangerousBlock, normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsTerminalMultiPaneE2ETests_DoesNotForceKillWtProcessDerivedFromHwndOwner()
    {
        var src = ReadTestFile("tests/Integration/WindowsTerminalMultiPaneE2ETests.cs");

        // The old cleanup loop derived a PID from each tracked HWND via
        // GetWindowThreadProcessId(hwnd, out var wtPid) and then called
        // wtProcess.Kill(). Windows Terminal is a single-instance app: that PID
        // owns every WT window on the desktop, including the developer's live
        // Copilot CLI host. Killing it kills their session. Cleanup must rely on
        // WM_CLOSE for windows it created and only Kill PIDs the test spawned.
        Assert.DoesNotContain("wtProcess.Kill();", src, StringComparison.Ordinal);
    }
}
