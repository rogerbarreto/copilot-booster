using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotBooster.Services;

internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string cwd, int timeoutSeconds, CancellationToken ct);
}

internal sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool WasKilled);
