namespace CopilotBooster.Tests.Services;

public sealed class SessionNameOverrideServiceTests
{
    private static string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"name-override-test-{Guid.NewGuid()}.json");
        return path;
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsEmptyDictionary()
    {
        var result = SessionNameOverrideService.Load(@"C:\nonexistent\name-override.json");
        Assert.Empty(result);
    }

    [Fact]
    public void Set_StoresNameAndResolvedFlag()
    {
        var file = CreateTempFile();
        try
        {
            SessionNameOverrideService.Set(file, "session-1", "My Name", true);
            var entry = SessionNameOverrideService.Get(file, "session-1");
            Assert.NotNull(entry);
            Assert.Equal("My Name", entry!.Name);
            Assert.True(entry.ResolvedFromUserMessage);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Set_NullName_RemovesEntry()
    {
        var file = CreateTempFile();
        try
        {
            SessionNameOverrideService.Set(file, "session-1", "Name", false);
            SessionNameOverrideService.Set(file, "session-1", null, false);
            var entry = SessionNameOverrideService.Get(file, "session-1");
            Assert.Null(entry);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Set_EmptyName_RemovesEntry()
    {
        var file = CreateTempFile();
        try
        {
            SessionNameOverrideService.Set(file, "session-1", "Name", false);
            SessionNameOverrideService.Set(file, "session-1", "", false);
            var entry = SessionNameOverrideService.Get(file, "session-1");
            Assert.Null(entry);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Set_OverwritesExisting()
    {
        var file = CreateTempFile();
        try
        {
            SessionNameOverrideService.Set(file, "session-1", "Original", false);
            SessionNameOverrideService.Set(file, "session-1", "Updated", true);
            var entry = SessionNameOverrideService.Get(file, "session-1");
            Assert.NotNull(entry);
            Assert.Equal("Updated", entry!.Name);
            Assert.True(entry.ResolvedFromUserMessage);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        var file = CreateTempFile();
        try
        {
            SessionNameOverrideService.Set(file, "session-1", "Name", false);
            var entry = SessionNameOverrideService.Get(file, "unknown");
            Assert.Null(entry);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Remove_RemovesEntry()
    {
        var file = CreateTempFile();
        try
        {
            SessionNameOverrideService.Set(file, "session-1", "Name", false);
            SessionNameOverrideService.Remove(file, "session-1");
            var entry = SessionNameOverrideService.Get(file, "session-1");
            Assert.Null(entry);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Remove_NonExistentId_DoesNotThrow()
    {
        var file = CreateTempFile();
        try
        {
            SessionNameOverrideService.Remove(file, "nonexistent");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Load_MultipleSessions_ReturnsAll()
    {
        var file = CreateTempFile();
        try
        {
            SessionNameOverrideService.Set(file, "s1", "Name 1", false);
            SessionNameOverrideService.Set(file, "s2", "Name 2", true);
            var entries = SessionNameOverrideService.Load(file);
            Assert.Equal(2, entries.Count);
            Assert.Equal("Name 1", entries["s1"].Name);
            Assert.False(entries["s1"].ResolvedFromUserMessage);
            Assert.Equal("Name 2", entries["s2"].Name);
            Assert.True(entries["s2"].ResolvedFromUserMessage);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Persistence_ResolvedFlagSurvivesRoundTrip()
    {
        var file = CreateTempFile();
        try
        {
            SessionNameOverrideService.Set(file, "s1", "Test Name", false);
            var loaded = SessionNameOverrideService.Load(file);
            Assert.Single(loaded);
            Assert.False(loaded["s1"].ResolvedFromUserMessage);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
