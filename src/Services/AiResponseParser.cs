using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CopilotBooster.Services;

internal sealed record AiCandidate(string Type, int Number, double Confidence, string Reasoning);

internal abstract record AiParseResult
{
    internal sealed record Success(IReadOnlyList<AiCandidate> Candidates) : AiParseResult;

    internal sealed record Failure(AiFailureClass Class, string Reason) : AiParseResult
    {
        internal AiFailureClass FailureClass => this.Class;
    }
}

internal static class AiResponseParser
{
    internal static AiParseResult Parse(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return new AiParseResult.Failure(AiFailureClass.MalformedJson, "stdout was empty");
        }

        var trimmed = stdout.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{' || trimmed[^1] != '}')
        {
            return new AiParseResult.Failure(AiFailureClass.MalformedJson, "stdout was not a pure JSON object");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(stdout);
        }
        catch (JsonException ex)
        {
            return new AiParseResult.Failure(AiFailureClass.MalformedJson, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return new AiParseResult.Failure(AiFailureClass.MalformedJson, ex.Message);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new AiParseResult.Failure(AiFailureClass.SchemaViolation, "top-level JSON value was not an object");
            }

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates))
            {
                return new AiParseResult.Failure(AiFailureClass.SchemaViolation, "missing candidates array");
            }

            if (candidates.ValueKind != JsonValueKind.Array)
            {
                return new AiParseResult.Failure(AiFailureClass.SchemaViolation, "candidates was not an array");
            }

            var result = new List<(AiCandidate Candidate, int Index)>();
            var index = 0;
            foreach (var item in candidates.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return new AiParseResult.Failure(AiFailureClass.SchemaViolation, $"candidate at index {index} was not an object");
                }

                if (!TryReadCandidate(item, index, out var candidate, out var reason))
                {
                    return new AiParseResult.Failure(AiFailureClass.SchemaViolation, reason);
                }

                result.Add((candidate, index));
                index++;
            }

            // Empty candidates is valid JSON. AiDetectionService classifies it as NoCandidates.
            return new AiParseResult.Success(result.Select(item => item.Candidate).ToList());
        }
    }

    private static bool TryReadCandidate(JsonElement item, int index, out AiCandidate candidate, out string reason)
    {
        candidate = new AiCandidate("", 0, 0, "");

        if (!TryGetRequiredProperty(item, "type", index, out var typeProp, out reason)
            || !TryGetRequiredProperty(item, "number", index, out var numberProp, out reason)
            || !TryGetRequiredProperty(item, "confidence", index, out var confidenceProp, out reason)
            || !TryGetRequiredProperty(item, "reasoning", index, out var reasoningProp, out reason))
        {
            return false;
        }

        if (typeProp.ValueKind != JsonValueKind.String)
        {
            reason = $"candidate at index {index} field type was not a string";
            return false;
        }

        var type = typeProp.GetString() ?? "";
        if (type is not ("issue" or "pr"))
        {
            reason = $"candidate at index {index} field type was not issue or pr";
            return false;
        }

        if (numberProp.ValueKind != JsonValueKind.Number || !numberProp.TryGetInt32(out var number))
        {
            reason = $"candidate at index {index} field number was not an integer";
            return false;
        }

        if (number <= 0)
        {
            reason = $"candidate at index {index} field number was not positive";
            return false;
        }

        if (confidenceProp.ValueKind != JsonValueKind.Number || !confidenceProp.TryGetDouble(out var confidence))
        {
            reason = $"candidate at index {index} field confidence was not a number";
            return false;
        }

        if (confidence is < 0.0 or > 1.0)
        {
            reason = $"candidate at index {index} field confidence was outside [0.0, 1.0]";
            return false;
        }

        if (reasoningProp.ValueKind != JsonValueKind.String)
        {
            reason = $"candidate at index {index} field reasoning was not a string";
            return false;
        }

        candidate = new AiCandidate(type, number, confidence, reasoningProp.GetString() ?? "");
        reason = string.Empty;
        return true;
    }

    private static bool TryGetRequiredProperty(JsonElement item, string propertyName, int index, out JsonElement property, out string reason)
    {
        if (item.TryGetProperty(propertyName, out property))
        {
            reason = string.Empty;
            return true;
        }

        reason = $"candidate at index {index} was missing {propertyName}";
        return false;
    }
}
