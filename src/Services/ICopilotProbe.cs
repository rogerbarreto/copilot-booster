namespace CopilotBooster.Services;

internal interface ICopilotProbe
{
    bool IsCopilotAvailable();

    void InvalidateCache();
}
