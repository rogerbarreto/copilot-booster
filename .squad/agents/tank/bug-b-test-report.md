## Bug B Test Suite - 2026-05-10 01:33

Created 5 test files for Bug B (stale session-pid mapping fix):

### Test Files Created
1. **SessionPidLivenessValidatorTests.cs** - ✅ PASSING (5 tests)
   - Pure DateTime overload tests for the liveness invariant
   - Tests Roger's exact Bug B scenario (mtime 8 hours before process start)
   - Tests fudge factor handling for clock skew

2. **HandleExternalSessionDiscoveredGateTests.cs** - ✅ PASSING (1 test)
   - Tests T1 watcher gate path
   - Note: HandleExternalSessionDiscovered calls real-FS validator directly, not injected callback

3. **IsCopilotHostActiveSessionAwareTests.cs** - ✅ PASSING (2 tests)
   - Tests session-aware eviction of EXISTING stale hosts
   - Validates that BuildActiveText drops "Copilot CLI" badge when session is stale

4. **RescanExistingSessionsEvictionTests.cs** - ✅ PASSING (1 test)
   - Tests discrimination between fresh and stale sessions
   - Validates ReprojectActiveCopilotHosts evicts stale hosts correctly

5. **TryFocusCopilotCliPidFallbackTests.cs** - ❌ FAILING (1 of 2 tests)
   - **BUG FOUND**: TryFocusCopilotCli_StaleSession_DoesNotFocus fails
   - Focus callback IS invoked even when session is stale
   - Priority 1 path (line 1189) should check IsCopilotHostActive, but focus still happens
   - Trinity needs to investigate why the liveness gate isn't working in the focus path

### Key Findings
- Trinity HAS already implemented the Bug B fix (all APIs exist)
- 4 of 5 test files pass completely (9 of 10 tests passing)
- 1 test reveals a remaining bug in the focus path
- The session-aware liveness check works correctly for badge display/eviction
- The focus path has a gap that needs fixing

### Implementation Details Learned
- 8-arg constructor exists with \Func<string, int, bool> isSessionLiveForCopilotPid\
- IsCopilotHostActive(string sessionId, CopilotHostInfo) is session-aware
- HandleExternalSessionDiscovered calls SessionPidLivenessValidator.IsLive directly (real-FS)
- SetCopilotHost immediately projects to _activeTrackedWindows
- ReprojectActiveCopilotHosts correctly evicts stale hosts
