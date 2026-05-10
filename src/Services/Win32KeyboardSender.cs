using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CopilotBooster.Services;

[ExcludeFromCodeCoverage]
internal sealed partial class Win32KeyboardSender : IKeyboardSender
{
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_NEXT = 0x22;

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    public void SendNextTab()
    {
        // Warp's ActivateNextTab on Windows is bound to Ctrl+PageDown (VK_NEXT),
        // NOT Ctrl+Tab. See warp/app/src/util/bindings.rs.
        var inputs = new[]
        {
            Win32Input.KeyDown(VK_CONTROL),
            Win32Input.KeyDown(VK_NEXT),
            Win32Input.KeyUp(VK_NEXT),
            Win32Input.KeyUp(VK_CONTROL),
        };

        nint fg = GetForegroundWindow();
        uint sent = Win32Input.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Input.INPUT>());
        int lastError = Marshal.GetLastWin32Error();
        RuntimeDiagnosticLog.Write(
            "Win32KeyboardSender.SendNextTab fg={0} sent={1}/{2} lastError={3}",
            fg,
            sent,
            inputs.Length,
            lastError);
    }
}
