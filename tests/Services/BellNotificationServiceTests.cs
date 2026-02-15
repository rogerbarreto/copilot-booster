public sealed class BellNotificationServiceTests : IDisposable
{
    private readonly NotifyIcon _trayIcon;

    public BellNotificationServiceTests()
    {
        this._trayIcon = new NotifyIcon();
    }

    public void Dispose()
    {
        this._trayIcon.Dispose();
    }

    [Fact]
    public void LastNotifiedSessionId_StartsNull()
    {
        var service = new BellNotificationService(this._trayIcon, () => true);

        Assert.Null(service.LastNotifiedSessionId);
    }

    [Fact]
    public void SeedStartupSessions_PreventsFalseNotifications()
    {
        var service = new BellNotificationService(this._trayIcon, () => true);
        service.SeedStartupSessions(new[] { "session-a", "session-b" });

        var snapshot = new ActiveStatusSnapshot(
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["session-a"] = "Session A",
                ["session-b"] = "Session B"
            },
            new Dictionary<string, string>
            {
                ["session-a"] = "bell",
                ["session-b"] = "bell"
            });

        service.CheckAndNotify(snapshot);

        // Seeded sessions should not trigger notifications, so LastNotifiedSessionId stays null
        Assert.Null(service.LastNotifiedSessionId);
    }

    [Fact]
    public void SeedStartupSessions_NewSessionStillNotifies()
    {
        var service = new BellNotificationService(this._trayIcon, () => true);
        service.SeedStartupSessions(new[] { "session-a" });

        var snapshot = new ActiveStatusSnapshot(
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["session-a"] = "Session A",
                ["session-new"] = "New Session"
            },
            new Dictionary<string, string>
            {
                ["session-a"] = "bell",
                ["session-new"] = "bell"
            });

        service.CheckAndNotify(snapshot);

        Assert.Equal("session-new", service.LastNotifiedSessionId);
    }
}
