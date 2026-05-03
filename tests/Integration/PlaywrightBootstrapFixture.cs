namespace CopilotBooster.IntegrationTests.Integration;

[CollectionDefinition(Name)]
public sealed class PlaywrightBootstrapCollection : ICollectionFixture<PlaywrightBootstrapFixture>
{
    public const string Name = "PlaywrightBootstrap";
}

public sealed class PlaywrightBootstrapFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim s_bootstrapLock = new(1, 1);
    private static bool s_bootstrapped;

    public async ValueTask InitializeAsync()
    {
        if (s_bootstrapped)
        {
            return;
        }

        await s_bootstrapLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (s_bootstrapped)
            {
                return;
            }

            if (!await CanLaunchChromiumAsync().ConfigureAwait(false))
            {
                InstallChromiumOrSkip();
            }

            s_bootstrapped = true;
        }
        finally
        {
            s_bootstrapLock.Release();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<bool> CanLaunchChromiumAsync()
    {
        try
        {
            using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true }).ConfigureAwait(false);
            return true;
        }
        catch (PlaywrightException ex) when (IsMissingBrowserException(ex))
        {
            return false;
        }
    }

    private static void InstallChromiumOrSkip()
    {
        try
        {
            var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (exitCode != 0)
            {
                Assert.Skip($"Playwright chromium auto-bootstrap failed with exit code {exitCode}.");
            }
        }
        catch (Exception ex)
        {
            Assert.Skip($"Playwright chromium auto-bootstrap failed: {ex.Message}");
        }
    }

    private static bool IsMissingBrowserException(PlaywrightException ex) =>
        ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("playwright install", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Please run the following command to download new browsers", StringComparison.OrdinalIgnoreCase);
}
