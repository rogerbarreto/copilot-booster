public sealed class CacheFreeArchitectureGuardTests
{
    [Fact]
    public void EventsJournalService_Source_DoesNotContainLatestCwdCache()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Services", "EventsJournalService.cs"));

        Assert.True(File.Exists(sourcePath), $"EventsJournalService.cs not found at {sourcePath}");
        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("_latestCwdBySessionId", source);
    }

    [Fact]
    public void EventsJournalService_Source_DoesNotContainCwdCacheImplementationShape()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Services", "EventsJournalService.cs"));

        Assert.True(File.Exists(sourcePath), $"EventsJournalService.cs not found at {sourcePath}");
        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("ConcurrentDictionary<string, string>", source);
        Assert.DoesNotContain("ReadAndCacheLatestCwd", source);
        Assert.DoesNotContain("raiseCwdChanged", source);
    }

    [Fact]
    public void EventsJournalService_Source_DoesNotContainCachedState()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Services", "EventsJournalService.cs"));

        Assert.True(File.Exists(sourcePath), $"EventsJournalService.cs not found at {sourcePath}");
        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("CachedState", source);
        Assert.DoesNotContain("_cache", source);
    }

    [Fact]
    public void EventsJournalService_Source_DoesNotExposeTryGetLatestCwd()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Services", "EventsJournalService.cs"));

        Assert.True(File.Exists(sourcePath), $"EventsJournalService.cs not found at {sourcePath}");
        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("TryGetLatestCwd", source);
    }

    [Fact]
    public void EventsJournalService_Source_DoesNotExposeApplyLiveCwdOverlay()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Services", "EventsJournalService.cs"));

        Assert.True(File.Exists(sourcePath), $"EventsJournalService.cs not found at {sourcePath}");
        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("ApplyLiveCwdOverlay", source);
    }

    [Fact]
    public void EventsJournalService_Source_DoesNotPersistEventsCacheJson()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Services", "EventsJournalService.cs"));

        Assert.True(File.Exists(sourcePath), $"EventsJournalService.cs not found at {sourcePath}");
        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("events-cache.json", source);
    }

    [Fact]
    public void MainForm_OnDebouncedRefreshAsync_DoesNotCallApplyLiveCwdOverlay()
    {
        var mainFormPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Forms", "MainForm.cs"));

        Assert.True(File.Exists(mainFormPath), $"MainForm.cs not found at {mainFormPath}");
        var mainFormContent = File.ReadAllText(mainFormPath);
        Assert.DoesNotContain("ApplyLiveCwdOverlay", mainFormContent);
    }

    [Fact]
    public void MainForm_RefreshBackgroundCoreAsync_DoesNotCallApplyLiveCwdOverlay()
    {
        var mainFormPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Forms", "MainForm.cs"));

        Assert.True(File.Exists(mainFormPath), $"MainForm.cs not found at {mainFormPath}");
        var mainFormContent = File.ReadAllText(mainFormPath);
        
        var refreshMethod = ExtractMethod(mainFormContent, "RefreshBackgroundCoreAsync");
        Assert.NotNull(refreshMethod);
        Assert.DoesNotContain("ApplyLiveCwdOverlay", refreshMethod);
    }

    [Fact]
    public void MainForm_WorkspaceWatcherHandler_OnlyCallsRequestRefresh()
    {
        var mainFormPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Forms", "MainForm.cs"));

        Assert.True(File.Exists(mainFormPath), $"MainForm.cs not found at {mainFormPath}");
        var mainFormContent = File.ReadAllText(mainFormPath);

        var workspaceChangedIndex = mainFormContent.IndexOf("this._workspaceWatcher.WorkspaceChanged +=", StringComparison.Ordinal);
        Assert.True(workspaceChangedIndex > 0, "WorkspaceChanged handler not found in MainForm.cs");

        var handlerStart = workspaceChangedIndex;
        var handlerEnd = mainFormContent.IndexOf("};", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerEnd > handlerStart, "WorkspaceChanged handler lambda closing not found");

        var handler = mainFormContent.Substring(handlerStart, handlerEnd - handlerStart + 2);

        Assert.Contains("RequestRefresh", handler);
        Assert.DoesNotContain("InvalidateLiveCwd", handler);
        Assert.DoesNotContain("TryGetLatestCwd", handler);
        Assert.DoesNotContain("_cache", handler);
    }

    [Fact]
    public void MainForm_EventsJournalLatestCwdChangedHandler_IsRemovedOrIsOnlyATrigger()
    {
        var mainFormPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "src", "Forms", "MainForm.cs"));

        Assert.True(File.Exists(mainFormPath), $"MainForm.cs not found at {mainFormPath}");
        var mainFormContent = File.ReadAllText(mainFormPath);

        var latestCwdChangedIndex = mainFormContent.IndexOf("LatestCwdChanged", StringComparison.Ordinal);
        
        if (latestCwdChangedIndex == -1)
        {
            return;
        }

        var handlerIndex = mainFormContent.IndexOf("OnLatestCwdChanged", StringComparison.Ordinal);
        if (handlerIndex == -1)
        {
            return;
        }

        var handlerMethod = ExtractMethod(mainFormContent, "OnLatestCwdChanged");
        Assert.NotNull(handlerMethod);

        Assert.Contains("RequestRefresh", handlerMethod);
        Assert.DoesNotContain("_cachedSessions[", handlerMethod);
        Assert.DoesNotContain("session.Cwd =", handlerMethod);
        Assert.DoesNotContain("session.Folder =", handlerMethod);
    }

    [Fact]
    public void Tests_Source_DoesNotReferenceRemovedLiveCwdOverlayApis()
    {
        var testsRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "tests"));

        Assert.True(Directory.Exists(testsRoot), $"tests directory not found at {testsRoot}");

        var offenders = Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFileName(path), nameof(CacheFreeArchitectureGuardTests) + ".cs", StringComparison.Ordinal))
            .Select(path => new
            {
                Path = path,
                Source = File.ReadAllText(path)
            })
            .Where(file =>
                file.Source.Contains("ApplyLiveCwdOverlay", StringComparison.Ordinal)
                || file.Source.Contains("TryGetLatestCwd", StringComparison.Ordinal)
                || file.Source.Contains("LatestCwdChanged", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string? ExtractMethod(string source, string methodName)
    {
        var methodStart = source.IndexOf($"void {methodName}", StringComparison.Ordinal);
        if (methodStart == -1)
        {
            methodStart = source.IndexOf($"async void {methodName}", StringComparison.Ordinal);
        }
        if (methodStart == -1)
        {
            methodStart = source.IndexOf($"Task {methodName}", StringComparison.Ordinal);
        }
        if (methodStart == -1)
        {
            methodStart = source.IndexOf($"async Task {methodName}", StringComparison.Ordinal);
        }
        if (methodStart == -1)
        {
            return null;
        }

        var braceCount = 0;
        var methodBodyStart = source.IndexOf('{', methodStart);
        if (methodBodyStart == -1)
        {
            return null;
        }

        for (var i = methodBodyStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                braceCount++;
            }
            else if (source[i] == '}')
            {
                braceCount--;
                if (braceCount == 0)
                {
                    return source.Substring(methodBodyStart, i - methodBodyStart + 1);
                }
            }
        }

        return null;
    }
}
