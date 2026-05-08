using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CopilotBooster.Services;

internal sealed record AiCandidate(string Type, int Number, double Confidence, string Reasoning);

internal static class AiResponseParser
{
    internal static IReadOnlyList<AiCandidate> Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<AiCandidate>();
            foreach (var item in candidates.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
                    ? typeProp.GetString() ?? ""
                    : "";
                var number = item.TryGetProperty("number", out var numberProp) && numberProp.ValueKind == JsonValueKind.Number && numberProp.TryGetInt32(out var parsedNumber)
                    ? parsedNumber
                    : 0;
                var confidence = item.TryGetProperty("confidence", out var confidenceProp) && confidenceProp.ValueKind == JsonValueKind.Number && confidenceProp.TryGetDouble(out var parsedConfidence)
                    ? parsedConfidence
                    : 0;
                var reasoning = item.TryGetProperty("reasoning", out var reasoningProp) && reasoningProp.ValueKind == JsonValueKind.String
                    ? reasoningProp.GetString() ?? ""
                    : "";

                result.Add(new AiCandidate(type, number, confidence, reasoning));
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
        catch (ArgumentException)
        {
            return [];
        }
    }
}
