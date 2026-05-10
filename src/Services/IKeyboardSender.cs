namespace CopilotBooster.Services;

internal interface IKeyboardSender
{
    /// <summary>
    /// Sends the host's "next tab" keyboard shortcut as a discrete key sequence
    /// to the currently-focused window. Caller is responsible for foregrounding
    /// the target HWND first.
    /// </summary>
    /// <remarks>
    /// Implementations are host-specific. The Win32 implementation sends Ctrl+PageDown,
    /// which is Warp Terminal's default <c>ActivateNextTab</c> binding on Windows
    /// (see warp/app/src/util/bindings.rs in the Warp source). Ctrl+Tab is the
    /// shortcut on macOS and several other terminals, but Warp on Windows does NOT
    /// bind it to tab navigation.
    /// </remarks>
    void SendNextTab();
}
