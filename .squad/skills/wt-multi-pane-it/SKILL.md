---
name: wt-multi-pane-it
description: Build or maintain LocalOnly Windows Terminal multi-pane integration tests for Copilot Booster.
---

# Windows Terminal Multi-Pane Integration Test Pattern

Use this when a test must prove Copilot Booster can detect and focus Copilot CLI sessions hosted inside Windows Terminal tabs or split panes.

## Required Shape

- Mark the test with both:

```csharp
[LocalOnlyFact]
[Trait("Category", "LocalOnly")]
```

- Keep the method name explicit about the live WT behavior.
- Run UI-sensitive code on an STA thread if the test creates WinForms controls or uses `Application.DoEvents()`.
- Put the class in `WindowEventHookCollection` if it manipulates real windows or foreground focus.

## Preflight Must Skip, Not Fail

Skip with `Assert.Skip` when any of these is missing:

- interactive desktop (`Environment.UserInteractive`, non-session-0, foreground HWND exists);
- `wt.exe` on PATH (`where.exe wt.exe` is enough);
- `copilot --help` succeeds and advertises `--deny-url`;
- Copilot exits before the test can observe a live process.

## Spawn Pattern

Create one PowerShell wrapper script per pane. Launch WT directly; do not invoke through PowerShell unless necessary.

```csharp
var args = "-w \"cb-it-<guid>\" "
    + "new-tab --title \"PaneA-Probe\" --suppressApplicationTitle powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"pane-a.ps1\""
    + " ; "
    + "split-pane --title \"PaneB-Probe\" --suppressApplicationTitle powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"pane-b.ps1\"";

Process.Start(new ProcessStartInfo
{
    FileName = "wt.exe",
    Arguments = args,
    UseShellExecute = true
});
```

Semicolons are WT command separators when passed directly in `ProcessStartInfo.Arguments`; do not escape them as ``\;`` unless a shell is interpreting the command first.

## PID and Session Mapping

`--deny-url=<guid>` is a beacon for process discovery, not the session ID.

Wrapper script pattern:

1. `Start-Process copilot -ArgumentList @('--deny-url=<guid>') -PassThru`.
2. Poll `Get-CimInstance Win32_Process` for `CommandLine -like '*--deny-url=<guid>*'`.
3. Write the discovered real Copilot PID to a marker file.
4. `Wait-Process` on that PID so the pane remains tied to the live process.

C# mapping pattern:

1. Read marker PID.
2. Find `~/.copilot/logs/process-*-<pid>.log`.
3. Parse with `CopilotLogWatcherService.TryParseLogContent` to get `session_id`.

## Booster Name Resolution

To force deterministic display names:

1. Let `CopilotLogWatcherService` discover the external session and set the unresolved placeholder.
2. Append a JSONL line to `<session>/events.jsonl`:

```json
{"type":"user.message","data":{"content":"PaneA-Probe"}}
```

3. Wait for `SessionNameOverrideService.Get(...).ResolvedFromUserMessage == true`.
4. Re-resolve the host entry if pane matching needs the new label:

```csharp
tracker.RemoveCopilotHost(sessionId);
tracker.HandleExternalSessionDiscovered(sessionId, copilotPid);
```

## Assertions

Assert all of these when running live:

- `SessionGridVisuals` displays the resolved label, not the GUID.
- Running apps contains `Copilot CLI`.
- `tracker.GetCopilotHost(sessionId).HostKindLabel == "Windows Terminal"`.
- Focus migrates to the WT HWND.
- `WindowsTerminalPaneGateway.EnumeratePanes(wtHwnd)` reports the expected pane/tab selected.

## Cleanup

Never pre-clean Copilot session state for this test. On teardown:

- Kill only captured Copilot PIDs.
- Send `WM_CLOSE` to captured WT HWNDs.
- Move created session directories to `%TEMP%\copilot-booster-it-<timestamp>\`; do not delete them.
