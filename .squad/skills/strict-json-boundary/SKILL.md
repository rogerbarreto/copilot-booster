---
name: strict-json-boundary
description: Use a discriminated parser result at service boundaries that need strict JSON validation and classified failures.
domain: csharp-services
confidence: medium
source: issue-18
---

## Pattern

Use an internal discriminated result record when a parser needs to return either validated data or a classified failure.

```csharp
internal abstract record ParseResult
{
    internal sealed record Success(IReadOnlyList<T> Items) : ParseResult;
    internal sealed record Failure(FailureClass Class, string Reason) : ParseResult;
}
```

## Rules

* Parser validates syntax and schema. Caller classifies domain outcomes such as empty result.
* Return failure objects rather than throwing for expected bad model output.
* Keep the enum deterministic when tests or UI depend on stable values.
* Sort and truncate only for explicitly lenient cases. Preserve input order otherwise.
* Log raw boundary data at debug level when the seed requires reproducibility.

## Example

`AiResponseParser.Parse(stdout)` returns `AiParseResult.Success` for valid JSON, including empty `candidates`. `AiDetectionService` maps empty success to `AiFailureClass.NoCandidates` and writes the terminal log at warning level.
