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
    private const int HOTKEY_ID_SPOTLIGHT = 0xB001;
    private const int HOTKEY_ID_WINDOW_PIN = 0xB002;

    private const uint MOD_NOREPEAT = 0x4000;
#if DEBUG
    private const uint VK_F1 = 0x70;
    private const uint VK_F2 = 0x71;
#else
    // Modifier flags for RegisterHotKey
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_WIN = 0x0008;
    private const uint VK_X = 0x58;
    private const uint VK_C = 0x43;
#endif

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HotkeyWindow? _window;
    private bool _registered;

    /// <summary>
    /// Raised when Win+Alt+X (spotlight) is pressed.
    /// </summary>
    internal event Action? HotkeyPressed;

    /// <summary>
    /// Raised when Win+Alt+C (window pin) is pressed.
    /// </summary>
    internal event Action? WindowPinHotkeyPressed;

    /// <summary>
    /// Registers the global hotkeys. Must be called from the UI thread.
    /// </summary>
    internal bool Register()
    {
        if (this._registered)
        {
            return true;
        }

        this._window = new HotkeyWindow(this);
        this._window.CreateHandle(new CreateParams
        {
            Parent = new IntPtr(-3)
        });

#if DEBUG
        this._registered = RegisterHotKey(this._window.Handle, HOTKEY_ID_SPOTLIGHT, MOD_NOREPEAT, VK_F1);
        _ = RegisterHotKey(this._window.Handle, HOTKEY_ID_WINDOW_PIN, MOD_NOREPEAT, VK_F2);
#else
        this._registered = RegisterHotKey(this._window.Handle, HOTKEY_ID_SPOTLIGHT, MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_X);
        _ = RegisterHotKey(this._window.Handle, HOTKEY_ID_WINDOW_PIN, MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_C);
#endif
        if (!this._registered)
        {
            this._window.DestroyHandle();
            this._window = null;
        }

        return this._registered;
    }

    internal void Unregister()
    {
        if (this._registered && this._window != null)
        {
            UnregisterHotKey(this._window.Handle, HOTKEY_ID_SPOTLIGHT);
            UnregisterHotKey(this._window.Handle, HOTKEY_ID_WINDOW_PIN);
            this._window.DestroyHandle();
            this._window = null;
            this._registered = false;
        }
    }

    public void Dispose()
    {
        this.Unregister();
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly GlobalHotkeyService _owner;

        internal HotkeyWindow(GlobalHotkeyService owner)
        {
            this._owner = owner;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                if (m.WParam == HOTKEY_ID_SPOTLIGHT)
                {
                    this._owner.HotkeyPressed?.Invoke();
                }
                else if (m.WParam == HOTKEY_ID_WINDOW_PIN)
                {
                    this._owner.WindowPinHotkeyPressed?.Invoke();
                }
            }

            base.WndProc(ref m);
        }
    }
}
