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
                    "session-1": { "Tab": "Archived", "IsPinned": false },
                    "session-2": { "Tab": "", "IsPinned": true }
                }
                """);

            var result = SessionArchiveService.Load(file);

            Assert.Equal(2, result.Count);
            Assert.Equal("Archived", result["session-1"].Tab);
            Assert.False(result["session-1"].IsPinned);
            Assert.Equal("", result["session-2"].Tab);
            Assert.True(result["session-2"].IsPinned);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Load_LegacyIsArchived_MigratesToTab()
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
            Assert.Equal("Archived", result["session-1"].Tab);
            Assert.Equal("", result["session-2"].Tab);
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
    public void SetTab_CreatesFileAndSetsState()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetTab(file, "session-1", "Archived");

            Assert.True(File.Exists(file));
            Assert.Equal("Archived", SessionArchiveService.GetTab(file, "session-1"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void SetTab_ToDefault_CleansUpState()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetTab(file, "session-1", "Archived");
            SessionArchiveService.SetTab(file, "session-1", "Active");

            var states = SessionArchiveService.Load(file);
            Assert.DoesNotContain("session-1", states.Keys);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void GetTab_WithNonExistent_ReturnsDefaultTab()
    {
        var file = this.CreateTempFile();
        try
        {
            var tab = SessionArchiveService.GetTab(file, "nonexistent");
            Assert.Equal("Active", tab);
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
    public void SetTab_PreservesPinState()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetPinned(file, "session-1", true);
            SessionArchiveService.SetTab(file, "session-1", "Archived");

            Assert.Equal("Archived", SessionArchiveService.GetTab(file, "session-1"));
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
            SessionArchiveService.SetTab(file, "session-1", "Archived");
            SessionArchiveService.Remove(file, "session-1");

            Assert.Equal("Active", SessionArchiveService.GetTab(file, "session-1"));
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
            SessionArchiveService.SetTab(file, "session-1", "Archived");
            SessionArchiveService.Remove(file, "nonexistent");

            Assert.Equal("Archived", SessionArchiveService.GetTab(file, "session-1"));
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
            SessionArchiveService.SetTab(file, "session-1", "Archived");

            // Move back to default — entry should remain because it's still pinned
            SessionArchiveService.SetTab(file, "session-1", "Active");

            var states = SessionArchiveService.Load(file);
            Assert.Contains("session-1", states.Keys);
            Assert.True(states["session-1"].IsPinned);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void RenameTab_UpdatesAllSessionStates()
    {
        var file = this.CreateTempFile();
        try
        {
            SessionArchiveService.SetTab(file, "session-1", "Work");
            SessionArchiveService.SetTab(file, "session-2", "Work");
            SessionArchiveService.SetTab(file, "session-3", "Personal");

            SessionArchiveService.RenameTab(file, "Work", "Projects");

            var states = SessionArchiveService.Load(file);
            Assert.Equal("Projects", states["session-1"].Tab);
            Assert.Equal("Projects", states["session-2"].Tab);
            Assert.Equal("Personal", states["session-3"].Tab);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ApplySessionStates_UntaggedSessions_AssignedToActiveTab_RegardlessOfTabOrder()
    {
        // Scenario: user reorders tabs so "Work" is first, "Active" is second.
        // Sessions with no stored tab state should still get "Active" (the DefaultTab),
        // not "Work" (which is just at position 0).
        var sessions = new List<NamedSession>
        {
            new() { Id = "new-session", Summary = "New" },
            new() { Id = "existing-session", Summary = "Existing" }
        };

        // "existing-session" has an explicit "Active" tab in state
        var states = new Dictionary<string, SessionArchiveService.SessionState>
        {
            ["existing-session"] = new() { Tab = "Active" }
        };

        // DefaultTab is still "Active" — tab order doesn't matter
        MainForm.ApplySessionStates(sessions, states, "Active");

        // The existing session keeps its explicit "Active" tab
        Assert.Equal("Active", sessions[1].Tab);

        // The new session (no state) gets the DefaultTab
        Assert.Equal("Active", sessions[0].Tab);
    }

    [Fact]
    public void ApplySessionStates_EmptyTabInState_AssignedToActiveTab_RegardlessOfTabOrder()
    {
        // Session has a state entry but Tab is empty (legacy or cleared)
        var sessions = new List<NamedSession>
        {
            new() { Id = "legacy-session", Summary = "Legacy" }
        };

        var states = new Dictionary<string, SessionArchiveService.SessionState>
        {
            ["legacy-session"] = new() { Tab = "" }
        };

        // DefaultTab is "Active" regardless of tab order
        MainForm.ApplySessionStates(sessions, states, "Active");

        Assert.Equal("Active", sessions[0].Tab);
    }

    [Theory]
    [InlineData("Main")]
    [InlineData("Default")]
    [InlineData("Sessions")]
    [InlineData("My Tab")]
    public void ApplySessionStates_RenamedFirstTab_UntaggedSessionsGetRenamedDefault(string renamedFirst)
    {
        // User renamed "Active" → renamedFirst. DefaultTab was updated to match.
        var sessions = new List<NamedSession>
        {
            new() { Id = "new-session", Summary = "New" }
        };

        var states = new Dictionary<string, SessionArchiveService.SessionState>();

        // DefaultTab tracks the rename
        MainForm.ApplySessionStates(sessions, states, renamedFirst);

        Assert.Equal(renamedFirst, sessions[0].Tab);
    }

    [Theory]
    [InlineData("Main")]
    [InlineData("Default")]
    [InlineData("Sessions")]
    [InlineData("My Tab")]
    public void ApplySessionStates_RenamedFirstTab_EmptyStateGetsRenamedDefault(string renamedFirst)
    {
        // Session has state entry with empty Tab (legacy migration).
        // DefaultTab was updated when user renamed "Active" → renamedFirst.
        var sessions = new List<NamedSession>
        {
            new() { Id = "legacy", Summary = "Legacy" }
        };

        var states = new Dictionary<string, SessionArchiveService.SessionState>
        {
            ["legacy"] = new() { Tab = "" }
        };

        MainForm.ApplySessionStates(sessions, states, renamedFirst);

        Assert.Equal(renamedFirst, sessions[0].Tab);
    }

    [Theory]
    [InlineData("Main")]
    [InlineData("Default")]
    [InlineData("Sessions")]
    public void ApplySessionStates_RenamedFirstTab_ReorderedToSecond_UntaggedStillGetsDefault(string renamedFirst)
    {
        // User renamed "Active" → renamedFirst, then moved it to position 1.
        // DefaultTab is still renamedFirst (name-based, not positional).
        // Untagged sessions should get the DefaultTab, not whatever is at index 0.
        var sessions = new List<NamedSession>
        {
            new() { Id = "new-session", Summary = "New" }
        };

        var states = new Dictionary<string, SessionArchiveService.SessionState>();

        // DefaultTab is renamedFirst regardless of position
        MainForm.ApplySessionStates(sessions, states, renamedFirst);

        Assert.Equal(renamedFirst, sessions[0].Tab);
    }
}
