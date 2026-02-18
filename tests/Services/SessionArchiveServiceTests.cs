public sealed class SessionArchiveServiceTests
{
    private string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"archive-test-{Guid.NewGuid()}.json");
        return path;
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsEmptyDictionary()
    {
        var result = SessionArchiveService.Load(@"C:\nonexistent\archive-state.json");
        Assert.Empty(result);
    }

    [Fact]
    public void Load_ValidFile_DeserializesCorrectly()
    {
        var file = this.CreateTempFile();
        try
        {
            File.WriteAllText(file, """
                {
                    "session-1": { "IsArchived": true, "IsPinned": false },
                    "session-2": { "IsArchived": false, "IsPinned": true }
                }
                """);

            var result = SessionArchiveService.Load(file);

            Assert.Equal(2, result.Count);
            Assert.True(result["session-1"].IsArchived);
            Assert.False(result["session-1"].IsPinned);
            Assert.False(result["session-2"].IsArchived);
            Assert.True(result["session-2"].IsPinned);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Load_CorruptedJson_ReturnsEmptyDictionary()
    {
        var file = this.CreateTempFile();
        try
        {
            File.WriteAllText(file, "not valid json {{{");
            var result = SessionArchiveService.Load(file);
            Assert.Empty(result);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void SetArchived_CreatesFileAndSetsState()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetArchived(file, "session-1", true);

            Assert.True(File.Exists(file));
            Assert.True(SessionArchiveService.IsArchived(file, "session-1"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void SetArchived_ToFalse_CleansUpDefaultState()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetArchived(file, "session-1", true);
            SessionArchiveService.SetArchived(file, "session-1", false);

            var states = SessionArchiveService.Load(file);
            Assert.DoesNotContain("session-1", states.Keys);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void IsArchived_WithArchivedSession_ReturnsTrue()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetArchived(file, "session-1", true);
            Assert.True(SessionArchiveService.IsArchived(file, "session-1"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void IsArchived_WithNonExistent_ReturnsFalse()
    {
        var file = this.CreateTempFile();
        try
        {
            Assert.False(SessionArchiveService.IsArchived(file, "nonexistent"));
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void SetPinned_CreatesFileAndSetsState()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetPinned(file, "session-1", true);

            Assert.True(File.Exists(file));
            Assert.True(SessionArchiveService.IsPinned(file, "session-1"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void SetPinned_ToFalse_CleansUpDefaultState()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetPinned(file, "session-1", true);
            SessionArchiveService.SetPinned(file, "session-1", false);

            var states = SessionArchiveService.Load(file);
            Assert.DoesNotContain("session-1", states.Keys);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void IsPinned_WithPinnedSession_ReturnsTrue()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetPinned(file, "session-1", true);
            Assert.True(SessionArchiveService.IsPinned(file, "session-1"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void IsPinned_WithNonExistent_ReturnsFalse()
    {
        var file = this.CreateTempFile();
        try
        {
            Assert.False(SessionArchiveService.IsPinned(file, "nonexistent"));
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void SetArchived_PreservesPinState()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetPinned(file, "session-1", true);
            SessionArchiveService.SetArchived(file, "session-1", true);

            Assert.True(SessionArchiveService.IsArchived(file, "session-1"));
            Assert.True(SessionArchiveService.IsPinned(file, "session-1"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Remove_DeletesSessionState()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetArchived(file, "session-1", true);
            SessionArchiveService.Remove(file, "session-1");

            Assert.False(SessionArchiveService.IsArchived(file, "session-1"));
            var states = SessionArchiveService.Load(file);
            Assert.DoesNotContain("session-1", states.Keys);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Remove_NonExistent_DoesNotThrow()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetArchived(file, "session-1", true);
            SessionArchiveService.Remove(file, "nonexistent");

            Assert.True(SessionArchiveService.IsArchived(file, "session-1"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void CleanupIfDefault_KeepsPinnedSession()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetPinned(file, "session-1", true);
            SessionArchiveService.SetArchived(file, "session-1", true);

            // Unarchive — entry should remain because it's still pinned
            SessionArchiveService.SetArchived(file, "session-1", false);

            var states = SessionArchiveService.Load(file);
            Assert.Contains("session-1", states.Keys);
            Assert.True(states["session-1"].IsPinned);
            Assert.False(states["session-1"].IsArchived);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
