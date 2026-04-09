namespace CopilotBooster.Tests.Services;

/// <summary>
/// Tests for the WelcomePopupDismissed setting persistence.
/// </summary>
public sealed class WelcomePopupDismissedTests : IDisposable
{
    private readonly string _tempFile;

    public WelcomePopupDismissedTests()
    {
        this._tempFile = Path.Combine(Path.GetTempPath(), $"settings-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(this._tempFile))
            {
                File.Delete(this._tempFile);
            }
        }
        catch { }
    }

    [Fact]
    public void WelcomePopupDismissed_DefaultFalse()
    {
        var settings = LauncherSettings.CreateDefault();
        Assert.False(settings.WelcomePopupDismissed);
    }

    [Fact]
    public void WelcomePopupDismissed_PersistsAfterSave()
    {
        var settings = LauncherSettings.CreateDefault();
        settings.WelcomePopupDismissed = true;
        settings.Save(this._tempFile);

        var reloaded = LauncherSettings.Load(this._tempFile);
        Assert.True(reloaded.WelcomePopupDismissed);
    }

    [Fact]
    public void WelcomePopupDismissed_FalsePersists()
    {
        var settings = LauncherSettings.CreateDefault();
        settings.WelcomePopupDismissed = false;
        settings.Save(this._tempFile);

        var reloaded = LauncherSettings.Load(this._tempFile);
        Assert.False(reloaded.WelcomePopupDismissed);
    }
}
