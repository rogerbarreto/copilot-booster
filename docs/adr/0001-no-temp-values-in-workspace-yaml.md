# 0001 — Booster does not write temporary values to `workspace.yaml.summary`

Status: Accepted (0.21.0)

## Decision

Copilot Booster may write `workspace.yaml` for the first time when it has a *real* value (e.g., a user-chosen session name at launch via `CopilotSessionCreatorService.CreateSession`). Booster must not write GUID/placeholder/heuristic values to `workspace.yaml.summary`. Temporary or heuristic display names live in the `session-names.json` **Sidecar** (see CONTEXT.md → **Booster-Resolved Name**) until Copilot CLI populates a real `summary` itself. Once `workspace.yaml` exists, Booster never updates `summary` again — that field is owned by Copilot CLI from creation onward.

## Why this matters

Without this rule, Booster's heuristics (window titles, GUIDs, placeholders) leak into a file Copilot CLI also writes, producing race conditions and "where did this name come from?" mysteries. With it, the file's lifecycle stays simple: Copilot CLI authors `summary`; Booster reads it.

## Considered alternatives

- **In-place mutation of `workspace.yaml.summary`** — produces races between Booster and Copilot CLI; legacy GUID values from older Booster versions polluted the field. Rejected.
- **Apply the rule even to user-chosen session names at launch** — too aggressive; the launch-time write is a legitimate first-write of a real value with no Copilot CLI competition. Rejected.

## Consequences

- The display-name resolution chain in `SessionService.LoadNamedSessions` is: `Alias` → `workspace.yaml.summary` → **Booster-Resolved Name** sidecar → cwd folder → GUID.
- Legacy `summary: <GUID>` values written by older Booster versions persist; they are not auto-migrated (rare-but-legitimate edge cases exist where a GUID-like string is a real summary).
- A new `SessionNameOverrideService` mirrors the existing `SessionAliasService` pattern.
