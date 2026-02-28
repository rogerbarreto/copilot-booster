using System.Text.Json;

public sealed class LauncherSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public LauncherSettingsTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void Load_WhenFileNotExists_CreatesDefaultFile()
    {
        var file = Path.Combine(this._tempDir, "sub", "settings.json");
        _ = LauncherSettings.Load(file);

        Assert.True(File.Exists(file));
        var loaded = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(file));
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.AllowedTools);
    }

    [Fact]
    public void Load_WhenFileNotExists_ReturnsDefault()
    {
        var file = Path.Combine(this._tempDir, "nonexistent", "settings.json");
        var settings = LauncherSettings.Load(file);

        Assert.NotNull(settings);
        Assert.Empty(settings.AllowedTools);
        Assert.Empty(settings.AllowedDirs);
        Assert.Equal("", settings.DefaultWorkDir);
    }

    [Fact]
    public void Load_WhenFileExists_DeserializesCorrectly()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var json = JsonSerializer.Serialize(new
        {
            allowedTools = new[] { "tool1", "tool2" },
            allowedDirs = new[] { @"C:\dir1" },
            defaultWorkDir = @"C:\work",
            ides = new[] { new { path = @"C:\code.exe", description = "VS Code" } }
        });
        File.WriteAllText(file, json);

        var settings = LauncherSettings.Load(file);

        Assert.Equal(2, settings.AllowedTools.Count);
        Assert.Contains("tool1", settings.AllowedTools);
        Assert.Contains("tool2", settings.AllowedTools);
        Assert.Single(settings.AllowedDirs);
        Assert.Equal(@"C:\dir1", settings.AllowedDirs[0]);
        Assert.Equal(@"C:\work", settings.DefaultWorkDir);
        Assert.Single(settings.Ides);
        Assert.Equal("VS Code", settings.Ides[0].Description);
        Assert.Equal(@"C:\code.exe", settings.Ides[0].Path);
    }

    [Fact]
    public void Load_WhenFileCorrupt_ReturnsDefault()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        File.WriteAllText(file, "not valid json {{{");

        var settings = LauncherSettings.Load(file);

        Assert.NotNull(settings);
        Assert.Empty(settings.AllowedTools);
        Assert.Empty(settings.AllowedDirs);
    }

    [Fact]
    public void Save_CreatesDirectoryAndFile()
    {
        var file = Path.Combine(this._tempDir, "sub", "deep", "settings.json");
        var settings = new LauncherSettings
        {
            AllowedTools = ["mytool"],
            DefaultWorkDir = @"C:\mywork"
        };

        settings.Save(file);

        Assert.True(File.Exists(file));
        var loaded = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(file));
        Assert.NotNull(loaded);
        Assert.Contains("mytool", loaded!.AllowedTools);
        Assert.Equal(@"C:\mywork", loaded.DefaultWorkDir);
    }

    [Fact]
    public void Save_OverwritesExisting()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var s1 = new LauncherSettings { DefaultWorkDir = "first" };
        s1.Save(file);

        var s2 = new LauncherSettings { DefaultWorkDir = "second" };
        s2.Save(file);

        var loaded = LauncherSettings.Load(file);
        Assert.Equal("second", loaded.DefaultWorkDir);
    }

    [Fact]
    public void CreateDefault_HasEmptyCollections()
    {
        var settings = LauncherSettings.CreateDefault();

        Assert.Empty(settings.AllowedTools);
        Assert.Empty(settings.AllowedDirs);
        Assert.Equal("", settings.DefaultWorkDir);
    }

    [Fact]
    public void BuildCopilotArgs_NoToolsNoDirs_ReturnsExtraArgsOnly()
    {
        var settings = new LauncherSettings();
        var result = settings.BuildCopilotArgs(["--resume", "abc"]);

        Assert.Equal("--resume abc", result);
    }

    [Fact]
    public void BuildCopilotArgs_WithToolsAndDirs_FormatsCorrectly()
    {
        var settings = new LauncherSettings
        {
            AllowedTools = ["bash", "python"],
            AllowedDirs = [this._tempDir, Path.GetTempPath()]
        };

        var result = settings.BuildCopilotArgs([]);

        Assert.Contains("--allow-tool \"bash\"", result);
        Assert.Contains("--allow-tool \"python\"", result);
        Assert.Contains($"--add-dir \"{this._tempDir}\"", result);
        Assert.Contains($"--add-dir \"{Path.GetTempPath()}\"", result);
    }

    [Fact]
    public void BuildCopilotArgs_WithExtraArgs_AppendsCorrectly()
    {
        var settings = new LauncherSettings
        {
            AllowedTools = ["tool1"]
        };

        var result = settings.BuildCopilotArgs(["--resume session1"]);

        Assert.StartsWith("--allow-tool \"tool1\"", result);
        Assert.EndsWith("--resume session1", result);
    }

    [Fact]
    public void BuildCopilotArgs_EmptyExtraArgs_ReturnsToolsAndDirsOnly()
    {
        var settings = new LauncherSettings
        {
            AllowedTools = ["tool1"],
            AllowedDirs = [this._tempDir]
        };

        var result = settings.BuildCopilotArgs([]);

        Assert.Equal($"--allow-tool \"tool1\" --add-dir \"{this._tempDir}\"", result);
    }

    [Fact]
    public void Load_WithTheme_DeserializesCorrectly()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var json = JsonSerializer.Serialize(new { theme = "dark" });
        File.WriteAllText(file, json);

        var settings = LauncherSettings.Load(file);

        Assert.Equal("dark", settings.Theme);
    }

    [Fact]
    public void Load_WithoutTheme_DefaultsToSystem()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var json = JsonSerializer.Serialize(new { allowedTools = Array.Empty<string>() });
        File.WriteAllText(file, json);

        var settings = LauncherSettings.Load(file);

        Assert.Equal("system", settings.Theme);
    }

    [Fact]
    public void Save_WithTheme_PersistsCorrectly()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var settings = new LauncherSettings { Theme = "light" };

        settings.Save(file);

        var loaded = LauncherSettings.Load(file);
        Assert.Equal("light", loaded.Theme);
    }

    [Fact]
    public void IdeSearchIgnoredDirs_Default_ContainsCommonDirectories()
    {
        var settings = new LauncherSettings();

        Assert.Contains("node_modules", settings.IdeSearchIgnoredDirs);
        Assert.Contains("bin", settings.IdeSearchIgnoredDirs);
        Assert.Contains("obj", settings.IdeSearchIgnoredDirs);
        Assert.Contains(".git", settings.IdeSearchIgnoredDirs);
        Assert.Contains(".venv", settings.IdeSearchIgnoredDirs);
        Assert.Contains("vendor", settings.IdeSearchIgnoredDirs);
    }

    [Fact]
    public void IdeEntry_FilePattern_DefaultsToEmpty()
    {
        var entry = new IdeEntry();
        Assert.Equal("", entry.FilePattern);
    }

    [Fact]
    public void IdeEntry_FilePattern_SerializesCorrectly()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var settings = new LauncherSettings
        {
            Ides = [new IdeEntry { Path = @"C:\code.exe", Description = "VS Code", FilePattern = "*.sln;*.csproj" }]
        };

        settings.Save(file);

        var loaded = LauncherSettings.Load(file);
        Assert.Single(loaded.Ides);
        Assert.Equal("*.sln;*.csproj", loaded.Ides[0].FilePattern);
    }

    [Fact]
    public void Save_WithIdeSearchIgnoredDirs_PersistsCorrectly()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var settings = new LauncherSettings
        {
            IdeSearchIgnoredDirs = ["custom_dir", "another_dir"]
        };

        settings.Save(file);

        var loaded = LauncherSettings.Load(file);
        Assert.Equal(2, loaded.IdeSearchIgnoredDirs.Count);
        Assert.Contains("custom_dir", loaded.IdeSearchIgnoredDirs);
        Assert.Contains("another_dir", loaded.IdeSearchIgnoredDirs);
    }

    [Fact]
    public void SessionColumnOrder_DefaultsToEmptyList()
    {
        var settings = new LauncherSettings();
        Assert.NotNull(settings.SessionColumnOrder);
        Assert.Empty(settings.SessionColumnOrder);
    }

    [Fact]
    public void SessionColumnOrder_SerializesCorrectly()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var settings = new LauncherSettings
        {
            SessionColumnOrder = ["CWD", "Session", "Date", "RunningApps"]
        };

        settings.Save(file);

        var loaded = LauncherSettings.Load(file);
        Assert.Equal(4, loaded.SessionColumnOrder.Count);
        Assert.Equal("CWD", loaded.SessionColumnOrder[0]);
        Assert.Equal("Session", loaded.SessionColumnOrder[1]);
        Assert.Equal("Date", loaded.SessionColumnOrder[2]);
        Assert.Equal("RunningApps", loaded.SessionColumnOrder[3]);
    }

    [Fact]
    public void Load_WithSessionColumnOrder_DeserializesCorrectly()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var json = JsonSerializer.Serialize(new
        {
            sessionColumnOrder = new[] { "Date", "CWD", "Session", "RunningApps" }
        });
        File.WriteAllText(file, json);

        var settings = LauncherSettings.Load(file);

        Assert.Equal(4, settings.SessionColumnOrder.Count);
        Assert.Equal("Date", settings.SessionColumnOrder[0]);
        Assert.Equal("CWD", settings.SessionColumnOrder[1]);
    }

    [Fact]
    public void ToastMode_DefaultsToEnabled()
    {
        var settings = new LauncherSettings();

        Assert.True(settings.ToastMode);
        Assert.Equal("bottom-center", settings.ToastPosition);
        Assert.Equal("primary", settings.ToastScreen);
        Assert.True(settings.ToastAnimate);
    }

    [Fact]
    public void ToastMode_SerializesCorrectly()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var settings = new LauncherSettings
        {
            ToastMode = true,
            ToastPosition = "top-right",
            ToastScreen = "cursor",
            ToastAnimate = false
        };

        settings.Save(file);

        var loaded = LauncherSettings.Load(file);
        Assert.True(loaded.ToastMode);
        Assert.Equal("top-right", loaded.ToastPosition);
        Assert.Equal("cursor", loaded.ToastScreen);
        Assert.False(loaded.ToastAnimate);
    }

    [Fact]
    public void Load_WithToastSettings_DeserializesCorrectly()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var json = JsonSerializer.Serialize(new
        {
            toastMode = true,
            toastPosition = "bottom-left",
            toastScreen = "cursor",
            toastAnimate = true
        });
        File.WriteAllText(file, json);

        var settings = LauncherSettings.Load(file);

        Assert.True(settings.ToastMode);
        Assert.Equal("bottom-left", settings.ToastPosition);
        Assert.Equal("cursor", settings.ToastScreen);
        Assert.True(settings.ToastAnimate);
    }

    [Fact]
    public void Load_WithoutToastSettings_DefaultsCorrectly()
    {
        var file = Path.Combine(this._tempDir, "settings.json");
        var json = JsonSerializer.Serialize(new { allowedTools = Array.Empty<string>() });
        File.WriteAllText(file, json);

        var settings = LauncherSettings.Load(file);

        Assert.True(settings.ToastMode);
        Assert.Equal("bottom-center", settings.ToastPosition);
        Assert.Equal("primary", settings.ToastScreen);
        Assert.True(settings.ToastAnimate);
    }

    [Fact]
    public void BuildCopilotArgs_WithNonExistentDir_SkipsIt()
    {
        var settings = new LauncherSettings
        {
            AllowedDirs = [@"Z:\NonExistent\FakeDir", @"C:\"]
        };

        var result = settings.BuildCopilotArgs([]);

        Assert.DoesNotContain("FakeDir", result);
        Assert.Contains(@"--add-dir ""C:\""", result);
    }

    [Fact]
    public void BuildCopilotArgs_AllDirsNonExistent_ReturnsNoAddDir()
    {
        var settings = new LauncherSettings
        {
            AllowedDirs = [@"Z:\Nope", @"Q:\AlsoNope"]
        };

        var result = settings.BuildCopilotArgs(["--resume", "abc"]);

        Assert.DoesNotContain("--add-dir", result);
        Assert.Equal("--resume abc", result);
    }
}
