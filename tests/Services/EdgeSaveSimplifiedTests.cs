using System.Reflection;

namespace CopilotBooster.Tests.Services;

public sealed class EdgeSaveSimplifiedTests : IDisposable
{
    private readonly string _signalsPath = Path.Combine(AppContext.BaseDirectory, "session-signals.js");
    private readonly string _testSessionId = $"test-edge-save-{Guid.NewGuid()}";

    public void Dispose()
    {
        // Clean up session-signals.js if written
        if (File.Exists(this._signalsPath))
        {
            File.Delete(this._signalsPath);
        }

        // Clean up test session directory
        var sessionDir = SessionStateService.GetSessionDir(this._testSessionId);
        if (Directory.Exists(sessionDir))
        {
            Directory.Delete(sessionDir, recursive: true);
        }
    }

    // ── WriteSessionSignals ───────────────────────────────────────────

    [Fact]
    public void WriteSessionSignals_WritesLastSavedTimestamps()
    {
        var data = new Dictionary<string, long>
        {
            ["session-a"] = 1700000000000,
            ["session-b"] = 1700000003000
        };

        EdgeWorkspaceService.WriteSessionSignals(data);

        Assert.True(File.Exists(this._signalsPath));
        var content = File.ReadAllText(this._signalsPath);
        Assert.Contains("window.__sessionSignals", content);
        Assert.Contains("session-a", content);
        Assert.Contains("1700000000000", content);
        Assert.Contains("session-b", content);
        Assert.Contains("1700000003000", content);
        Assert.Contains("window.__signalInterval = 3000", content);
    }

    [Fact]
    public void WriteSessionSignals_CustomInterval_WritesInterval()
    {
        var data = new Dictionary<string, long> { ["s1"] = 100 };

        EdgeWorkspaceService.WriteSessionSignals(data, signalIntervalMs: 5000);

        var content = File.ReadAllText(this._signalsPath);
        Assert.Contains("window.__signalInterval = 5000", content);
    }

    [Fact]
    public void WriteSessionSignals_EmptyDict_WritesEmptyObject()
    {
        EdgeWorkspaceService.WriteSessionSignals([]);

        var content = File.ReadAllText(this._signalsPath);
        Assert.Contains("window.__sessionSignals = {}", content);
    }

    // ── EdgeTabPersistenceService removed methods ─────────────────────

    [Fact]
    public void EdgeTabPersistenceService_SaveTabTitleHash_DoesNotExist()
    {
        var method = typeof(EdgeTabPersistenceService).GetMethod(
            "SaveTabTitleHash", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Null(method);
    }

    [Fact]
    public void EdgeTabPersistenceService_LoadTabTitleHash_DoesNotExist()
    {
        var method = typeof(EdgeTabPersistenceService).GetMethod(
            "LoadTabTitleHash", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Null(method);
    }

    [Fact]
    public void EdgeTabPersistenceService_ComputeSavedTabHash_DoesNotExist()
    {
        var method = typeof(EdgeTabPersistenceService).GetMethod(
            "ComputeSavedTabHash", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Null(method);
    }

    // ── EdgeWorkspaceService removed methods ──────────────────────────

    [Fact]
    public void EdgeWorkspaceService_CheckForTabChanges_DoesNotExist()
    {
        var method = typeof(EdgeWorkspaceService).GetMethod(
            "CheckForTabChanges", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Null(method);
    }

    [Fact]
    public void EdgeWorkspaceService_GetTabNameHash_DoesNotExist()
    {
        var method = typeof(EdgeWorkspaceService).GetMethod(
            "GetTabNameHash", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Null(method);
    }

    [Fact]
    public void EdgeWorkspaceService_HasUnsavedChanges_DoesNotExist()
    {
        var method = typeof(EdgeWorkspaceService).GetMethod(
            "HasUnsavedChanges", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Null(method);
    }

    [Fact]
    public void EdgeWorkspaceService_DetectSaveSignal_DoesNotExist()
    {
        var method = typeof(EdgeWorkspaceService).GetMethod(
            "DetectSaveSignal", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Null(method);
    }

    // ── SaveTabs + LoadTabs round-trip ─────────────────────────────────

    [Fact]
    public void SaveTabs_LoadTabs_RoundTrip()
    {
        EdgeTabPersistenceService.SaveTabs(this._testSessionId, ["https://example.com", "https://github.com"]);

        var loaded = EdgeTabPersistenceService.LoadTabs(this._testSessionId);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("https://example.com", loaded[0]);
        Assert.Equal("https://github.com", loaded[1]);
    }

    [Fact]
    public void HasSavedTabs_AfterSave_ReturnsTrue()
    {
        Assert.False(EdgeTabPersistenceService.HasSavedTabs(this._testSessionId));

        EdgeTabPersistenceService.SaveTabs(this._testSessionId, ["https://example.com"]);

        Assert.True(EdgeTabPersistenceService.HasSavedTabs(this._testSessionId));
    }

    [Fact]
    public void LoadTabs_NoSavedTabs_ReturnsEmptyList()
    {
        var loaded = EdgeTabPersistenceService.LoadTabs(this._testSessionId);
        Assert.Empty(loaded);
    }

    // ── ExtractSessionId regression ───────────────────────────────────

    [Fact]
    public void ExtractSessionId_SimpleGuid_ReturnsId()
    {
        var result = EdgeWorkspaceService.ExtractSessionId("CB Session [abc-123]");
        Assert.Equal("abc-123", result);
    }
}
