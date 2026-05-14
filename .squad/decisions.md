# Team Decisions

## STANDING RULE: All-Green Test Suite Required (2026-05-13)

**Date:** 2026-05-13
**By:** Roger Barreto (Copilot directive)
**Status:** BINDING

Pre-existing test failures are NOT acceptable. The team may not declare work "done" while ANY test in the suite is failing, even if the failure pre-dates the current change. Whoever lands work that meets a red suite must either:
1. Fix the pre-existing failure as part of their delivery, OR
2. Escalate to the coordinator with a clear analysis of the failure and a plan, before claiming completion.

"Unrelated" is not a sufficient justification on its own. This is a standing release policy: the project ships only with a fully green suite.
