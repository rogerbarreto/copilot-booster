using Microsoft.Extensions.Logging;

namespace CopilotBooster.IntegrationTests.Integration.TestTools;

internal sealed class CapturingLogger : ILogger
{
    internal List<CapturedLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        this.Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

internal sealed record CapturedLogEntry(LogLevel Level, string Message, Exception? Exception);
