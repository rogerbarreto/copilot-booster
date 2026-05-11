using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace CopilotBooster.Services;

[ExcludeFromCodeCoverage]
internal sealed class SystemPaneFocusClock : IPaneFocusClock
{
    public void Sleep(int millis)
    {
        Thread.Sleep(millis);
    }
}
