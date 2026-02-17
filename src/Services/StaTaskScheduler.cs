using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotBooster.Services;

/// <summary>
/// A TaskScheduler that runs tasks on a dedicated STA thread.
/// Required for COM-based APIs like UI Automation.
/// </summary>
internal sealed class StaTaskScheduler : TaskScheduler, IDisposable
{
    internal static StaTaskScheduler Instance { get; } = new();

    private readonly BlockingCollection<Task> _tasks = new();
    private readonly Thread _staThread;

    private StaTaskScheduler()
    {
        this._staThread = new Thread(this.RunTasks)
        {
            IsBackground = true,
            Name = "STA Worker"
        };
        this._staThread.SetApartmentState(ApartmentState.STA);
        this._staThread.Start();
    }

    protected override void QueueTask(Task task) => this._tasks.Add(task);

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

    protected override IEnumerable<Task> GetScheduledTasks() => this._tasks;

    private void RunTasks()
    {
        foreach (var task in this._tasks.GetConsumingEnumerable())
        {
            this.TryExecuteTask(task);
        }
    }

    public void Dispose()
    {
        this._tasks.CompleteAdding();
        this._tasks.Dispose();
    }
}
