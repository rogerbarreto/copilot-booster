namespace CopilotBooster.Tests.Services;

public sealed class AiResponseParserTests
{
    [Theory]
    [MemberData(nameof(StrictValidationRows))]
    public void Parse_StrictValidationMatrix_ReturnsExpectedBranch(string name, string json, object? expectedFailureClass, int? expectedCount, double[]? expectedConfidences)
    {
        _ = name;

        var result = AiResponseParser.Parse(json);

        if (expectedFailureClass is string failureClassName)
        {
            var failure = Assert.IsType<AiParseResult.Failure>(result);
            Assert.Equal(Enum.Parse<AiFailureClass>(failureClassName), failure.FailureClass);
            return;
        }

        if (expectedFailureClass is AiFailureClass failureClass)
        {
            var failure = Assert.IsType<AiParseResult.Failure>(result);
            Assert.Equal(failureClass, failure.FailureClass);
            return;
        }

        var success = Assert.IsType<AiParseResult.Success>(result);
        Assert.Equal(expectedCount, success.Candidates.Count);
        if (expectedConfidences != null)
        {
            Assert.Equal(expectedConfidences, success.Candidates.Select(candidate => candidate.Confidence).ToArray());
        }
    }

    [Fact]
    public void Parse_ThreeValidCandidates_PreservesInputOrder()
    {
        const string Json = "{\"candidates\":[{\"type\":\"issue\",\"number\":1,\"confidence\":0.2,\"reasoning\":\"first\"},{\"type\":\"pr\",\"number\":2,\"confidence\":0.9,\"reasoning\":\"second\"},{\"type\":\"issue\",\"number\":3,\"confidence\":0.5,\"reasoning\":\"third\"}]}";

        var result = AiResponseParser.Parse(Json);

        var success = Assert.IsType<AiParseResult.Success>(result);
        Assert.Equal([1, 2, 3], success.Candidates.Select(candidate => candidate.Number).ToArray());
    }

    public static IEnumerable<object?[]> StrictValidationRows()
    {
        yield return Failure("empty string", "", AiFailureClass.MalformedJson);
        yield return Failure("whitespace only", "   \n  ", AiFailureClass.MalformedJson);
        yield return Failure("pure prose", "no candidates found", AiFailureClass.MalformedJson);
        yield return Failure("json fenced with language", "```json\n{\"candidates\":[]}\n```", AiFailureClass.MalformedJson);
        yield return Failure("json fenced without language", "```\n{\"candidates\":[]}\n```", AiFailureClass.MalformedJson);
        yield return Failure("leading prose", "Here is the result:\n{\"candidates\":[]}", AiFailureClass.MalformedJson);
        yield return Failure("trailing prose", "{\"candidates\":[]}\nHope this helps!", AiFailureClass.MalformedJson);
        yield return Failure("truly malformed json", "{candidates: [", AiFailureClass.MalformedJson);

        yield return Failure("missing candidates", "{\"foo\":\"bar\"}", AiFailureClass.SchemaViolation);
        yield return Failure("candidates object", "{\"candidates\":{}}", AiFailureClass.SchemaViolation);
        yield return Failure("missing type", "{\"candidates\":[{\"number\":1,\"confidence\":0.9,\"reasoning\":\"x\"}]}", AiFailureClass.SchemaViolation);
        yield return Failure("missing number", "{\"candidates\":[{\"type\":\"pr\",\"confidence\":0.9,\"reasoning\":\"x\"}]}", AiFailureClass.SchemaViolation);
        yield return Failure("missing confidence", "{\"candidates\":[{\"type\":\"pr\",\"number\":1,\"reasoning\":\"x\"}]}", AiFailureClass.SchemaViolation);
        yield return Failure("missing reasoning", "{\"candidates\":[{\"type\":\"pr\",\"number\":1,\"confidence\":0.9}]}", AiFailureClass.SchemaViolation);
        yield return Failure("uppercase type", "{\"candidates\":[{\"type\":\"PR\",\"number\":1,\"confidence\":0.9,\"reasoning\":\"x\"}]}", AiFailureClass.SchemaViolation);
        yield return Failure("unknown type", "{\"candidates\":[{\"type\":\"bug\",\"number\":1,\"confidence\":0.9,\"reasoning\":\"x\"}]}", AiFailureClass.SchemaViolation);
        yield return Failure("zero number", "{\"candidates\":[{\"type\":\"pr\",\"number\":0,\"confidence\":0.9,\"reasoning\":\"x\"}]}", AiFailureClass.SchemaViolation);
        yield return Failure("negative number", "{\"candidates\":[{\"type\":\"pr\",\"number\":-1,\"confidence\":0.9,\"reasoning\":\"x\"}]}", AiFailureClass.SchemaViolation);
        yield return Failure("string number", "{\"candidates\":[{\"type\":\"pr\",\"number\":\"42\",\"confidence\":0.9,\"reasoning\":\"x\"}]}", AiFailureClass.SchemaViolation);
        yield return Failure("confidence above range", "{\"candidates\":[{\"type\":\"pr\",\"number\":1,\"confidence\":1.5,\"reasoning\":\"x\"}]}", AiFailureClass.SchemaViolation);
        yield return Failure("confidence below range", "{\"candidates\":[{\"type\":\"pr\",\"number\":1,\"confidence\":-0.1,\"reasoning\":\"x\"}]}", AiFailureClass.SchemaViolation);
        yield return Failure("string confidence", "{\"candidates\":[{\"type\":\"pr\",\"number\":1,\"confidence\":\"0.9\",\"reasoning\":\"x\"}]}", AiFailureClass.SchemaViolation);
        yield return Failure("mixed valid and invalid", "{\"candidates\":[{\"type\":\"pr\",\"number\":1,\"confidence\":0.9,\"reasoning\":\"x\"},{\"type\":\"issue\",\"number\":2,\"confidence\":0.8,\"reasoning\":\"y\"},{\"type\":\"bug\",\"number\":3,\"confidence\":0.7,\"reasoning\":\"z\"}]}", AiFailureClass.SchemaViolation);

        yield return Success("empty candidates", "{\"candidates\":[]}", 0);
        yield return Success("one valid candidate", "{\"candidates\":[{\"type\":\"pr\",\"number\":42,\"confidence\":0.9,\"reasoning\":\"x\"}]}", 1);
        yield return Success("three valid candidates", "{\"candidates\":[{\"type\":\"issue\",\"number\":1,\"confidence\":0.2,\"reasoning\":\"first\"},{\"type\":\"pr\",\"number\":2,\"confidence\":0.9,\"reasoning\":\"second\"},{\"type\":\"issue\",\"number\":3,\"confidence\":0.5,\"reasoning\":\"third\"}]}", 3);
        yield return Success("more than three preserves all in input order", "{\"candidates\":[{\"type\":\"pr\",\"number\":1,\"confidence\":0.1,\"reasoning\":\"a\"},{\"type\":\"pr\",\"number\":2,\"confidence\":0.9,\"reasoning\":\"b\"},{\"type\":\"pr\",\"number\":3,\"confidence\":0.5,\"reasoning\":\"c\"},{\"type\":\"pr\",\"number\":4,\"confidence\":0.7,\"reasoning\":\"d\"},{\"type\":\"pr\",\"number\":5,\"confidence\":0.3,\"reasoning\":\"e\"}]}", 5, [0.1, 0.9, 0.5, 0.7, 0.3]);
        yield return Success("confidence inclusive bounds", "{\"candidates\":[{\"type\":\"issue\",\"number\":1,\"confidence\":0.0,\"reasoning\":\"low\"},{\"type\":\"pr\",\"number\":2,\"confidence\":1.0,\"reasoning\":\"high\"}]}", 2, [0.0, 1.0]);
    }

    private static object?[] Failure(string name, string json, AiFailureClass failureClass)
    {
        return [name, json, failureClass.ToString(), null, null];
    }

    private static object?[] Success(string name, string json, int count, double[]? confidences = null)
    {
        return [name, json, null, count, confidences];
    }
}
