# Project Context

- **Owner:** {user name}
- **Project:** {project description}
- **Stack:** {languages, frameworks, tools}
- **Created:** {timestamp}

## Learnings

### Trinity Lockout Pattern (2026-05-16)

Trinity authored the first GREEN implementation of the live CWD overlay feature. Dozer's gap-find tests revealed two critical gaps:
1. Missing callsite in `RefreshBackgroundCoreAsync` (full-refresh path revert bug)
2. Forward-slash trim not handled in Folder computation

Per ULTRA DIRECTIVE (2026-05-15T18-50-00Z), gaps = REJECT and original author is locked out. Trinity could not revise. Switch (new Services Dev agent) was hired as lockout relief and re-implemented from scratch, closing both gaps. Lesson: Gap-find tests enforce accountability; lockout ensures fresh eyes on revisions.

### Switch as Lockout Relief (2026-05-16)

Switch successfully handled Trinity's lockout by re-implementing the live CWD overlay feature from scratch. Both MainForm callsites were wired, forward-slash trim was added, and all 9 targeted tests passed. This establishes Switch as the designated lockout-relief Services Dev. Pattern: When an author is locked out by gap-find, hire a different agent with equivalent expertise.

### Oracle Binary Gate Reviews (2026-05-16)

Oracle performed gate reviews under the ULTRA DIRECTIVE binary regime (APPROVE or REJECT, no intermediate verdicts). During Trinity's attempt, Oracle approved despite Dozer's gaps being findable. During Switch's attempt, Oracle's binary gate review was rigorous and complete. Lesson: Binary verdicts demand higher rigor; intermediate language ("with gaps", "notes", "conditions") allows rigor to slip. The switch to binary verdicts visibly improved gate quality.

### Dozer Gap-Find Pattern (2026-05-16)

Dozer (analytical-diversity peer reviewer) added 5 gap-coverage tests to Tank's 4 RED tests:
- Case-sensitivity edge case: Windows paths differ only by case
- Multi-session independence: N sessions with mixed states processed correctly
- Forward-slash path edge case: Copilot CLI emits paths with forward slashes on Windows
- Missing callsite guard 1: OnDebouncedRefreshAsync ordered sequence
- Missing callsite guard 2: RefreshBackgroundCoreAsync missing overlay (gap Trinity would have missed)

Dozer's ordered source-contract guards (exact substring sequences in order) forced both callsites to be correctly placed. Lesson: Gap-find tests should be concrete, actionable (not fuzzy), and backed by specific edge cases from production usage patterns.
