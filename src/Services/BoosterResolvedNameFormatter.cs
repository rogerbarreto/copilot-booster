using System.Text.RegularExpressions;

namespace CopilotBooster.Services;

/// <summary>
/// Formats raw user messages into Booster-Resolved Name display strings.
/// </summary>
internal static partial class BoosterResolvedNameFormatter
{
    private const int MaxLength = 32;
    private const string Ellipsis = "\u2026"; // Unicode ellipsis character

    [GeneratedRegex(@"^```[^\n]*\n")]
    private static partial Regex CodeFenceRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Formats a raw user.message body into a Booster-Resolved Name display string.
    /// Rules (apply in order):
    ///   1. Return null if input is null/empty/whitespace-only.
    ///   2. Strip leading triple-backtick code fences (```lang\n... or just ```\n).
    ///   3. Trim whitespace from both ends.
    ///   4. Collapse runs of whitespace (any \s incl. newlines, tabs) to a single space.
    ///   5. If resulting length ≤ 32: return as-is.
    ///   6. If length > 32: take first 32 chars + Unicode "…" (single char, U+2026). Final length = 33.
    /// </summary>
    internal static string? Format(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Strip leading triple-backtick code fences
        var result = CodeFenceRegex().Replace(raw, string.Empty);

        // Trim whitespace
        result = result.Trim();

        // Collapse runs of whitespace to a single space
        result = WhitespaceRegex().Replace(result, " ");

        // Truncate if needed
        if (result.Length <= MaxLength)
        {
            return result;
        }

        return result.Substring(0, MaxLength) + Ellipsis;
    }

    /// <summary>
    /// Builds the unresolved placeholder for a Copilot CLI session given its host process name.
    /// Format: "{HostProcessName}:Copilot". E.g., "WindowsTerminal:Copilot", "pwsh:Copilot".
    /// If hostProcessName is null/empty/whitespace, returns "Copilot".
    /// </summary>
    internal static string BuildPlaceholder(string? hostProcessName)
    {
        if (string.IsNullOrWhiteSpace(hostProcessName))
        {
            return "Copilot";
        }

        return $"{hostProcessName.Trim()}:Copilot";
    }
}
