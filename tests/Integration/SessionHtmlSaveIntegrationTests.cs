using Microsoft.Playwright;

namespace CopilotBooster.IntegrationTests.Integration;

public class SessionHtmlSaveIntegrationTests : IAsyncDisposable
{
    private string? _signalsFilePath;

    public async ValueTask DisposeAsync()
    {
        if (this._signalsFilePath != null && File.Exists(this._signalsFilePath))
        {
            File.Delete(this._signalsFilePath);
        }

        await ValueTask.CompletedTask;
    }

    private static string FindSessionHtml()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "session.html"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "session.html")),
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("session.html not found");
    }

    private static string ToFileUri(string path) =>
        $"file:///{path.Replace('\\', '/')}";

    [Fact]
    public async Task SessionHtml_LoadsWithCorrectTitle()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();

        var sessionHtml = FindSessionHtml();
        await page.GotoAsync($"{ToFileUri(sessionHtml)}#test-session-id");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var title = await page.TitleAsync();
        Assert.Contains("CB Session", title);
        Assert.Contains("test-session-id", title);
    }

    [Fact]
    public async Task SessionHtml_SaveButtonClick_ChangesTitle()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();

        var sessionHtml = FindSessionHtml();
        await page.GotoAsync($"{ToFileUri(sessionHtml)}#test-save-id");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await page.ClickAsync("#save-link");

        var title = await page.TitleAsync();
        Assert.Contains("::Save", title);
    }

    [Fact]
    public async Task SessionHtml_SaveButtonClick_ShowsSavingState()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();

        var sessionHtml = FindSessionHtml();
        await page.GotoAsync($"{ToFileUri(sessionHtml)}#test-saving-id");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await page.ClickAsync("#save-link");

        var buttonText = await page.TextContentAsync("#save-link");
        Assert.Contains("Saving", buttonText);
    }

    [Fact]
    public async Task SessionHtml_SaveCompletes_WhenSignalProvided()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Args = ["--allow-file-access-from-files"],
        });
        var page = await browser.NewPageAsync();

        var sessionHtml = FindSessionHtml();
        var sessionDir = Path.GetDirectoryName(sessionHtml)!;
        this._signalsFilePath = Path.Combine(sessionDir, "session-signals.js");

        // Write initial empty signals file so the first poll doesn't error
        File.WriteAllText(this._signalsFilePath, "window.__sessionSignals = {};");

        await page.GotoAsync($"{ToFileUri(sessionHtml)}#test-signal-id");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await page.ClickAsync("#save-link");

        // Get requestedAt timestamp and write signal with a value exceeding it
        var requestedAt = await page.EvaluateAsync<long>("requestedAt");
        var signalTs = requestedAt + 1000;
        File.WriteAllText(
            this._signalsFilePath,
            $"window.__sessionSignals = {{\"test-signal-id\": {signalTs}}};");

        // Trigger pollSignals directly to avoid waiting for the 3s timer
        await page.EvaluateAsync("pollSignals()");

        // Wait for button text to reset to "Save Tabs"
        await page.WaitForFunctionAsync(
            "() => document.getElementById('save-link').textContent.includes('Save Tabs')",
            null,
            new() { Timeout = 10000 });

        var buttonText = await page.TextContentAsync("#save-link");
        Assert.Contains("Save Tabs", buttonText);
    }

    [Fact]
    public async Task SessionHtml_LoadsWithSessionName()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();

        var sessionHtml = FindSessionHtml();
        await page.GotoAsync($"{ToFileUri(sessionHtml)}#session-id/My%20Session%20Name");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var title = await page.TitleAsync();
        Assert.Contains("My Session Name", title);

        var sessionName = await page.TextContentAsync("#session-name");
        Assert.Equal("My Session Name", sessionName);
    }

    [Fact]
    public async Task SessionHtml_SaveCompletesViaAppCodePath_WriteSessionSignals()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Args = ["--allow-file-access-from-files"],
        });
        var page = await browser.NewPageAsync();

        var sessionHtml = FindSessionHtml();
        var sessionDir = Path.GetDirectoryName(sessionHtml)!;
        this._signalsFilePath = Path.Combine(sessionDir, "session-signals.js");

        // Write initial empty signals
        EdgeWorkspaceService.WriteSessionSignals(new Dictionary<string, long>());

        await page.GotoAsync($"{ToFileUri(sessionHtml)}#test-app-path-id");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Click save — JS sets title to ::Save
        await page.ClickAsync("#save-link");
        var title = await page.TitleAsync();
        Assert.Contains("::Save", title);

        // Simulate what the C# app does when it detects ::Save:
        // 1. It would call GetTabUrls() (not testable without real Edge)
        // 2. It calls SaveTabs() (not testable without real tabs)
        // 3. It calls WriteSessionSignals with a lastSaved timestamp — THIS is the app code path
        var requestedAt = await page.EvaluateAsync<long>("requestedAt");
        var lastSaved = new Dictionary<string, long> { ["test-app-path-id"] = requestedAt + 1000 };
        EdgeWorkspaceService.WriteSessionSignals(lastSaved);

        // Trigger pollSignals and verify the button resets
        await page.EvaluateAsync("pollSignals()");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('save-link').textContent.includes('Save Tabs')",
            null,
            new() { Timeout = 10000 });

        var buttonText = await page.TextContentAsync("#save-link");
        Assert.Contains("Save Tabs", buttonText);

        // Verify beforeunload guard is deactivated after save completes
        var isGuardActive = await page.EvaluateAsync<bool>("beforeUnloadActive");
        Assert.False(isGuardActive, "beforeunload guard should be inactive after save completes");
    }

    [Fact]
    public async Task SessionHtml_BeforeUnloadGuard_ActiveDuringSaving()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();

        var sessionHtml = FindSessionHtml();
        await page.GotoAsync($"{ToFileUri(sessionHtml)}#test-guard-id");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await page.ClickAsync("#save-link");

        var isGuardActive = await page.EvaluateAsync<bool>("beforeUnloadActive");
        Assert.True(isGuardActive);
    }
}
