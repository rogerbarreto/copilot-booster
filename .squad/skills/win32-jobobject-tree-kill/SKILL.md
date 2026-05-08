---
name: win32-jobobject-tree-kill
description: Kill a spawned Windows process tree from C# with a Win32 Job Object.
---

# Win32 JobObject tree kill

## Use when

- A service spawns a process that may spawn descendants.
- User cancel or timeout must kill the full tree.
- No external NuGet dependency is allowed.

## C# pattern

1. Wrap P/Invoke in an internal static helper.
2. Create a job with `CreateJobObject`.
3. Set `JOBOBJECT_EXTENDED_LIMIT_INFORMATION.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`.
4. Start the child process.
5. Assign `process.Handle` using `AssignProcessToJobObject` immediately after start.
6. On cancel or timeout, call `TerminateJobObject(job, 1)`.
7. Close the job handle in `finally`.

## P/Invoke surface

```csharp
CreateJobObject
SetInformationJobObject
AssignProcessToJobObject
TerminateJobObject
CloseHandle
JOBOBJECT_EXTENDED_LIMIT_INFORMATION
JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
```

## Copilot Booster location

- Helper: `src/Services/Win32JobObject.cs`
- Production use: `src/Services/ProcessRunner.cs`
- Contract: `IProcessRunner.RunAsync(...) -> ProcessResult.WasKilled`

## Classification rule

- `WasKilled == true && ct.IsCancellationRequested` means user cancel. Log `outcome=cancelled`, leave failure class null.
- `WasKilled == true && !ct.IsCancellationRequested` means timeout.
