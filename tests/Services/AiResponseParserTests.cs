namespace CopilotBooster.Tests.Services;

public sealed class AiResponseParserTests
{
    [Fact]
    public void Parse_ValidJsonWithOnePrCandidate_ReturnsCandidate()
    {
        const string Json = "{\"candidates\":[{\"type\":\"pr\",\"number\":42,\"confidence\":0.9,\"reasoning\":\"explicitly mentioned in latest user turn\"}]}";

        var candidates = AiResponseParser.Parse(Json);

        var candidate = Assert.Single(candidates);
        Assert.Equal("pr", candidate.Type);
        Assert.Equal(42, candidate.Number);
        Assert.Equal(0.9, candidate.Confidence);
        Assert.Equal("explicitly mentioned in latest user turn", candidate.Reasoning);
    }
}
