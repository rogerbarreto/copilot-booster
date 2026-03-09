using System.Diagnostics;

namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Diagnostic test using real VS Code Insiders to understand IDE tracking
/// with multiple sessions opened and closed one by one.
/// </summary>
public sealed class VsCodeTrackingDiagnosticTests : IDisposable
{
    private readonly List<Process> _startedProcesses = [];

    private const string VsCodeInsidersPath = @"C:\Users\roger\AppData\Local\Programs\Microsoft VS Code Insiders\Code - Insiders.exe";

    public void Dispose()
    {
        foreach (var proc in this._startedProcesses)
        {
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

    /// <summary>
    /// Opens 3 VS Code Insiders windows for different sessions, then closes them one by one.
    /// Logs all events and tracking state at each step.
    /// </summary>
    [StaFact]
    public void Diagnostic_VsCodeInsiders_ThreeSessionsCloseOneByOne()
    {
        if (!File.Exists(VsCodeInsidersPath))
        {
            Console.Error.WriteLine($"SKIP: VS Code Insiders not found at {VsCodeInsidersPath}");
            return;
        }

        const string Session1 = "diag-vsc-1";
        const string Session2 = "diag-vsc-2";
        const string Session3 = "diag-vsc-3";

        // Create 3 temp folders so each VS Code instance opens a different workspace
        var dir1 = Path.Combine(Path.GetTempPath(), Session1);
        var dir2 = Path.Combine(Path.GetTempPath(), Session2);
        var dir3 = Path.Combine(Path.GetTempPath(), Session3);
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        Directory.CreateDirectory(dir3);

        try
        {
            using var hookService = new WindowEventHookService();
            var tracker = new ActiveStatusTracker();

            var sessions = new List<NamedSession>
            {
                new() { Id = Session1, Summary = "Session 1" },
                new() { Id = Session2, Summary = "Session 2" },
                new() { Id = Session3, Summary = "Session 3" }
            };

            // Track all events
            var dirtySessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allCreated = new List<(IntPtr Hwnd, int Pid, string Title)>();
            var allDestroyed = new List<(IntPtr Hwnd, string MatchedSession)>();

            hookService.WindowCreated += hwnd =>
            {
                int pid = WindowFocusService.GetWindowProcessId(hwnd);
                string title = WindowFocusService.GetWindowTitle(hwnd);
                allCreated.Add((hwnd, pid, title));

                var sid = tracker.OnWindowCreated(hwnd);
                if (sid != null)
                {
                    dirtySessionIds.Add(sid);
                    Console.Error.WriteLine($"[WindowCreated] CAPTURED HWND={hwnd}, PID={pid}, Title='{title}', Session={sid}");
                }
            };

            hookService.WindowDestroyed += hwnd =>
            {
                var affected = tracker.OnWindowDestroyed(hwnd);
                foreach (var id in affected)
                {
                    dirtySessionIds.Add(id);
                    allDestroyed.Add((hwnd, id));
                    Console.Error.WriteLine($"[WindowDestroyed] HWND={hwnd}, Affected={id}");
                }
            };

            hookService.ForegroundChanged += hwnd =>
            {
                var sid = tracker.OnWindowCreated(hwnd);
                if (sid != null)
                {
                    dirtySessionIds.Add(sid);
                    Console.Error.WriteLine($"[ForegroundChanged] CAPTURED HWND={hwnd}, Session={sid}");
                }
            };

            hookService.Start();

            // ── Open Session 1 ──
            Console.Error.WriteLine("\n=== Opening Session 1 ===");
            var proc1 = this.LaunchVsCode(dir1);
            tracker.TrackProcess(Session1, new ActiveProcess("VS Code Insiders", proc1.Id, dir1));
            Console.Error.WriteLine($"Tracked PID={proc1.Id} for {Session1}");
            WaitAndPump(5000);
            DumpState(tracker, sessions, "After opening Session 1");

            // ── Open Session 2 ──
            Console.Error.WriteLine("\n=== Opening Session 2 ===");
            var proc2 = this.LaunchVsCode(dir2);
            tracker.TrackProcess(Session2, new ActiveProcess("VS Code Insiders", proc2.Id, dir2));
            Console.Error.WriteLine($"Tracked PID={proc2.Id} for {Session2}");
            WaitAndPump(5000);
            DumpState(tracker, sessions, "After opening Session 2");

            // ── Open Session 3 ──
            Console.Error.WriteLine("\n=== Opening Session 3 ===");
            var proc3 = this.LaunchVsCode(dir3);
            tracker.TrackProcess(Session3, new ActiveProcess("VS Code Insiders", proc3.Id, dir3));
            Console.Error.WriteLine($"Tracked PID={proc3.Id} for {Session3}");
            WaitAndPump(5000);
            DumpState(tracker, sessions, "After opening Session 3");

            // ── Close Session 2 (middle one) ──
            Console.Error.WriteLine("\n=== Closing Session 2 ===");
            dirtySessionIds.Clear();
            proc2.Kill();
            WaitAndPump(5000);
            DumpState(tracker, sessions, "After closing Session 2");
            Console.Error.WriteLine($"Dirty sessions after close: [{string.Join(", ", dirtySessionIds)}]");

            // ── Close Session 1 ──
            Console.Error.WriteLine("\n=== Closing Session 1 ===");
            dirtySessionIds.Clear();
            proc1.Kill();
            WaitAndPump(5000);
            DumpState(tracker, sessions, "After closing Session 1");
            Console.Error.WriteLine($"Dirty sessions after close: [{string.Join(", ", dirtySessionIds)}]");

            // ── Close Session 3 ──
            Console.Error.WriteLine("\n=== Closing Session 3 ===");
            dirtySessionIds.Clear();
            proc3.Kill();
            WaitAndPump(5000);
            DumpState(tracker, sessions, "After closing Session 3");

            Console.Error.WriteLine("\n=== DIAGNOSIS COMPLETE ===");
        }
        finally
        {
            try { Directory.Delete(dir1, true); } catch { }
            try { Directory.Delete(dir2, true); } catch { }
            try { Directory.Delete(dir3, true); } catch { }
        }
    }

    private Process LaunchVsCode(string folder)
    {
        var proc = SessionInteractionManager.OpenInIde(VsCodeInsidersPath, folder)!;
        this._startedProcesses.Add(proc);
        return proc;
    }

    private static void DumpState(ActiveStatusTracker tracker, List<NamedSession> sessions, string label)
    {
        Console.Error.WriteLine($"  [{label}]");
        foreach (var s in sessions)
        {
            var text = tracker.BuildActiveText(s.Id);
            Console.Error.WriteLine($"    {s.Id}: '{text}'");
        }
    }

    private static void WaitAndPump(int ms)
    {
        var deadline = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }
}
