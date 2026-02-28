public sealed class HealWorkspaceYamlTests : IDisposable
{
    private readonly string _tempDir;

    public HealWorkspaceYamlTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void HealWorkspaceYaml_FixesBareNullSummary()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: test-123\ncwd: C:\\work\nsummary:\nname:\n");

        var result = CopilotSessionCreatorService.HealWorkspaceYaml(wsFile);

        Assert.True(result);
        var content = File.ReadAllText(wsFile);
        Assert.Contains("summary: \"\"", content);
        Assert.Contains("name: \"\"", content);
    }

    [Fact]
    public void HealWorkspaceYaml_DoesNotModifyValidFile()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: test-123\ncwd: C:\\work\nsummary: My Session\n");

        var result = CopilotSessionCreatorService.HealWorkspaceYaml(wsFile);

        Assert.False(result);
        var content = File.ReadAllText(wsFile);
        Assert.Contains("summary: My Session", content);
    }

    [Fact]
    public void HealWorkspaceYaml_FixesBareNullWithTrailingSpaces()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: test-123\nsummary:   \n");

        var result = CopilotSessionCreatorService.HealWorkspaceYaml(wsFile);

        Assert.True(result);
        var content = File.ReadAllText(wsFile);
        Assert.Contains("summary: \"\"", content);
    }

    [Fact]
    public void HealWorkspaceYaml_NonExistentFile_ReturnsFalse()
    {
        var result = CopilotSessionCreatorService.HealWorkspaceYaml(Path.Combine(this._tempDir, "nope.yaml"));

        Assert.False(result);
    }

    [Fact]
    public void HealWorkspaceYaml_PreservesOtherFields()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: test-123\ncwd: S:\\repo\nsummary:\nsummary_count: 0\n");

        CopilotSessionCreatorService.HealWorkspaceYaml(wsFile);

        var lines = File.ReadAllLines(wsFile);
        Assert.Equal("id: test-123", lines[0]);
        Assert.Equal("cwd: S:\\repo", lines[1]);
        Assert.Equal("summary: \"\"", lines[2]);
        Assert.Equal("summary_count: 0", lines[3]);
    }

    [Fact]
    public void HealWorkspaceYaml_DoesNotModifyQuotedEmptySummary()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: test-123\ncwd: C:\\work\nsummary: \"\"\n");

        var result = CopilotSessionCreatorService.HealWorkspaceYaml(wsFile);

        Assert.False(result);
    }

    [Fact]
    public void HealWorkspaceYaml_FixesMultipleBareNullFields()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: test-123\ncwd: C:\\work\nsummary:\nname:\nbranch:\n");

        var result = CopilotSessionCreatorService.HealWorkspaceYaml(wsFile);

        Assert.True(result);
        var lines = File.ReadAllLines(wsFile);
        Assert.Equal("summary: \"\"", lines[2]);
        Assert.Equal("name: \"\"", lines[3]);
        Assert.Equal("branch: \"\"", lines[4]);
    }
}
