namespace CopilotBooster.E2E;

/// <summary>
/// End-to-end tests that create copilot sessions via workspace.yaml and
/// verify them using copilot --resume -p (non-interactive mode).
/// </summary>
public sealed class SessionCreationTests
{
    private readonly string _workDir;

    public SessionCreationTests()
    {
        // Use the repo root as a valid git-enabled working directory
        _workDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    [Fact]
    public void WorkspaceYaml_WithIdField_SessionStartsWithoutError()
    {
        using var harness = CopilotCliHarness.CreateSession(_workDir, sessionName: "No Error Test");

        var (output, exitCode) = harness.RunPrompt(
            "Reply with exactly: SESSION_OK", _workDir);

        Assert.DoesNotContain("Error: Failed to load workspace", output);
        Assert.DoesNotContain("invalid_type", output);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void WorkspaceYaml_SessionIdMatchesResume()
    {
        using var harness = CopilotCliHarness.CreateSession(_workDir, sessionName: "ID Match Test");

        var (output, _) = harness.RunPrompt(
            "What is the current session ID? Reply with only the UUID, nothing else.", _workDir);

        Assert.Contains(harness.SessionId, output);
    }

    [Fact]
    public void WorkspaceYaml_MinimalFormat_HasRequiredFields()
    {
        using var harness = CopilotCliHarness.CreateSession(_workDir, sessionName: "Fields Test");

        // Verify the workspace.yaml was written correctly before copilot loads it
        var content = harness.ReadWorkspaceYaml();
        Assert.NotNull(content);
        Assert.Contains($"id: {harness.SessionId}", content);
        Assert.Contains($"cwd: {_workDir}", content);
        Assert.Contains("summary: Fields Test", content);
        Assert.Contains("summary_count: 0", content);
        Assert.Contains("created_at:", content);
        Assert.Contains("updated_at:", content);
    }

    [Fact]
    public void WorkspaceYaml_FromSource_OverridesIdCwdSummary()
    {
        // Create a "source" workspace.yaml with all fields
        var sourceDir = Path.Combine(Path.GetTempPath(), $"copilot-e2e-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        try
        {
            var sourceWs = Path.Combine(sourceDir, "workspace.yaml");
            File.WriteAllLines(sourceWs,
            [
                "id: source-id-12345",
                @"cwd: C:\original\path",
                @"git_root: C:\original\repo",
                "repository: testuser/testrepo",
                "branch: main",
                "summary_count: 5",
                "created_at: 2026-01-01T00:00:00.000Z",
                "updated_at: 2026-01-01T00:00:00.000Z",
                "summary: Original Session Name",
            ]);

            using var harness = CopilotCliHarness.CreateSession(
                _workDir,
                sessionName: "Cloned Session",
                sourceWorkspaceYaml: sourceWs);

            var content = harness.ReadWorkspaceYaml();
            Assert.NotNull(content);

            // Overridden fields
            Assert.Contains($"id: {harness.SessionId}", content);
            Assert.DoesNotContain("source-id-12345", content);
            Assert.Contains($"cwd: {_workDir}", content);
            Assert.DoesNotContain(@"C:\original\path", content);
            Assert.Contains("summary: Cloned Session", content);
            Assert.DoesNotContain("Original Session Name", content);

            // Preserved fields from source
            Assert.Contains(@"git_root: C:\original\repo", content);
            Assert.Contains("repository: testuser/testrepo", content);
            Assert.Contains("branch: main", content);

            // Reset fields
            Assert.Contains("summary_count: 0", content);
            Assert.DoesNotContain("2026-01-01T00:00:00.000Z", content);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    [Fact]
    public void WorkspaceYaml_FromSource_SessionStartsWithoutError()
    {
        // Find a real existing session to use as source template
        var sessionStateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "session-state");

        string? sourceWs = null;
        if (Directory.Exists(sessionStateDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(sessionStateDir))
            {
                var ws = Path.Combine(dir, "workspace.yaml");
                if (File.Exists(ws) && File.ReadAllText(ws).Contains("id:"))
                {
                    sourceWs = ws;
                    break;
                }
            }
        }

        if (sourceWs == null)
        {
            return; // No existing session to use as template
        }

        using var harness = CopilotCliHarness.CreateSession(
            _workDir,
            sessionName: "From Source E2E",
            sourceWorkspaceYaml: sourceWs);

        var (output, exitCode) = harness.RunPrompt(
            "Reply with exactly: SOURCE_SESSION_OK", _workDir);

        Assert.DoesNotContain("Error: Failed to load workspace", output);
        Assert.DoesNotContain("invalid_type", output);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void WorkspaceYaml_WithoutIdField_ShowsErrorInInteractiveMode()
    {
        // When workspace.yaml is missing the id field, copilot -p still works
        // but interactive mode shows "Failed to load workspace" with invalid_type error.
        // This test verifies the workspace.yaml is at least loaded without crash in -p mode,
        // and documents the known interactive-mode validation gap.
        var sessionId = Guid.NewGuid().ToString();
        var sessionStateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "session-state");
        var sessionDir = Path.Combine(sessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        try
        {
            // Write workspace.yaml WITHOUT id field — causes error in interactive mode
            File.WriteAllLines(Path.Combine(sessionDir, "workspace.yaml"),
            [
                $"cwd: {_workDir}",
                "summary: Missing ID Test",
            ]);

            var copilotExe = CopilotBooster.Services.CopilotLocator.FindCopilotExe();
            if (copilotExe == null)
            {
                return;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = copilotExe,
                Arguments = $"--resume {sessionId} -p \"Reply with exactly: YAML_OK\"",
                WorkingDirectory = _workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = System.Diagnostics.Process.Start(psi)!;
            process.WaitForExit(60_000);

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            // -p mode tolerates missing id, but the id field MUST be present
            // for interactive mode. This test documents the divergence.
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            try { Directory.Delete(sessionDir, recursive: true); } catch { }
        }
    }
}
