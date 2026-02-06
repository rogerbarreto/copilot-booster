using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public class LauncherSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public LauncherSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Load_WhenFileNotExists_CreatesDefaultFile()
    {
        var file = Path.Combine(_tempDir, "sub", "settings.json");
        var settings = LauncherSettings.Load(file);

        Assert.True(File.Exists(file));
        var loaded = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(file));
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.AllowedTools);
    }

    [Fact]
    public void Load_WhenFileNotExists_ReturnsDefault()
    {
        var file = Path.Combine(_tempDir, "nonexistent", "settings.json");
        var settings = LauncherSettings.Load(file);

        Assert.NotNull(settings);
        Assert.Empty(settings.AllowedTools);
        Assert.Empty(settings.AllowedDirs);
        Assert.Equal("", settings.DefaultWorkDir);
    }

    [Fact]
    public void Load_WhenFileExists_DeserializesCorrectly()
    {
        var file = Path.Combine(_tempDir, "settings.json");
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
        var file = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(file, "not valid json {{{");

        var settings = LauncherSettings.Load(file);

        Assert.NotNull(settings);
        Assert.Empty(settings.AllowedTools);
        Assert.Empty(settings.AllowedDirs);
    }

    [Fact]
    public void Save_CreatesDirectoryAndFile()
    {
        var file = Path.Combine(_tempDir, "sub", "deep", "settings.json");
        var settings = new LauncherSettings
        {
            AllowedTools = new List<string> { "mytool" },
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
        var file = Path.Combine(_tempDir, "settings.json");
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
        var result = settings.BuildCopilotArgs(new[] { "--resume", "abc" });

        Assert.Equal("--resume abc", result);
    }

    [Fact]
    public void BuildCopilotArgs_WithToolsAndDirs_FormatsCorrectly()
    {
        var settings = new LauncherSettings
        {
            AllowedTools = new List<string> { "bash", "python" },
            AllowedDirs = new List<string> { @"C:\code", @"D:\work" }
        };

        var result = settings.BuildCopilotArgs(Array.Empty<string>());

        Assert.Contains("\"--allow-tool=bash\"", result);
        Assert.Contains("\"--allow-tool=python\"", result);
        Assert.Contains("\"--add-dir=C:\\code\"", result);
        Assert.Contains("\"--add-dir=D:\\work\"", result);
    }

    [Fact]
    public void BuildCopilotArgs_WithExtraArgs_AppendsCorrectly()
    {
        var settings = new LauncherSettings
        {
            AllowedTools = new List<string> { "tool1" }
        };

        var result = settings.BuildCopilotArgs(new[] { "--resume session1" });

        Assert.StartsWith("\"--allow-tool=tool1\"", result);
        Assert.EndsWith("--resume session1", result);
    }

    [Fact]
    public void BuildCopilotArgs_EmptyExtraArgs_ReturnsToolsAndDirsOnly()
    {
        var settings = new LauncherSettings
        {
            AllowedTools = new List<string> { "tool1" },
            AllowedDirs = new List<string> { @"C:\dir" }
        };

        var result = settings.BuildCopilotArgs(Array.Empty<string>());

        Assert.Equal("\"--allow-tool=tool1\" \"--add-dir=C:\\dir\"", result);
    }
}
