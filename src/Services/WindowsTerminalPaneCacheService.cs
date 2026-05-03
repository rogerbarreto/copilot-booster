using System;
using System.Collections.Generic;
using System.Linq;

namespace CopilotBooster.Services;

internal readonly record struct WindowsTerminalPaneCacheKey(long WtWindowHwnd, int CopilotPid);

internal sealed record WindowsTerminalPaneCacheEntry(
    IntPtr WtWindowHwnd,
    int CopilotPid,
    IntPtr PaneHwnd,
    string PaneTitle);

internal sealed class WindowsTerminalPaneCacheService
{
    private readonly Dictionary<WindowsTerminalPaneCacheKey, WindowsTerminalPaneCacheEntry> _entries = [];

    internal bool TryGet(IntPtr wtWindowHwnd, int copilotPid, out WindowsTerminalPaneCacheEntry entry)
    {
        var key = new WindowsTerminalPaneCacheKey(wtWindowHwnd.ToInt64(), copilotPid);
        if (this._entries.TryGetValue(key, out var cached))
        {
            entry = cached;
            return true;
        }

        entry = new WindowsTerminalPaneCacheEntry(IntPtr.Zero, 0, IntPtr.Zero, string.Empty);
        return false;
    }

    internal void Set(IntPtr wtWindowHwnd, int copilotPid, IntPtr paneHwnd, string paneTitle)
    {
        var key = new WindowsTerminalPaneCacheKey(wtWindowHwnd.ToInt64(), copilotPid);
        this._entries[key] = new WindowsTerminalPaneCacheEntry(wtWindowHwnd, copilotPid, paneHwnd, paneTitle);
    }

    internal void InvalidateForTerminalWindow(IntPtr wtWindowHwnd)
    {
        var keyHwnd = wtWindowHwnd.ToInt64();
        foreach (var key in this._entries.Keys.Where(k => k.WtWindowHwnd == keyHwnd).ToList())
        {
            this._entries.Remove(key);
        }
    }

    internal void InvalidatePane(IntPtr paneHwnd)
    {
        foreach (var key in this._entries.Where(kvp => kvp.Value.PaneHwnd == paneHwnd || kvp.Value.WtWindowHwnd == paneHwnd).Select(kvp => kvp.Key).ToList())
        {
            this._entries.Remove(key);
        }
    }

    internal void Revalidate()
    {
        foreach (var key in this._entries
            .Where(kvp => !WindowFocusService.IsWindowAlive(kvp.Value.WtWindowHwnd)
                || (kvp.Value.PaneHwnd != kvp.Value.WtWindowHwnd && !WindowFocusService.IsWindowAlive(kvp.Value.PaneHwnd)))
            .Select(kvp => kvp.Key)
            .ToList())
        {
            this._entries.Remove(key);
        }
    }
}
