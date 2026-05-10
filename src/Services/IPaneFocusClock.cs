namespace CopilotBooster.Services;

internal interface IPaneFocusClock
{
    /// <summary>
    /// Pauses execution for the specified number of milliseconds.
    /// </summary>
    void Sleep(int millis);
}
