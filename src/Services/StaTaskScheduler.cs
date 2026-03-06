using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotBooster.Services;

/// <summary>
/// A TaskScheduler that runs each task on a fresh STA thread.
/// A fresh thread avoids stale COM proxy caches that accumulate on a persistent
/// STA thread which doesn't pump Windows messages between tasks.
/// </summary>
internal sealed class StaTaskScheduler : TaskScheduler, IDisposable
{
    internal static StaTaskScheduler Instance { get; } = new();

    protected override void QueueTask(Task task)
    {
        var thread = new Thread(() => this.TryExecuteTask(task))
        {
            IsBackground = true,
            Name = "STA Worker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

    protected override IEnumerable<Task> GetScheduledTasks() => [];

    public void Dispose() { }
}
