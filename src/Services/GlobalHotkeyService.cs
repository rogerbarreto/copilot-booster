using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CopilotBooster.Services;

/// <summary>
/// Registers a system-wide global hotkey and raises an event when it is pressed.
/// Uses a hidden message-only window to receive <c>WM_HOTKEY</c> messages.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0xB001;

    // Modifier flags for RegisterHotKey
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private const uint VK_X = 0x58;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HotkeyWindow? _window;
    private bool _registered;

    /// <summary>
    /// Raised when the registered global hotkey is pressed.
    /// </summary>
    internal event Action? HotkeyPressed;

    /// <summary>
    /// Registers the global hotkey (Win+Alt+Space). Must be called from the UI thread.
    /// </summary>
    /// <returns><c>true</c> if registration succeeded; otherwise <c>false</c>.</returns>
    internal bool Register()
    {
        if (this._registered)
        {
            return true;
        }

        this._window = new HotkeyWindow(this);
        this._window.CreateHandle(new CreateParams
        {
            // HWND_MESSAGE parent creates a message-only window
            Parent = new IntPtr(-3)
        });

        this._registered = RegisterHotKey(this._window.Handle, HOTKEY_ID, MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_X);
        if (!this._registered)
        {
            this._window.DestroyHandle();
            this._window = null;
        }

        return this._registered;
    }

    /// <summary>
    /// Unregisters the global hotkey and destroys the message window.
    /// </summary>
    internal void Unregister()
    {
        if (this._registered && this._window != null)
        {
            UnregisterHotKey(this._window.Handle, HOTKEY_ID);
            this._window.DestroyHandle();
            this._window = null;
            this._registered = false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Unregister();
    }

    /// <summary>
    /// Hidden NativeWindow that receives WM_HOTKEY messages.
    /// </summary>
    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly GlobalHotkeyService _owner;

        internal HotkeyWindow(GlobalHotkeyService owner)
        {
            this._owner = owner;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam == HOTKEY_ID)
            {
                this._owner.HotkeyPressed?.Invoke();
            }

            base.WndProc(ref m);
        }
    }
}
