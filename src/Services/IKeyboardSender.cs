namespace CopilotBooster.Services;

internal interface IKeyboardSender
{
    /// <summary>
    /// Sends Ctrl+Tab as a discrete key sequence to the currently-focused window.
    /// Caller is responsible for foregrounding the target HWND first.
    /// </summary>
    void SendCtrlTab();
}
