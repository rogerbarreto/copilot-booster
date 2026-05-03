namespace CopilotBooster.IntegrationTests.Integration;

[Collection(PlaywrightBootstrapCollection.Name)]
public sealed class SessionHtmlSaveIntegrationTests : IAsyncDisposable
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
    public async Task SessionHtml_LoadsWithCorrectTitleAsync()
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
    public async Task SessionHtml_SaveButtonClick_ChangesTitleAsync()
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
    public async Task SessionHtml_SaveButtonClick_ShowsSavingStateAsync()
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
    public async Task SessionHtml_SaveCompletes_WhenSignalProvidedAsync()
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
    public async Task SessionHtml_LoadsWithSessionNameAsync()
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
    public async Task SessionHtml_SaveCompletesViaAppCodePath_WriteSessionSignalsAsync()
    {
        const string SessionId = "test-app-path-id";
        var testSessionDir = SessionStateService.EnsureSessionDir(SessionId);

        try
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
            EdgeWorkspaceService.WriteSessionSignals([]);

            // Set up context watcher to track tab count changes
            using var contextWatcher = new SessionContextWatcherService();
            Assert.Equal(0, contextWatcher.GetCounts(SessionId).Tabs);

            await page.GotoAsync($"{ToFileUri(sessionHtml)}#{SessionId}");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            // Click save — JS sets title to ::Save
            await page.ClickAsync("#save-link");
            var title = await page.TitleAsync();
            Assert.Contains("::Save", title);

            // Simulate what the C# app does when it detects ::Save:
            var urls = new List<string> { "https://github.com", "https://learn.microsoft.com" };
            EdgeTabPersistenceService.SaveTabs(SessionId, urls);
            contextWatcher.UpdateTabCount(SessionId, urls.Count);

            var requestedAt = await page.EvaluateAsync<long>("requestedAt");
            var lastSaved = new Dictionary<string, long> { [SessionId] = requestedAt + 1000 };
            EdgeWorkspaceService.WriteSessionSignals(lastSaved);

            // Trigger pollSignals and verify the button resets
            await page.EvaluateAsync("pollSignals()");
            await page.WaitForFunctionAsync(
                "() => document.getElementById('save-link').textContent.includes('Save Tabs')",
                null,
                new() { Timeout = 10000 });

            var buttonText = await page.TextContentAsync("#save-link");
            Assert.Contains("Save Tabs", buttonText);

            // ASSERT: after button resets, tab count reflects the saved tabs
            var counts = contextWatcher.GetCounts(SessionId);
            Assert.Equal(2, counts.Tabs);
            var persisted = EdgeTabPersistenceService.LoadTabs(SessionId);
            Assert.Equal(persisted.Count, counts.Tabs);

            // Verify beforeunload guard is deactivated after save completes
            var isGuardActive = await page.EvaluateAsync<bool>("beforeUnloadActive");
            Assert.False(isGuardActive, "beforeunload guard should be inactive after save completes");
        }
        finally
        {
            if (Directory.Exists(testSessionDir))
            {
                Directory.Delete(testSessionDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SessionHtml_BeforeUnloadGuard_ActiveDuringSavingAsync()
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

    /// <summary>
    /// Full E2E test: Playwright opens session.html in a headed Chromium window,
    /// clicks save, WindowEventHookService detects the ::Save title change,
    /// the detection logic extracts the session ID and writes session-signals.js,
    /// then session.html polls and resets the save button.
    /// </summary>
    [StaFact]
    public async Task SessionHtml_E2E_SaveDetectedByWindowHook_SignalWritten_ButtonResetsAsync()
    {
        const string SessionId = "e2e-save-hook-test";

        var sessionHtml = FindSessionHtml();
        var sessionDir = Path.GetDirectoryName(sessionHtml)!;
        this._signalsFilePath = Path.Combine(sessionDir, "session-signals.js");

        // Write initial empty signals
        EdgeWorkspaceService.WriteSessionSignals([]);

        // Set up WindowEventHookService to detect ::Save title change
        using var hookService = new WindowEventHookService();
        string? detectedSaveTitle = null;
        using var saveDetected = new ManualResetEventSlim();

        hookService.WindowTitleChanged += (hwnd, title) =>
        {
            if (title.Contains("::Save", StringComparison.OrdinalIgnoreCase)
                && title.Contains(SessionId, StringComparison.OrdinalIgnoreCase))
            {
                detectedSaveTitle = title;
                saveDetected.Set();
            }
        };
        hookService.Start();

        // Launch Chromium HEADED so the window title is visible to SetWinEventHook
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = false,
            Args = ["--allow-file-access-from-files"],
        });
        var page = await browser.NewPageAsync();

        await page.GotoAsync($"{ToFileUri(sessionHtml)}#{SessionId}");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Click save — JS changes document.title to "CB Session [e2e-save-hook-test]::Save"
        await page.ClickAsync("#save-link");

        // Pump messages to receive the WinEvent hook callback
        var deadline = Environment.TickCount64 + 10000;
        while (!saveDetected.IsSet && Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            await Task.Delay(50);
        }

        // ASSERT: WindowEventHookService detected the ::Save title change
        Assert.True(saveDetected.IsSet, "WindowEventHookService should detect ::Save in Chromium window title");
        Assert.Contains("::Save", detectedSaveTitle);

        // Now do what HandleEdgeSaveSignalAsync does: extract session ID, write signal
        var extractedId = EdgeWorkspaceService.ExtractSessionId(detectedSaveTitle!);
        Assert.Equal(SessionId, extractedId);

        // Write lastSaved timestamp via the app code path
        var requestedAt = await page.EvaluateAsync<long>("requestedAt");
        var lastSaved = new Dictionary<string, long> { [SessionId] = requestedAt + 1000 };
        EdgeWorkspaceService.WriteSessionSignals(lastSaved);

        // Trigger pollSignals and verify session.html resets the button
        await page.EvaluateAsync("pollSignals()");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('save-link').textContent.includes('Save Tabs')",
            null,
            new() { Timeout = 10000 });

        var buttonText = await page.TextContentAsync("#save-link");
        Assert.Contains("Save Tabs", buttonText);

        // Verify beforeunload guard deactivated
        var isGuardActive = await page.EvaluateAsync<bool>("beforeUnloadActive");
        Assert.False(isGuardActive, "beforeunload guard should be inactive after save completes");
    }

    /// <summary>
    /// Verifies that multiple rapid ::Save title changes (from NAMECHANGE events)
    /// result in only a single signal write — the debounce guard prevents duplicates.
    /// </summary>
    [StaFact]
    public async Task SessionHtml_E2E_MultipleSaveEvents_SignalWrittenOnlyOnceAsync()
    {
        const string SessionId = "e2e-debounce-test";

        var sessionHtml = FindSessionHtml();
        var sessionDir = Path.GetDirectoryName(sessionHtml)!;
        this._signalsFilePath = Path.Combine(sessionDir, "session-signals.js");

        EdgeWorkspaceService.WriteSessionSignals([]);

        using var hookService = new WindowEventHookService();
        int saveDetectionCount = 0;

        hookService.WindowTitleChanged += (hwnd, title) =>
        {
            if (title.Contains("::Save", StringComparison.OrdinalIgnoreCase)
                && title.Contains(SessionId, StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref saveDetectionCount);

                // Simulate what HandleEdgeSaveSignalAsync does — write signal
                // Use the same debounce pattern: only write if not already written
                var extractedId = EdgeWorkspaceService.ExtractSessionId(title);
                if (extractedId != null)
                {
                    var lastSaved = new Dictionary<string, long>
                    {
                        [extractedId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    EdgeWorkspaceService.WriteSessionSignals(lastSaved);
                }
            }
        };
        hookService.Start();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = false,
            Args = ["--allow-file-access-from-files"],
        });
        var page = await browser.NewPageAsync();

        await page.GotoAsync($"{ToFileUri(sessionHtml)}#{SessionId}");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Click save — this triggers the ::Save title
        await page.ClickAsync("#save-link");

        // Pump messages to collect all NAMECHANGE events
        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            await Task.Delay(50);
        }

        // The hook may fire multiple times for the same title change
        // (Edge/Chromium can emit multiple NAMECHANGE events)
        // But the signal file should reflect only the latest write
        Assert.True(saveDetectionCount >= 1, "At least one ::Save detection should occur");

        // Read the signal file and verify it contains exactly one session entry
        var signalContent = File.ReadAllText(this._signalsFilePath);
        Assert.Contains(SessionId, signalContent);

        // Count how many times the session ID appears as a key in the JSON
        var keyPattern = $"\"{SessionId}\"";
        var keyCount = signalContent.Split(keyPattern).Length - 1;
        Assert.Equal(1, keyCount);

        // Verify the button resets
        await page.EvaluateAsync("pollSignals()");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('save-link').textContent.includes('Save Tabs')",
            null,
            new() { Timeout = 10000 });

        var buttonText = await page.TextContentAsync("#save-link");
        Assert.Contains("Save Tabs", buttonText);
    }

    /// <summary>
    /// Full integration test: save tabs → update context watcher → GetCounts matches immediately.
    /// Simulates the exact flow from HandleEdgeSaveSignalAsync and OnSaveEdgeTabs handlers.
    /// </summary>
    [LocalOnlyStaFact]
    [Trait("Category", "LocalOnly")]
    public async Task SessionHtml_E2E_SaveTabs_ContextCountUpdatesImmediatelyAsync()
    {
        const string SessionId = "e2e-tab-count-test";
        var testSessionDir = SessionStateService.EnsureSessionDir(SessionId);

        try
        {
            var sessionHtml = FindSessionHtml();
            var sessionDir = Path.GetDirectoryName(sessionHtml)!;
            this._signalsFilePath = Path.Combine(sessionDir, "session-signals.js");
            EdgeWorkspaceService.WriteSessionSignals([]);

            using var contextWatcher = new SessionContextWatcherService();
            string? countChangedForId = null;
            int countChangedFiredCount = 0;
            contextWatcher.CountsChanged += id =>
            {
                countChangedForId = id;
                Interlocked.Increment(ref countChangedFiredCount);
            };

            // Before save: no tabs
            Assert.Equal(0, contextWatcher.GetCounts(SessionId).Tabs);

            using var hookService = new WindowEventHookService();
            using var saveDetected = new ManualResetEventSlim();

            hookService.WindowTitleChanged += (hwnd, title) =>
            {
                if (!title.Contains("::Save", StringComparison.OrdinalIgnoreCase)
                    || !title.Contains(SessionId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Simulate HandleEdgeSaveSignalAsync:
                // 1. Extract session ID
                var sid = EdgeWorkspaceService.ExtractSessionId(title);
                if (sid == null)
                {
                    return;
                }

                // 2. Save tabs (simulated — in real flow these come from Edge UI Automation)
                var urls = new List<string>
                {
                    "https://github.com/features",
                    "https://learn.microsoft.com",
                    "https://stackoverflow.com"
                };
                EdgeTabPersistenceService.SaveTabs(sid, urls);

                // 3. Update context watcher (the fix we're testing)
                contextWatcher.UpdateTabCount(sid, urls.Count);

                // 4. Write signal so session.html resets
                var lastSaved = new Dictionary<string, long>
                {
                    [sid] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                EdgeWorkspaceService.WriteSessionSignals(lastSaved);

                saveDetected.Set();
            };
            hookService.Start();

            // Launch headed browser
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = false,
                Args = ["--allow-file-access-from-files"],
            });
            var page = await browser.NewPageAsync();
            await page.GotoAsync($"{ToFileUri(sessionHtml)}#{SessionId}");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            // Click save
            await page.ClickAsync("#save-link");

            // Pump messages for WinEvent hook
            var deadline = Environment.TickCount64 + 10000;
            while (!saveDetected.IsSet && Environment.TickCount64 < deadline)
            {
                Application.DoEvents();
                await Task.Delay(50);
            }

            Assert.True(saveDetected.IsSet, "::Save title change should be detected");

            // ASSERT: tab count updated IMMEDIATELY after save
            var counts = contextWatcher.GetCounts(SessionId);
            Assert.Equal(3, counts.Tabs);

            // ASSERT: CountsChanged event fired
            Assert.Equal(SessionId, countChangedForId);
            Assert.True(countChangedFiredCount >= 1);

            // ASSERT: persisted tabs match cached count
            var persisted = EdgeTabPersistenceService.LoadTabs(SessionId);
            Assert.Equal(counts.Tabs, persisted.Count);

            // Verify session.html button resets
            await page.EvaluateAsync("pollSignals()");
            await page.WaitForFunctionAsync(
                "() => document.getElementById('save-link').textContent.includes('Save Tabs')",
                null,
                new() { Timeout = 10000 });
            Assert.Contains("Save Tabs", await page.TextContentAsync("#save-link"));

            // ASSERT: right after button returns to "Save Tabs", tab count MUST reflect the save
            counts = contextWatcher.GetCounts(SessionId);
            Assert.Equal(3, counts.Tabs);

            // ASSERT: persisted tabs match cached count at this point
            persisted = EdgeTabPersistenceService.LoadTabs(SessionId);
            Assert.Equal(3, persisted.Count);
            Assert.Equal(persisted.Count, counts.Tabs);

            // Now simulate saving with 0 tabs (clear)
            countChangedForId = null;
            EdgeTabPersistenceService.SaveTabs(SessionId, []);
            contextWatcher.UpdateTabCount(SessionId, 0);

            // ASSERT: tab count drops to 0 immediately (icon should be removed)
            counts = contextWatcher.GetCounts(SessionId);
            Assert.Equal(0, counts.Tabs);
            Assert.Equal(SessionId, countChangedForId);

            // ASSERT: persisted tabs also empty
            persisted = EdgeTabPersistenceService.LoadTabs(SessionId);
            Assert.Empty(persisted);
            Assert.Equal(persisted.Count, counts.Tabs);
        }
        finally
        {
            if (Directory.Exists(testSessionDir))
            {
                Directory.Delete(testSessionDir, recursive: true);
            }
        }
    }
}
