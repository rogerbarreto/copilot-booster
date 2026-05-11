using System;

namespace CopilotBooster.Services;

internal sealed class WarpPaneFocuser
{
    internal const int DefaultMaxIterations = 30;
    internal const int DefaultSettleMillis = 150;

    private readonly IWindowTitleReader _titleReader;
    private readonly IKeyboardSender _keys;
    private readonly IPaneFocusClock _clock;
    private readonly Func<IntPtr, bool> _focusHwnd;
    private readonly int _maxIterations;
    private readonly int _settleMillis;
    private readonly int _warmupMillis;

    public WarpPaneFocuser(
        IWindowTitleReader titleReader,
        IKeyboardSender keys,
        IPaneFocusClock clock,
        Func<IntPtr, bool> focusHwnd,
        int maxIterations = DefaultMaxIterations,
        int settleMillis = DefaultSettleMillis,
        int warmupMillis = 0)
    {
        this._titleReader = titleReader;
        this._keys = keys;
        this._clock = clock;
        this._focusHwnd = focusHwnd;
        this._maxIterations = maxIterations;
        this._settleMillis = settleMillis;
        this._warmupMillis = warmupMillis;
    }

    /// <summary>
    /// Returns true iff a tab matching expectedTitle was found AND focused.
    /// On false, the focuser ends with the ORIGINAL pane active (full-cycle returns to start)
    ///   AND warp.exe is foregrounded.
    /// Match: case-insensitive ordinal exact comparison of GetWindowText output to expectedTitle.
    /// Algorithm:
    ///   1. Find main HWND for warpProcessId. If none → return false.
    ///   2. Foreground warp.exe (focusHwnd).
    ///   3. Read current title → originalTitle.
    ///   4. If originalTitle matches expectedTitle (case-insensitive) → return true.
    ///   5. Loop up to maxIterations:
    ///      a. SendNextTab() (Ctrl+PageDown for Warp).
    ///      b. Sleep(settleMillis).
    ///      c. Read title → currentTitle.
    ///      d. If currentTitle matches expectedTitle → return true.
    ///      e. If currentTitle equals originalTitle (we cycled back) → break loop, return false.
    ///   6. After loop exit (cap reached without match or cycled back): return false.
    ///      The caller should not assume original was restored automatically beyond the cycle-back case;
    ///      iteration cap reached without cycle-back is a degenerate case (rare) — log it.
    /// </summary>
    public bool TryFocusPane(int warpProcessId, string expectedTitle)
    {
        // Defensive: null/empty title → bail
        if (string.IsNullOrEmpty(expectedTitle))
        {
            return false;
        }

        // 1. Find main HWND
        IntPtr hwnd = this._titleReader.FindMainWindowHandle(warpProcessId);
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        // 2. Foreground warp.exe
        if (!this._focusHwnd(hwnd))
        {
            return false;
        }

        // 2b. Warmup pause: SetForegroundWindow returns before focus has
        // actually transferred, and SendInput sends to whatever window IS
        // foreground at fire time. Without this pause, Ctrl+Tab races with
        // the foreground transfer and lands on Booster instead of Warp,
        // leaving Warp focused but on the wrong tab.
        if (this._warmupMillis > 0)
        {
            this._clock.Sleep(this._warmupMillis);
        }

        // 3. Read current title → originalTitle
        string originalTitle = this._titleReader.ReadTitle(hwnd);

        // 4. If originalTitle matches expectedTitle → return true
        if (string.Equals(originalTitle, expectedTitle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 5. Loop up to maxIterations
        for (int i = 0; i < this._maxIterations; i++)
        {
            // a. SendNextTab() — Ctrl+PageDown for Warp on Windows
            this._keys.SendNextTab();

            // b. Sleep(settleMillis)
            this._clock.Sleep(this._settleMillis);

            // c. Read title → currentTitle
            string currentTitle = this._titleReader.ReadTitle(hwnd);

            // d. If currentTitle matches expectedTitle → return true
            if (string.Equals(currentTitle, expectedTitle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // e. If currentTitle equals originalTitle (we cycled back) → break loop, return false
            if (string.Equals(currentTitle, originalTitle, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        // 6. After loop exit: return false
        return false;
    }
}
