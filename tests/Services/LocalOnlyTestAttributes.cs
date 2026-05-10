using System.Runtime.CompilerServices;

namespace CopilotBooster.Tests.Services;

internal static class LocalOnlyTestGate
{
    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("COPILOT_BOOSTER_RUN_LOCALONLY"), "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("COPILOT_BOOSTER_RUN_LOCALONLY"), "true", StringComparison.OrdinalIgnoreCase);
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class LocalOnlyFactAttribute : FactAttribute
{
    public LocalOnlyFactAttribute([CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        this.Skip = "LocalOnly heavy memory test; set COPILOT_BOOSTER_RUN_LOCALONLY=1 to run.";
        this.SkipType = typeof(LocalOnlyTestGate);
        this.SkipUnless = nameof(LocalOnlyTestGate.IsEnabled);
    }
}
