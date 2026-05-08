namespace CopilotBooster.IntegrationTests.Integration.TestTools;

/// <summary>
/// Reusable fake for tests that need to cross the copilot prompt process boundary.
/// Supports canned results plus a one-shot exception mode for process-spawn failures.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private ProcessResult _result;
    private Exception? _nextException;

    internal FakeProcessRunner(ProcessResult result)
    {
        this._result = result;
    }

    internal List<FakeProcessRunnerCall> Calls { get; } = [];

    internal void SetResult(ProcessResult result)
    {
        this._result = result;
    }

    internal void ThrowOnNextCall(Exception ex)
    {
        this._nextException = ex;
    }

    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        string cwd,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        this.Calls.Add(new FakeProcessRunnerCall(fileName, args.ToArray(), cwd, timeoutSeconds));
        if (this._nextException != null)
        {
            var ex = this._nextException;
            this._nextException = null;
            throw ex;
        }

        return Task.FromResult(this._result);
    }
}

internal sealed record FakeProcessRunnerCall(string FileName, string[] Args, string Cwd, int TimeoutSeconds);
