using System.Diagnostics;

namespace CopilotBooster.IntegrationTests.Integration;

public sealed class ProcessTrackingIntegrationTests : IDisposable
{
    private readonly List<Process> _startedProcesses = [];

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

    private Process StartProcess(string arguments)
    {
        var proc = Process.Start(new ProcessStartInfo("cmd.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        this._startedProcesses.Add(proc);
        return proc;
    }

    [Fact]
    public void ProcessExitTracker_TrackedProcessKilled_FiresExitEvent()
    {
        using var tracker = new ProcessExitTracker();
        using var fired = new ManualResetEventSlim(false);
        int exitedPid = 0;

        var proc = this.StartProcess("/k echo hello");

        tracker.ProcessExited += pid => { exitedPid = pid; fired.Set(); };
        tracker.Watch(proc.Id);

        proc.Kill();

        Assert.True(fired.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(proc.Id, exitedPid);
    }

    [Fact]
    public void ProcessExitTracker_ProcessExitsNaturally_FiresExitEvent()
    {
        using var tracker = new ProcessExitTracker();
        using var fired = new ManualResetEventSlim(false);
        int exitedPid = 0;

        var proc = this.StartProcess("/c ping 127.0.0.1 -n 2");

        tracker.ProcessExited += pid => { exitedPid = pid; fired.Set(); };
        tracker.Watch(proc.Id);

        Assert.True(fired.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(proc.Id, exitedPid);
    }

    [Fact]
    public void ActiveStatusTracker_OnProcessExited_RemovesTrackedProcess()
    {
        var statusTracker = new ActiveStatusTracker();
        using var tracker = new ProcessExitTracker();
        using var fired = new ManualResetEventSlim(false);

        var proc = this.StartProcess("/k echo hello");

        statusTracker.TrackProcess("session-1", new ActiveProcess("cmd", proc.Id, null));

        tracker.ProcessExited += pid => { fired.Set(); };
        tracker.Watch(proc.Id);

        proc.Kill();

        Assert.True(fired.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        statusTracker.OnProcessExited(proc.Id);

        string activeText = statusTracker.BuildActiveText("session-1");
        Assert.DoesNotContain("cmd", activeText);
    }

    [Fact]
    public void ProcessExitTracker_MultipleProcesses_OnlyExitedOneFires()
    {
        using var tracker = new ProcessExitTracker();
        using var fired1 = new ManualResetEventSlim(false);
        using var fired2 = new ManualResetEventSlim(false);
        int exitedPid = 0;

        var proc1 = this.StartProcess("/k echo proc1");
        var proc2 = this.StartProcess("/k echo proc2");

        tracker.ProcessExited += pid =>
        {
            if (pid == proc1.Id) { exitedPid = pid; fired1.Set(); }
            if (pid == proc2.Id) { fired2.Set(); }
        };

        tracker.Watch(proc1.Id);
        tracker.Watch(proc2.Id);

        proc1.Kill();

        Assert.True(fired1.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(proc1.Id, exitedPid);
        Assert.False(fired2.Wait(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken));

        tracker.Unwatch(proc2.Id);
    }
}
