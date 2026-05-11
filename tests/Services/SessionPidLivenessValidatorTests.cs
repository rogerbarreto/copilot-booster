namespace CopilotBooster.Tests.Services;

/// <summary>
/// Unit tests for SessionPidLivenessValidator's pure DateTime overload.
/// Tests the invariant that a session-pid binding is live iff
/// events.jsonl.mtime + fudge >= copilot.StartTime.
/// </summary>
public sealed class SessionPidLivenessValidatorTests
{
    [Fact]
    public void IsLive_MtimeAfterStartTime_ReturnsTrue()
    {
        // Live binding: events.jsonl written 1 hour after copilot started.
        var startTime = new DateTime(2026, 5, 9, 22, 38, 0, DateTimeKind.Utc);
        var mtime = startTime.AddHours(1);

        var result = SessionPidLivenessValidator.IsLive(
            eventsJsonlMtimeUtc: mtime,
            copilotStartTimeUtc: startTime,
            fudgeSeconds: 5);

        Assert.True(result);
    }

    [Fact]
    public void IsLive_MtimeBeforeStartTime_ReturnsFalse()
    {
        // Roger's exact Bug B scenario: session 0bb1099b events.jsonl mtime = May 9 15:58 UTC,
        // but pid 39992 started May 9 22:38 UTC (8 hours later). The pid switched sessions
        // via /resume and never wrote to 0bb1099b again. The binding is stale.
        var mtime = new DateTime(2026, 5, 9, 15, 58, 0, DateTimeKind.Utc);
        var startTime = new DateTime(2026, 5, 9, 22, 38, 0, DateTimeKind.Utc);

        var result = SessionPidLivenessValidator.IsLive(
            eventsJsonlMtimeUtc: mtime,
            copilotStartTimeUtc: startTime,
            fudgeSeconds: 5);

        Assert.False(result);
    }

    [Fact]
    public void IsLive_MtimeWithinFudgeFactor_ReturnsTrue()
    {
        // Clock skew: mtime is 3 seconds before startTime, but default fudge=5 covers it.
        var startTime = new DateTime(2026, 5, 9, 22, 38, 0, DateTimeKind.Utc);
        var mtime = startTime.AddSeconds(-3);

        var result = SessionPidLivenessValidator.IsLive(
            eventsJsonlMtimeUtc: mtime,
            copilotStartTimeUtc: startTime,
            fudgeSeconds: 5);

        Assert.True(result);
    }

    [Fact]
    public void IsLive_MtimeJustBeyondFudge_ReturnsFalse()
    {
        // mtime is 6 seconds before startTime, exceeds default fudge=5.
        var startTime = new DateTime(2026, 5, 9, 22, 38, 0, DateTimeKind.Utc);
        var mtime = startTime.AddSeconds(-6);

        var result = SessionPidLivenessValidator.IsLive(
            eventsJsonlMtimeUtc: mtime,
            copilotStartTimeUtc: startTime,
            fudgeSeconds: 5);

        Assert.False(result);
    }

    [Fact]
    public void IsLive_CustomFudge_Honored()
    {
        // Custom fudge=120s covers a 100-second clock skew.
        var startTime = new DateTime(2026, 5, 9, 22, 38, 0, DateTimeKind.Utc);
        var mtime = startTime.AddSeconds(-100);

        var result = SessionPidLivenessValidator.IsLive(
            eventsJsonlMtimeUtc: mtime,
            copilotStartTimeUtc: startTime,
            fudgeSeconds: 120);

        Assert.True(result);
    }
}
