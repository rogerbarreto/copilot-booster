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
}
