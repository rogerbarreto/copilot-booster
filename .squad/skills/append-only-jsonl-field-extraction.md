# Append Only JSONL Field Extraction

## Pattern

- Accept `TextReader` for pure parser seams.
- Read one line at a time. Do not materialize the whole JSONL file.
- Skip blank lines and malformed JSON with `catch (JsonException)`.
- Treat a malformed final line as normal because the writer may be mid append.
- Keep fallback fields separately from live fields. Return latest live value, else fallback, else null.

## Concurrent Writer Read

```csharp
using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
using var reader = new StreamReader(stream);
var value = ExtractLatestField(reader);
```

## Change Event Cache

- Store latest value per session in `ConcurrentDictionary<string, string>` with ordinal ignore case keys.
- Compare values with `StringComparison.OrdinalIgnoreCase`.
- Prime cache without firing events.
- Fire events only from watcher or poll paths when suppression is false.