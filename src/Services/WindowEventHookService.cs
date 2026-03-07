using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CopilotBooster.Services;

/// <summary>
/// Wraps Win32 SetWinEventHook / UnhookWinEvent to monitor window lifecycle events.
/// WINEVENT_OUTOFCONTEXT delivers callbacks on the thread that called SetWinEventHook
/// (the UI thread), so no cross-thread marshaling is needed for the events themselves.
/// </summary>
[ExcludeFromCodeCoverage]
internal partial class WindowEventHookService : IDisposable
{
    private const uint WINEVENT_OUTOFCONTEXT = 0x0002;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const int OBJID_WINDOW = 0x00000000;
    private const uint GA_ROOT = 2;

    private readonly List<IntPtr> _hookHandles = [];
    private WinEventProc? _callback;

    /// <summary>Fires when a new visible top-level window is created.</summary>
    public event Action<IntPtr>? WindowCreated;

    /// <summary>Fires when a window is destroyed.</summary>
    public event Action<IntPtr>? WindowDestroyed;

    /// <summary>Fires when a visible top-level window title changes (HWND + new title).</summary>
    public event Action<IntPtr, string>? WindowTitleChanged;

    /// <summary>Fires when the foreground window changes.</summary>
    public event Action<IntPtr>? ForegroundChanged;

    /// <summary>
    /// Installs the WinEvent hooks. Must be called on the UI thread (requires a message pump).
    /// </summary>
    public void Start()
    {
        if (this._hookHandles.Count > 0)
        {
            return;
        }

        // Hold a reference to prevent the delegate from being garbage collected.
        this._callback = this.OnWinEvent;

        this.InstallHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE);
        this.InstallHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY);
        this.InstallHook(EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE);
        this.InstallHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
    }

    /// <summary>
    /// Removes all installed hooks.
    /// </summary>
    public void Stop()
    {
        foreach (var handle in this._hookHandles)
        {
            UnhookWinEvent(handle);
        }

        this._hookHandles.Clear();
        this._callback = null;
    }

    public void Dispose()
    {
        this.Stop();
    }

    private void InstallHook(uint eventMin, uint eventMax)
    {
        var handle = SetWinEventHook(
            eventMin,
            eventMax,
            IntPtr.Zero,
            this._callback!,
            0,
            0,
            WINEVENT_OUTOFCONTEXT);

        if (handle != IntPtr.Zero)
        {
            this._hookHandles.Add(handle);
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        switch (eventType)
        {
            case EVENT_OBJECT_CREATE:
                if (IsWindowVisible(hwnd) && GetAncestor(hwnd, GA_ROOT) == hwnd)
                {
                    this.WindowCreated?.Invoke(hwnd);
                }
                break;

            case EVENT_OBJECT_DESTROY:
                this.WindowDestroyed?.Invoke(hwnd);
                break;

            case EVENT_OBJECT_NAMECHANGE:
                // Only handle top-level window name changes, not child element changes.
                if (idObject == OBJID_WINDOW && IsWindowVisible(hwnd) && GetAncestor(hwnd, GA_ROOT) == hwnd)
                {
                    var title = GetWindowTitle(hwnd);
                    this.WindowTitleChanged?.Invoke(hwnd, title);
                }
                break;

            case EVENT_SYSTEM_FOREGROUND:
                this.ForegroundChanged?.Invoke(hwnd);
                break;
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(len + 1);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    #region P/Invoke

    private delegate void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(IntPtr hWinEventHook);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
#pragma warning disable SYSLIB1054
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);
#pragma warning restore SYSLIB1054

    #endregion
}
