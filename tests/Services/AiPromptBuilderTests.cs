namespace CopilotBooster.Tests.Services;

public sealed class AiPromptBuilderTests
{
    [Fact]
    public void Render_SubstitutesPlaceholders_AndKeepsRequiredSchemaLines()
    {
        var sessionFolder = @"C:\Users\tester\.copilot\session-state\abc123";

        var prompt = AiPromptBuilder.Build("rogerbarreto", "copilot-booster", sessionFolder);

        Assert.DoesNotContain("{owner}", prompt);
        Assert.DoesNotContain("{repo}", prompt);
        Assert.DoesNotContain("{abs_path_to_session_state_folder}", prompt);
        Assert.Contains("The session belongs to repository: rogerbarreto/copilot-booster", prompt);
        Assert.Contains($"Folder: {sessionFolder}", prompt);
        Assert.Contains("Respond with EXACTLY ONE JSON object. No prose. No markdown code fences. No backticks.", prompt);
        Assert.Contains("  \"candidates\": [", prompt);
        Assert.Contains("    { \"type\": \"issue\" | \"pr\", \"number\": <int>, \"confidence\": <float 0.0-1.0>, \"reasoning\": \"<one short sentence>\" }", prompt);
        Assert.Contains("  - No candidates -> {\"candidates\": []}.", prompt);
        Assert.Contains("  - First character must be `{`, last must be `}`.", prompt);
    }

    [Fact]
    public void Render_IncludesDevelopmentLinkageDiscoveryInstructions()
    {
        var sessionFolder = @"C:\Users\tester\.copilot\session-state\abc123";

        var prompt = AiPromptBuilder.Build("rogerbarreto", "copilot-booster", sessionFolder);

        Assert.Contains("Development", prompt);
        Assert.Contains("closedByPullRequestsReferences", prompt);
        Assert.Contains("closingIssuesReferences", prompt);
        Assert.Contains("gh issue view <N> --repo rogerbarreto/copilot-booster --json closedByPullRequestsReferences", prompt);
        Assert.Contains("gh pr view <N> --repo rogerbarreto/copilot-booster --json closingIssuesReferences", prompt);
        Assert.Contains("REJECT linked items whose repository differs from rogerbarreto/copilot-booster", prompt);
        Assert.Contains("Deduplicate: if a candidate was already added from an explicit reference, do not add it again from linkage", prompt);
    }

    [Fact]
    public void Render_WithExistingAttachments_IncludesSeedSectionAndPerSeedEntries()
    {
        var sessionFolder = @"C:\Users\tester\.copilot\session-state\abc123";
        var existing = new List<AiPromptBuilder.ExistingAttachment>
        {
            new("issue", 15),
            new("pr", 42),
        };

        var prompt = AiPromptBuilder.Build("rogerbarreto", "copilot-booster", sessionFolder, existing);

        Assert.Contains("# Seed candidates from existing session attachments", prompt);
        Assert.Contains("- issue #15", prompt);
        Assert.Contains("- pr #42", prompt);
        Assert.Contains("Skip the validation step", prompt);
        Assert.Contains("Run linkage discovery from step 3", prompt);
        Assert.Contains("Do NOT include the existing attachments themselves as candidates", prompt);
    }

    [Fact]
    public void Render_WithoutExistingAttachments_DoesNotIncludeSeedSection()
    {
        var sessionFolder = @"C:\Users\tester\.copilot\session-state\abc123";

        var prompt = AiPromptBuilder.Build("rogerbarreto", "copilot-booster", sessionFolder, []);

        Assert.DoesNotContain("# Seed candidates from existing session attachments", prompt);
    }
}
