namespace CopilotBooster.Tests.Services;

public sealed class AiDetectionTooltipsTests
{
    [Fact]
    public void ForFailure_Timeout_ReturnsTimeoutMessage()
    {
        var result = AiDetectionTooltips.ForFailure(AiFailureClass.Timeout, 300);

        Assert.Equal("Detection timed out after 300 seconds.", result);
    }

    [Fact]
    public void ForFailure_MalformedJson_ReturnsInvalidResponseMessage()
    {
        var result = AiDetectionTooltips.ForFailure(AiFailureClass.MalformedJson, null);

        Assert.Equal("Copilot returned an invalid response. See app log for details.", result);
    }

    [Fact]
    public void ForUndecided_LowConfidence_ReturnsPrefixAndTopThreeCandidates()
    {
        var candidates = new[]
        {
            new AiCandidate("pr", 42, 0.3, "weak PR match"),
            new AiCandidate("issue", 99, 0.25, "weak issue match"),
            new AiCandidate("pr", 7, 0.2, "older mention"),
            new AiCandidate("issue", 8, 0.1, "fourth mention")
        };

        var result = AiDetectionTooltips.ForUndecided(UndecidedReason.LowConfidence, candidates);

        Assert.Equal($"AI couldn't decide with confidence. Top candidates:{Environment.NewLine}PR #42 (confidence: 0.30) - weak PR match{Environment.NewLine}Issue #99 (confidence: 0.25) - weak issue match{Environment.NewLine}PR #7 (confidence: 0.20) - older mention", result);
    }

    [Fact]
    public void ForUndecided_AllAlreadyLinked_ReturnsAllLinkedMessage()
    {
        var result = AiDetectionTooltips.ForUndecided(UndecidedReason.AllAlreadyLinked, null);

        Assert.Equal("All matches were already linked to this session.", result);
    }
}
