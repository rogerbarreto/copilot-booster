
using System;
using System.Diagnostics;
using System.IO;
using CopilotBooster.Models;
using CopilotBooster.Services;

namespace CopilotBooster.Tests.Services;
/// <summary>
/// Tests for Bug D: /resume rebind support in ActiveStatusTracker.HandleExternalSessionDiscovered.
/// When a copilotPid is already bound to sessionA, and a discovery event arrives for sessionB with
/// the same PID, the tracker should evict sessionA before binding sessionB.
/// </summary>
public sealed class HandleExternalSessionDiscoveredRebindTests
{
    [Fact]
    public void Discovery_ForPidAlreadyBoundToDifferentSession_RemovesOldBinding()
    {
        // Bug D: PID 39992 was bound to session 0bb1099b, then `/resume` switched to session 2d76b3fe.
        // The watcher emits a discovery event for 2d76b3fe + 39992.
        // The tracker should REMOVE the old 0bb1099b → 39992 binding before adding the new one.

        var tracker = new ActiveStatusTracker();
        string? removedSessionId = null;
        tracker.CopilotHostRemoved += (sid) => removedSessionId = sid;

        // Pre-bind sessionA → copilotPid (use our own PID so it passes liveness check)
        var sessionA = "aaaaaaaa-1111-2222-3333-444444444444";
        var sessionB = "bbbbbbbb-5555-6666-7777-888888888888";
        var copilotPid = Process.GetCurrentProcess().Id;

        // Create fake events.jsonl for sessionA in Program.SessionStateDir so the liveness gate passes
        var sessionDirA = Path.Combine(Program.SessionStateDir, sessionA);
        Directory.CreateDirectory(sessionDirA);
        File.WriteAllText(Path.Combine(sessionDirA, "events.jsonl"), "{}");

        // Manually inject the old binding (simulating a prior discovery)
        var hostInfoA = new CopilotHostInfo(
            HostHwnd: IntPtr.Zero,
            HostPid: 1234,
            CopilotPid: copilotPid,
            HostProcessName: "WindowsTerminal.exe",
            HostKindLabel: "Windows Terminal");
        tracker.GetType()
            .GetMethod("SetCopilotHost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(tracker, [sessionA, hostInfoA]);

        Assert.NotNull(tracker.GetCopilotHost(sessionA));

        // Create fake events.jsonl for sessionB in Program.SessionStateDir so the liveness gate passes
        var sessionDirB = Path.Combine(Program.SessionStateDir, sessionB);
        Directory.CreateDirectory(sessionDirB);
        File.WriteAllText(Path.Combine(sessionDirB, "events.jsonl"), "{}");

        // Trigger discovery for sessionB with the SAME copilotPid
        tracker.HandleExternalSessionDiscovered(sessionB, copilotPid);

        // Assert: old binding removed, new binding added
        Assert.Equal(sessionA, removedSessionId);
        Assert.Null(tracker.GetCopilotHost(sessionA));
        Assert.NotNull(tracker.GetCopilotHost(sessionB));
        Assert.Equal(copilotPid, tracker.GetCopilotHost(sessionB)?.CopilotPid);

        // Cleanup
        try { Directory.Delete(sessionDirA, true); } catch { }
        try { Directory.Delete(sessionDirB, true); } catch { }
    }

    [Fact]
    public void Discovery_ForPidAlreadyBoundToSameSession_NoChange()
    {
        // Bug D: idempotency check — if the same (sessionId, copilotPid) pair is discovered again,
        // no events should fire and the binding should remain unchanged.

        var tracker = new ActiveStatusTracker();
        int removeCount = 0;
        tracker.CopilotHostRemoved += (_) => removeCount++;

        var sessionA = "aaaaaaaa-1111-2222-3333-444444444444";
        var copilotPid = Process.GetCurrentProcess().Id;

        // Create fake events.jsonl for sessionA in Program.SessionStateDir so the liveness gate passes
        var sessionDirA = Path.Combine(Program.SessionStateDir, sessionA);
        Directory.CreateDirectory(sessionDirA);
        File.WriteAllText(Path.Combine(sessionDirA, "events.jsonl"), "{}");

        // Manually inject the binding
        var hostInfoA = new CopilotHostInfo(
            HostHwnd: IntPtr.Zero,
            HostPid: 1234,
            CopilotPid: copilotPid,
            HostProcessName: "WindowsTerminal.exe",
            HostKindLabel: "Windows Terminal");
        tracker.GetType()
            .GetMethod("SetCopilotHost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(tracker, [sessionA, hostInfoA]);

        var originalHost = tracker.GetCopilotHost(sessionA);
        Assert.NotNull(originalHost);

        // Trigger discovery for the SAME sessionA + copilotPid
        tracker.HandleExternalSessionDiscovered(sessionA, copilotPid);

        // Assert: no removal event, copilotPid unchanged (but HostInfo might be re-resolved)
        Assert.Equal(0, removeCount);
        var currentHost = tracker.GetCopilotHost(sessionA);
        Assert.NotNull(currentHost);
        Assert.Equal(copilotPid, currentHost?.CopilotPid);

        // Cleanup
        try { Directory.Delete(sessionDirA, true); } catch { }
    }
}
