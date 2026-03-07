using System.Diagnostics;

public sealed class ProcessExitTrackerTests : IDisposable
{
    private readonly ProcessExitTracker _tracker = new();

    public void Dispose()
    {
        this._tracker.Dispose();
    }

    [Fact]
    public void Watch_NonExistentPid_FiresProcessExitedImmediately()
    {
        using var fired = new ManualResetEventSlim(false);
        int exitedPid = 0;

        this._tracker.ProcessExited += pid =>
        {
            exitedPid = pid;
            fired.Set();
        };

        this._tracker.Watch(int.MaxValue);

        Assert.True(fired.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "ProcessExited should fire for non-existent PID");
        Assert.Equal(int.MaxValue, exitedPid);
    }

    [Fact]
    public void Unwatch_UnknownPid_DoesNotThrow()
    {
        var ex = Record.Exception(() => this._tracker.Unwatch(12345));
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_AfterWatch_DoesNotThrow()
    {
        this._tracker.Watch(Environment.ProcessId);

        var ex = Record.Exception(() => this._tracker.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Watch_CurrentProcess_DoesNotFireImmediately()
    {
        using var fired = new ManualResetEventSlim(false);

        this._tracker.ProcessExited += _ => fired.Set();
        this._tracker.Watch(Environment.ProcessId);

        Assert.False(fired.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken), "ProcessExited should not fire for a running process");
        this._tracker.Unwatch(Environment.ProcessId);
    }

    [Fact]
    public void Watch_ShortLivedProcess_FiresWhenProcessExits()
    {
        using var fired = new ManualResetEventSlim(false);
        int exitedPid = 0;

        var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;

        this._tracker.ProcessExited += pid =>
        {
            exitedPid = pid;
            fired.Set();
        };

        this._tracker.Watch(process.Id);

        Assert.True(fired.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "ProcessExited should fire when the process exits");
        Assert.Equal(process.Id, exitedPid);
    }
}
