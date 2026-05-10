namespace CopilotBooster.Tests.Services;

/// <summary>
/// Tests that HandleExternalSessionDiscovered gates on SessionPidLivenessValidator
/// BEFORE calling CopilotHostResolver (T1 watcher path). Note: HandleExternalSessionDiscovered
/// calls the real-FS overload directly, NOT the injected _isSessionLiveForCopilotPid callback.
/// </summary>
public sealed class HandleExternalSessionDiscoveredGateTests
{
    [Fact]
    public void HandleExternalSessionDiscovered_NoEventsJsonl_DoesNotBindHost()
    {
        // HandleExternalSessionDiscovered calls SessionPidLivenessValidator.IsLive directly (real-FS).
        // For a fake session ID with no events.jsonl and a non-existent PID,
        // the validator returns false and the binding should be rejected.
        var tracker = new ActiveStatusTracker();

        // Use a fake session ID that won't have events.jsonl and a fake PID
        tracker.HandleExternalSessionDiscovered("00000000-0000-0000-0000-000000000000", 99999);

        // Host should not be bound because validator gate rejects it
        Assert.Null(tracker.GetCopilotHost("00000000-0000-0000-0000-000000000000"));
    }
}
