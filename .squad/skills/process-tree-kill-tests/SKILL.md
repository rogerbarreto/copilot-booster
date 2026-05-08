---
name: process-tree-kill-tests
description: Validate Windows process tree cancellation with real parent and child PIDs.
---

# Process tree kill tests

Use for real Windows tree-kill integration tests.

## Fixture pattern

```csharp
var beforePowerShell = SnapshotProcessIds("powershell");
var beforePing = SnapshotProcessIds("PING");
var command = "$p = Start-Process ping -ArgumentList '-n','60','127.0.0.1' -PassThru; Start-Sleep -Seconds 60";
await runner.RunAsync("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command], cwd, timeout, ct);
```

## Assertions

* Capture PID diffs for parent and child process names before cancellation.
* Cancel the `CancellationTokenSource`.
* Await the runner result and assert `WasKilled`.
* Assert every diffed parent and child PID is gone.
* Cleanup only captured PIDs with `Process.Kill(entireProcessTree: true)`.

## Avoid

* Do not kill by process name.
* Do not require external network.
* Do not add fixture executables unless OS commands are not deterministic enough.