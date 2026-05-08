namespace CopilotBooster.Services;

internal static class AiPromptBuilder
{
    private const string Template = """
You are helping CopilotBooster identify which GitHub issue(s) and/or pull request(s) a Copilot CLI session was working on.

# Repository (HARD CONSTRAINT)
The session belongs to repository: {owner}/{repo}
Only return candidates from this repository.

# Where to read the session
Folder: {abs_path_to_session_state_folder}
  - workspace.yaml
  - events.jsonl   (JSON Lines; can be large -- use tail/grep, don't read the whole thing)

Event types of interest:
  - user.message
  - assistant.message
  - tool.execution_*  (arguments and results often contain PR/issue refs)

Recent events at the END of events.jsonl reflect the session's current focus -- weight them higher.

# How to find candidates
1. Scan the conversation for EXPLICIT references to issue/PR numbers:
     #123, "PR 456", "issue 789", https://github.com/{owner}/{repo}/(pull|issues)/N
2. For each explicit reference, you MUST validate it exists with:
     gh issue view <N> --repo {owner}/{repo} --json number,title,state
     gh pr view <N> --repo {owner}/{repo} --json number,title,state
   A candidate that fails validation gets confidence < 0.5.

DO NOT search GitHub for issues or PRs that aren't already mentioned in the conversation.
DO NOT guess from keywords or topics.
If the conversation has NO explicit issue/PR references, return {"candidates": []}.

# Confidence rubric
  1.0       Explicitly mentioned AND validated, AND the conversation treats it as the active task ("fix #123", branch checked out from it, assigned).
  0.7-0.9   Explicitly mentioned, validated, referenced multiple times or in clearly task-relevant context.
  0.5-0.7   Explicitly mentioned, validated, but only in passing.
  < 0.5     Mentioned but validation failed (deleted / wrong repo / typo), OR ambiguous which of several refs is "the" target.

If multiple references exist, prefer ones in the most recent events.

# Output (STRICT)
Respond with EXACTLY ONE JSON object. No prose. No markdown code fences. No backticks.

{
  "candidates": [
    { "type": "issue" | "pr", "number": <int>, "confidence": <float 0.0-1.0>, "reasoning": "<one short sentence>" }
  ]
}

  - At most 3 candidates, sorted by confidence descending.
  - No candidates -> {"candidates": []}.
  - First character must be `{`, last must be `}`.
""";

    internal static string Build(string owner, string repo, string absSessionStateFolder)
    {
        return Template
            .Replace("{owner}", owner, System.StringComparison.Ordinal)
            .Replace("{repo}", repo, System.StringComparison.Ordinal)
            .Replace("{abs_path_to_session_state_folder}", absSessionStateFolder, System.StringComparison.Ordinal);
    }
}
