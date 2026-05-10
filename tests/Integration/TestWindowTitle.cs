using System.Runtime.CompilerServices;

namespace CopilotBooster.IntegrationTests;

/// <summary>
/// Builds an Administrator-style prefix that identifies which integration test
/// spawned a particular window. If a test leaks a window, the title makes the
/// originating method visible at a glance.
///
/// The prefix uses the form <c>"BoosterTest-{testName}:  "</c>. The trailing
/// <c>":  "</c> (single token + colon + two spaces) reuses the existing
/// "Administrator:  " strip pattern in <c>WindowFocusService.MatchTrackedWindowTitle</c>,
/// so adding the prefix never breaks the production matchers.
/// </summary>
internal static class TestWindowTitle
{
    private const string Prefix = "BoosterTest";

    /// <summary>
    /// Returns <c>"BoosterTest-{caller}:  {label}"</c>. The caller is captured
    /// via <see cref="CallerMemberNameAttribute"/> so each test method tags the
    /// windows it opens automatically.
    /// </summary>
    internal static string For(string label, [CallerMemberName] string caller = "")
    {
        var sanitized = string.IsNullOrWhiteSpace(caller) ? "anonymous" : caller;
        return $"{Prefix}-{sanitized}:  {label}";
    }

    /// <summary>
    /// Returns the tag-only prefix (no label) for callers that need a bare
    /// recognizable title, such as <c>wt.exe -w new new-tab --title</c> spawns
    /// where copilot will overwrite the title once it starts.
    /// </summary>
    internal static string Tag([CallerMemberName] string caller = "")
    {
        var sanitized = string.IsNullOrWhiteSpace(caller) ? "anonymous" : caller;
        return $"{Prefix}-{sanitized}";
    }
}
