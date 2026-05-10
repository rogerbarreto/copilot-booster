using System.Collections.Generic;

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
3. After a candidate validates, ALSO discover its linked counterpart via the GitHub
   "Development" relationship (the right-rail "Development" panel and `Closes #N` /
   `Fixes #N` keywords both flow into this connection):
     - For an issue: gh issue view <N> --repo {owner}/{repo} --json closedByPullRequestsReferences
       Each entry's `number` is a PR that, when merged, closes this issue. Add it as a `pr` candidate.
     - For a PR: gh pr view <N> --repo {owner}/{repo} --json closingIssuesReferences
       Each entry's `number` is an issue this PR will close. Add it as an `issue` candidate.
   Linkage rules:
     - Linked counterparts inherit the source candidate's confidence, capped at 0.95
       (linkage is structural but the counterpart was not explicitly mentioned).
     - REJECT linked items whose repository differs from {owner}/{repo}
       (compare `repository.owner.login` and `repository.name`; honor the HARD CONSTRAINT above).
     - The `reasoning` MUST mention the linkage, e.g. "linked PR via Development relationship from issue #15".
     - Empty linkage array -> no extra candidate.
     - Deduplicate: if a candidate was already added from an explicit reference, do not add it again from linkage.

DO NOT search GitHub for issues or PRs that aren't already mentioned in the conversation
or surfaced via the Development linkage in step 3.
DO NOT guess from keywords or topics.
If the conversation has NO explicit issue/PR references, return {"candidates": []}.

# Confidence rubric
  1.0       Explicitly mentioned AND validated, AND the conversation treats it as the active task ("fix #123", branch checked out from it, assigned).
  0.7-0.9   Explicitly mentioned, validated, referenced multiple times or in clearly task-relevant context.
  0.5-0.7   Explicitly mentioned, validated, but only in passing.
  < 0.5     Mentioned but validation failed (deleted / wrong repo / typo), OR ambiguous which of several refs is "the" target.

If multiple references exist, prefer ones in the most recent events.

{seed_section}# Output (STRICT)
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

    internal sealed record ExistingAttachment(string Type, int Number);

    internal static string Build(string owner, string repo, string absSessionStateFolder)
    {
        return Build(owner, repo, absSessionStateFolder, []);
    }

    internal static string Build(string owner, string repo, string absSessionStateFolder, IReadOnlyList<ExistingAttachment> existingAttachments)
    {
        var seedSection = BuildSeedSection(existingAttachments);

        return Template
            .Replace("{owner}", owner, System.StringComparison.Ordinal)
            .Replace("{repo}", repo, System.StringComparison.Ordinal)
            .Replace("{abs_path_to_session_state_folder}", absSessionStateFolder, System.StringComparison.Ordinal)
            .Replace("{seed_section}", seedSection, System.StringComparison.Ordinal);
    }

    private static string BuildSeedSection(IReadOnlyList<ExistingAttachment> existingAttachments)
    {
        if (existingAttachments is null || existingAttachments.Count == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Seed candidates from existing session attachments");
        sb.AppendLine("The session is already linked to these GitHub items in this repository:");
        foreach (var item in existingAttachments)
        {
            sb.Append("  - ").Append(item.Type).Append(" #").Append(item.Number).AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("For each existing attachment:");
        sb.AppendLine("  1. Skip the validation step (step 2) — the user already linked it; treat it as valid.");
        sb.AppendLine("  2. Run linkage discovery from step 3 directly against it.");
        sb.AppendLine("  3. Add each linked counterpart returned by the Development linkage as a candidate (confidence 0.95).");
        sb.AppendLine("  4. Do NOT include the existing attachments themselves as candidates — they are already linked.");
        sb.AppendLine();
        return sb.ToString();
    }
}
