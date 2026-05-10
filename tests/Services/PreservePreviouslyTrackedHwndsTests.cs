namespace CopilotBooster.Tests.Services;

public class PreservePreviouslyTrackedHwndsTests
{
    private static readonly Dictionary<string, string> s_summaries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Map Agent Workflow Sequence"] = "session-oh-my-codex",
        ["Fix auth bug"] = "session-auth",
    };

    [Fact]
    public void RepurposedTitle_NoLongerMatches_DropsHwnd()
    {
        // Regression: oh-my-codex pane was tagged "Copilot CLI" via session-summary match
        // when copilot.exe was alive. Copilot exited; the wt pane was reused for wsl/codex.
        // The previousHwnds fallback used to preserve the tag forever — now it must drop.
        var prevHwnd = new IntPtr(0xAAAA);
        var previouslyTracked = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["session-oh-my-codex"] = [("Copilot CLI", "Map Agent Workflow Sequence", prevHwnd)],
        };
        var results = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase);

        WindowFocusService.PreservePreviouslyTrackedHwnds(
            results,
            [],
            previouslyTracked,
            s_summaries,
            isWindowAlive: _ => true,
            readWindowTitle: _ => "rbarreto@ROGER-SERVER: /mnt/s/repo/community/oh-my-codex");

        Assert.False(results.ContainsKey("session-oh-my-codex"));
    }

    [Fact]
    public void TitleStillMatchesSummary_PreservesHwnd()
    {
        var prevHwnd = new IntPtr(0xBBBB);
        var previouslyTracked = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["session-auth"] = [("Copilot CLI", "Fix auth bug", prevHwnd)],
        };
        var results = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase);

        WindowFocusService.PreservePreviouslyTrackedHwnds(
            results,
            [],
            previouslyTracked,
            s_summaries,
            isWindowAlive: _ => true,
            readWindowTitle: _ => "Fix auth bug");

        Assert.True(results.ContainsKey("session-auth"));
        Assert.Single(results["session-auth"]);
        Assert.Equal(prevHwnd, results["session-auth"][0].Hwnd);
    }

    [Fact]
    public void EmojiPrefixedSummary_StillPreserves()
    {
        // Copilot CLI prefixes the wt pane title with an emoji while working;
        // MatchTrackedWindowTitle strips the emoji before matching, so the pane
        // must still be preserved across the transient title flicker.
        var prevHwnd = new IntPtr(0xCCCC);
        var previouslyTracked = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["session-auth"] = [("Copilot CLI", "Fix auth bug", prevHwnd)],
        };
        var results = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase);

        WindowFocusService.PreservePreviouslyTrackedHwnds(
            results,
            [],
            previouslyTracked,
            s_summaries,
            isWindowAlive: _ => true,
            readWindowTitle: _ => "\uD83E\uDD16 Fix auth bug");

        Assert.True(results.ContainsKey("session-auth"));
    }

    [Fact]
    public void HwndNotAlive_DoesNotPreserve()
    {
        var prevHwnd = new IntPtr(0xDDDD);
        var previouslyTracked = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["session-auth"] = [("Copilot CLI", "Fix auth bug", prevHwnd)],
        };
        var results = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase);

        WindowFocusService.PreservePreviouslyTrackedHwnds(
            results,
            [],
            previouslyTracked,
            s_summaries,
            isWindowAlive: _ => false,
            readWindowTitle: _ => "Fix auth bug");

        Assert.False(results.ContainsKey("session-auth"));
    }

    [Fact]
    public void HwndAlreadyMatchedFresh_DoesNotDoublePreserve()
    {
        var prevHwnd = new IntPtr(0xEEEE);
        var previouslyTracked = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["session-auth"] = [("Copilot CLI", "Fix auth bug", prevHwnd)],
        };
        var results = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<IntPtr> { prevHwnd };

        WindowFocusService.PreservePreviouslyTrackedHwnds(
            results,
            matched,
            previouslyTracked,
            s_summaries,
            isWindowAlive: _ => true,
            readWindowTitle: _ => "Fix auth bug");

        Assert.False(results.ContainsKey("session-auth"));
    }

    [Fact]
    public void TitleNowMatchesDifferentSession_DoesNotPreserveUnderOldSession()
    {
        var prevHwnd = new IntPtr(0xFFFF);
        var previouslyTracked = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["session-oh-my-codex"] = [("Copilot CLI", "Map Agent Workflow Sequence", prevHwnd)],
        };
        var results = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase);

        WindowFocusService.PreservePreviouslyTrackedHwnds(
            results,
            [],
            previouslyTracked,
            s_summaries,
            isWindowAlive: _ => true,
            readWindowTitle: _ => "Fix auth bug");

        Assert.False(results.ContainsKey("session-oh-my-codex"));
    }

    [Fact]
    public void SessionAlreadyInResults_SkipsFallback()
    {
        var prevHwnd = new IntPtr(0x10101);
        var freshHwnd = new IntPtr(0x20202);
        var previouslyTracked = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["session-auth"] = [("Copilot CLI", "Fix auth bug", prevHwnd)],
        };
        var results = new Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["session-auth"] = [("Copilot CLI", "Fix auth bug", freshHwnd)],
        };

        WindowFocusService.PreservePreviouslyTrackedHwnds(
            results,
            [freshHwnd],
            previouslyTracked,
            s_summaries,
            isWindowAlive: _ => true,
            readWindowTitle: _ => "Fix auth bug");

        Assert.Single(results["session-auth"]);
        Assert.Equal(freshHwnd, results["session-auth"][0].Hwnd);
    }
}
