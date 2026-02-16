public sealed class SessionAliasServiceTests
{
    private string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"alias-test-{Guid.NewGuid()}.json");
        return path;
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsEmptyDictionary()
    {
        var result = SessionAliasService.Load(@"C:\nonexistent\aliases.json");
        Assert.Empty(result);
    }

    [Fact]
    public void SetAlias_CreatesFileAndStoresAlias()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionAliasService.SetAlias(file, "session-1", "My Alias");
            var alias = SessionAliasService.GetAlias(file, "session-1");
            Assert.Equal("My Alias", alias);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void SetAlias_EmptyAlias_RemovesEntry()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionAliasService.SetAlias(file, "session-1", "My Alias");
            SessionAliasService.SetAlias(file, "session-1", "");
            var alias = SessionAliasService.GetAlias(file, "session-1");
            Assert.Null(alias);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void GetAlias_UnknownId_ReturnsNull()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionAliasService.SetAlias(file, "session-1", "Alias");
            var alias = SessionAliasService.GetAlias(file, "unknown");
            Assert.Null(alias);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void RemoveAlias_RemovesEntry()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionAliasService.SetAlias(file, "session-1", "Alias");
            SessionAliasService.RemoveAlias(file, "session-1");
            var alias = SessionAliasService.GetAlias(file, "session-1");
            Assert.Null(alias);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void RemoveAlias_NonExistentId_DoesNotThrow()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionAliasService.RemoveAlias(file, "nonexistent");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Load_MultipleSessions_ReturnsAll()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionAliasService.SetAlias(file, "s1", "Alias 1");
            SessionAliasService.SetAlias(file, "s2", "Alias 2");
            var aliases = SessionAliasService.Load(file);
            Assert.Equal(2, aliases.Count);
            Assert.Equal("Alias 1", aliases["s1"]);
            Assert.Equal("Alias 2", aliases["s2"]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void SetAlias_OverwritesExisting()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionAliasService.SetAlias(file, "s1", "Original");
            SessionAliasService.SetAlias(file, "s1", "Updated");
            var alias = SessionAliasService.GetAlias(file, "s1");
            Assert.Equal("Updated", alias);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
