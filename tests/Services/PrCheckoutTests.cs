using System.Text.Json;

public sealed class PrCheckoutTests
{
    [Fact]
    public void NewSessionResult_HeadBranch_CarriedToCaller()
    {
        // When a PR is validated, the head branch name from the API should be
        // available in NewSessionResult so callers can use it instead of "pr-{number}"
        var result = new NewSessionResult(
            SessionName: "Test",
            Action: BranchAction.FromPr,
            BranchName: null,
            BaseBranch: null,
            Remote: "origin",
            PrNumber: 4358,
            Platform: GitService.HostingPlatform.GitHub,
            HeadBranch: "shkr/feat_durable_task_hitl");

        Assert.Equal("shkr/feat_durable_task_hitl", result.HeadBranch);
    }

    [Fact]
    public void NewSessionResult_HeadBranch_NullWhenNotAvailable()
    {
        var result = new NewSessionResult(
            SessionName: "Test",
            Action: BranchAction.FromPr,
            BranchName: null,
            BaseBranch: null,
            Remote: "origin",
            PrNumber: 123,
            Platform: GitService.HostingPlatform.GitHub,
            HeadBranch: null);

        Assert.Null(result.HeadBranch);
    }

    [Fact]
    public void ParseGitHubPrResponse_ExtractsHeadRef()
    {
        // Simulates the GitHub API response for a PR
        var json = """
        {
            "number": 4358,
            "title": "Durable task HITL",
            "head": {
                "ref": "shkr/feat_durable_task_hitl",
                "repo": {
                    "full_name": "microsoft/agent-framework"
                }
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? title = null;
        string? headRef = null;

        if (root.TryGetProperty("title", out var titleProp))
        {
            title = titleProp.GetString();
        }

        if (root.TryGetProperty("head", out var headProp) &&
            headProp.TryGetProperty("ref", out var refProp))
        {
            headRef = refProp.GetString();
        }

        Assert.Equal("Durable task HITL", title);
        Assert.Equal("shkr/feat_durable_task_hitl", headRef);
    }

    [Fact]
    public void ParseGitHubPrResponse_HeadRefMissing_ReturnsNull()
    {
        var json = """
        {
            "number": 123,
            "title": "Some PR"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? headRef = null;
        if (root.TryGetProperty("head", out var headProp) &&
            headProp.TryGetProperty("ref", out var refProp))
        {
            headRef = refProp.GetString();
        }

        Assert.Null(headRef);
    }

    [Fact]
    public void CallerShouldUseHeadBranch_InsteadOfPrPrefix()
    {
        // Simulates the logic in MainForm.ContextMenu.cs
        var headBranch = "shkr/feat_durable_task_hitl";
        int prNumber = 4358;

        // The local branch name should be the head branch when available
        var localBranchName = headBranch ?? $"pr-{prNumber}";

        Assert.Equal("shkr/feat_durable_task_hitl", localBranchName);
    }

    [Fact]
    public void CallerFallsBackToPrPrefix_WhenHeadBranchNull()
    {
        string? headBranch = null;
        int prNumber = 4358;

        var localBranchName = headBranch ?? $"pr-{prNumber}";

        Assert.Equal("pr-4358", localBranchName);
    }
}
