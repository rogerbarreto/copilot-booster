using System.Diagnostics;

namespace CopilotBooster.IntegrationTests;

[Trait("Category", "RequiresInteractiveDesktop")]
[Collection(WindowEventHookCollection.Name)]
public class TerminalTitleDetectionIntegrationTests(ITestOutputHelper output)
{
    [StaFact]
    public void TerminalWithSessionTitle_DetectedByHooksAndMatched()
    {
        using var hookService = new WindowEventHookService();
        string? matchedTitle = null;
        Process? proc = null;

        try
        {
            hookService.WindowTitleChanged += (hwnd, title) =>
            {
                if (title.Contains("Terminal - test-session-abc"))
                {
                    matchedTitle = title;
                }
            };
            hookService.Start();

            var titleArg = TestWindowTitle.For("Terminal - test-session-abc");
            proc = Process.Start(new ProcessStartInfo("cmd.exe", $"/k title {titleArg}")
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            })!;

            PumpMessages(5000);

            Assert.NotNull(matchedTitle);

            var match = WindowFocusService.MatchTrackedWindowTitle(matchedTitle!);
            Assert.NotNull(match);
            Assert.Equal("test-session-abc", match.Value.SessionId);
            Assert.Equal("Terminal", match.Value.Label);
        }
        finally
        {
            KillProcess(proc);
        }
    }

    [StaFact]
    public void CopilotCliTitle_DetectedByHooksAndMatched()
    {
        using var hookService = new WindowEventHookService();
        string? matchedTitle = null;
        Process? proc = null;

        try
        {
            hookService.WindowTitleChanged += (hwnd, title) =>
            {
                if (title.Contains("Copilot CLI - session-xyz"))
                {
                    matchedTitle = title;
                }
            };
            hookService.Start();

            var titleArg = TestWindowTitle.For("Copilot CLI - session-xyz");
            proc = Process.Start(new ProcessStartInfo("cmd.exe", $"/k title {titleArg}")
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            })!;

            PumpMessages(5000);

            Assert.NotNull(matchedTitle);

            var match = WindowFocusService.MatchTrackedWindowTitle(matchedTitle!);
            Assert.NotNull(match);
            Assert.Equal("session-xyz", match.Value.SessionId);
            Assert.Equal("Copilot CLI", match.Value.Label);
        }
        finally
        {
            KillProcess(proc);
        }
    }

    [StaFact]
    public void ActiveStatusTracker_CapturesTerminalWindow()
    {
        using var hookService = new WindowEventHookService();
        var tracker = new ActiveStatusTracker();
        Process? proc = null;

        try
        {
            hookService.WindowTitleChanged += (hwnd, title) =>
            {
                tracker.OnWindowTitleChanged(hwnd, title, null);
            };
            hookService.Start();

            var titleArg = TestWindowTitle.For("Terminal - test-session-123");
            proc = Process.Start(new ProcessStartInfo("cmd.exe", $"/k title {titleArg}")
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            })!;

            PumpMessages(5000);

            var activeText = tracker.BuildActiveText("test-session-123");
            output.WriteLine($"ActiveText: '{activeText}'");
            Assert.Contains("Terminal", activeText);
        }
        finally
        {
            KillProcess(proc);
        }
    }

    [StaFact]
    public void TitleChangeFromGenericToSessionPattern_DetectedViaNameChange()
    {
        using var hookService = new WindowEventHookService();
        string? matchedTitle = null;
        bool sawGenericTitle = false;
        Process? proc = null;

        try
        {
            hookService.WindowTitleChanged += (hwnd, title) =>
            {
                // Detect the initial generic title (before rename)
                if (!title.Contains("Terminal - dynamic-session") && !sawGenericTitle && title.Length > 0)
                {
                    sawGenericTitle = true;
                }

                if (title.Contains("Terminal - dynamic-session"))
                {
                    matchedTitle = title;
                }
            };
            hookService.Start();

            // Launch cmd.exe — it initially gets a generic title, then changes it after a delay
            var titleArg = TestWindowTitle.For("Terminal - dynamic-session");
            proc = Process.Start(new ProcessStartInfo(
                "cmd.exe",
                $"/k \"ping -n 3 127.0.0.1 >nul & title {titleArg}\"")
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            })!;

            PumpMessages(5000);

            Assert.NotNull(matchedTitle);

            var match = WindowFocusService.MatchTrackedWindowTitle(matchedTitle!);
            Assert.NotNull(match);
            Assert.Equal("dynamic-session", match.Value.SessionId);
            Assert.Equal("Terminal", match.Value.Label);
        }
        finally
        {
            KillProcess(proc);
        }
    }

    private static void PumpMessages(int durationMs)
    {
        var deadline = Environment.TickCount64 + durationMs;
        while (Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void KillProcess(Process? proc)
    {
        if (proc == null)
        {
            return;
        }

        try
        {
            if (!proc.HasExited)
            {
                proc.Kill();
            }
        }
        catch { }

        proc.Dispose();
    }
}
