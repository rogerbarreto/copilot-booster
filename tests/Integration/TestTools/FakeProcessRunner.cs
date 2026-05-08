namespace CopilotBooster.IntegrationTests.Integration.TestTools;

/// <summary>
/// Reusable fake for tests that need to cross the copilot prompt process boundary.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly ProcessResult _result;

    internal FakeProcessRunner(ProcessResult result)
    {
        this._result = result;
    }

    internal List<FakeProcessRunnerCall> Calls { get; } = [];

    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        string cwd,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        this.Calls.Add(new FakeProcessRunnerCall(fileName, args.ToArray(), cwd, timeoutSeconds));
        return Task.FromResult(this._result);
    }
}

internal sealed record FakeProcessRunnerCall(string FileName, string[] Args, string Cwd, int TimeoutSeconds);
