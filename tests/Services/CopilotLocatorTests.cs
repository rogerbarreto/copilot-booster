public class FindCopilotExeTests : IDisposable
{
    private readonly string _tempDir;

    public FindCopilotExeTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void FindCopilotExe_CandidateExists_ReturnsIt()
    {
        var fakeCopilot = Path.Combine(this._tempDir, "copilot.exe");
        File.WriteAllText(fakeCopilot, "fake");

        var result = CopilotLocator.FindCopilotExe(new[] { fakeCopilot });

        Assert.Equal(fakeCopilot, result);
    }

    [Fact]
    public void FindCopilotExe_NoCandidatesExist_FallsBackToDefault()
    {
        var result = CopilotLocator.FindCopilotExe(new[]
        {
            Path.Combine(this._tempDir, "nonexistent1.exe"),
            Path.Combine(this._tempDir, "nonexistent2.exe")
        });

        // Falls through to 'where' command or returns "copilot.exe"
        Assert.NotEmpty(result);
    }

    [Fact]
    public void FindCopilotExe_FirstCandidateMatches_ReturnsFirst()
    {
        var first = Path.Combine(this._tempDir, "first.exe");
        var second = Path.Combine(this._tempDir, "second.exe");
        File.WriteAllText(first, "fake1");
        File.WriteAllText(second, "fake2");

        var result = CopilotLocator.FindCopilotExe(new[] { first, second });

        Assert.Equal(first, result);
    }

    [Fact]
    public void FindCopilotExe_EmptyCandidates_FallsBackToDefault()
    {
        var result = CopilotLocator.FindCopilotExe(Array.Empty<string>());

        Assert.NotEmpty(result);
    }
}
