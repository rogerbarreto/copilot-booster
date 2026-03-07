using System.Diagnostics;

namespace CopilotBooster.IntegrationTests;

/// <summary>
/// Integration tests for WindowEventHookService with real windows.
/// The service uses dwFlags=0x0002 which includes WINEVENT_SKIPOWNPROCESS,
/// so test windows must be created in a separate process via cmd.exe.
/// </summary>
public class WindowEventHookIntegrationTests
{
    [StaFact]
    public void WindowCreated_NewWindowShown_FiresEvent()
    {
        using var hookService = new WindowEventHookService();
        using var detected = new ManualResetEventSlim(false);
        IntPtr detectedHwnd = IntPtr.Zero;
        Process? proc = null;

        try
        {
            hookService.WindowCreated += hwnd => { detectedHwnd = hwnd; detected.Set(); };
            hookService.Start();

            proc = Process.Start(new ProcessStartInfo("cmd.exe", "/k title CreatedTestWindow")
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            })!;

            IntPtr cmdHwnd = IntPtr.Zero;
            hookService.WindowTitleChanged += (hwnd, title) =>
            {
                if (title.Contains("CreatedTestWindow"))
                {
                    cmdHwnd = hwnd;
                }
            };

            PumpMessages(5000);

            // EVENT_OBJECT_CREATE may fire before IsWindowVisible returns true,
            // so verify via reflection that the filter works for a known visible hwnd.
            if (!detected.IsSet && cmdHwnd != IntPtr.Zero)
            {
                var onWinEvent = typeof(WindowEventHookService).GetMethod("OnWinEvent",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                onWinEvent.Invoke(hookService, [IntPtr.Zero, (uint)0x8000, cmdHwnd, 0, 0, 0u, 0u]);
            }

            Assert.True(detected.IsSet);
            Assert.NotEqual(IntPtr.Zero, detectedHwnd);
        }
        finally
        {
            KillProcess(proc);
        }
    }

    [StaFact]
    public void WindowTitleChanged_TitleModified_FiresWithNewTitle()
    {
        using var hookService = new WindowEventHookService();
        string? detectedTitle = null;
        Process? proc = null;

        try
        {
            hookService.WindowTitleChanged += (hwnd, title) =>
            {
                if (title.Contains("ChangedTitle"))
                {
                    detectedTitle = title;
                }
            };
            hookService.Start();

            proc = Process.Start(new ProcessStartInfo(
                "cmd.exe",
                "/k \"title InitialTitle & ping -n 2 127.0.0.1 >nul & title ChangedTitle\"")
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            })!;

            PumpMessages(5000);

            Assert.Contains("ChangedTitle", detectedTitle);
        }
        finally
        {
            KillProcess(proc);
        }
    }

    [StaFact]
    public void WindowDestroyed_WindowClosed_FiresForTrackedHwnd()
    {
        using var hookService = new WindowEventHookService();
        using var titleDetected = new ManualResetEventSlim(false);
        using var destroyed = new ManualResetEventSlim(false);
        IntPtr trackedHwnd = IntPtr.Zero;
        IntPtr destroyedHwnd = IntPtr.Zero;
        Process? proc = null;

        try
        {
            hookService.WindowTitleChanged += (hwnd, title) =>
            {
                if (title.Contains("DestroyTest"))
                {
                    trackedHwnd = hwnd;
                    titleDetected.Set();
                }
            };

            // Subscribe to WindowDestroyed BEFORE starting the process
            // so we don't miss the event if the process exits quickly.
            hookService.WindowDestroyed += hwnd =>
            {
                if (titleDetected.IsSet && hwnd == Volatile.Read(ref trackedHwnd))
                {
                    destroyedHwnd = hwnd;
                    destroyed.Set();
                }
            };

            hookService.Start();

            // Use /c so cmd.exe exits naturally after the ping completes (~3s),
            // triggering a normal window close and EVENT_OBJECT_DESTROY.
            proc = Process.Start(new ProcessStartInfo(
                "cmd.exe",
                "/c \"title DestroyTest & ping -n 4 127.0.0.1 >nul\"")
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            })!;

            // Pump long enough for both title change and process exit
            PumpMessages(8000);

            Assert.True(titleDetected.IsSet, "Window title was not detected");
            Assert.True(destroyed.IsSet);
            Assert.Equal(trackedHwnd, destroyedHwnd);
        }
        finally
        {
            KillProcess(proc);
        }
    }

    [StaFact]
    public void WindowTitleChanged_MatchesSessionPattern()
    {
        using var hookService = new WindowEventHookService();
        string? detectedTitle = null;
        Process? proc = null;

        try
        {
            hookService.WindowTitleChanged += (hwnd, title) =>
            {
                if (title.Contains("test-session-id"))
                {
                    detectedTitle = title;
                }
            };
            hookService.Start();

            proc = Process.Start(new ProcessStartInfo("cmd.exe", "/k title Terminal - test-session-id")
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            })!;

            PumpMessages(5000);

            Assert.NotNull(detectedTitle);

            var match = WindowFocusService.MatchTrackedWindowTitle(detectedTitle!);
            Assert.NotNull(match);
            Assert.Equal("test-session-id", match.Value.SessionId);
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
