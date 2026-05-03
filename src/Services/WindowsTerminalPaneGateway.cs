using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

internal sealed class WindowsTerminalPaneGateway : IWindowsTerminalPaneGateway
{
    public WindowsTerminalPaneEnumeration EnumeratePanes(IntPtr wtHwnd)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var panes = new List<WindowsTerminalPaneInfo>();
            bool isPartial = false;

            var rootElement = AutomationElement.FromHandle(wtHwnd);
            if (rootElement == null)
            {
                return new WindowsTerminalPaneEnumeration(Array.Empty<WindowsTerminalPaneInfo>(), IsPartial: false);
            }

            var condition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.TabItem);

            var tabItems = rootElement.FindAll(TreeScope.Descendants, condition);
            if (tabItems == null || tabItems.Count == 0)
            {
                return new WindowsTerminalPaneEnumeration(Array.Empty<WindowsTerminalPaneInfo>(), IsPartial: false);
            }

            foreach (AutomationElement tabItem in tabItems)
            {
                if (sw.ElapsedMilliseconds > 250)
                {
                    isPartial = true;
                    break;
                }

                var current = tabItem.Current;
                var name = current.Name;
                var hwnd = current.NativeWindowHandle == 0 ? IntPtr.Zero : new IntPtr(current.NativeWindowHandle);
                var select = CreateSelectAction(tabItem);
                var isSelected = IsSelected(tabItem);
                panes.Add(new WindowsTerminalPaneInfo(name, hwnd, current.ProcessId, isSelected, select, GetRuntimeId(tabItem)));
            }

            return new WindowsTerminalPaneEnumeration(panes, isPartial);
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning(
                "UIA tab enumeration failed for hwnd {Hwnd}: {Error}",
                wtHwnd,
                ex.Message);
            return new WindowsTerminalPaneEnumeration(Array.Empty<WindowsTerminalPaneInfo>(), IsPartial: false);
        }
    }

    public bool FocusPane(IntPtr wtHwnd, string paneRuntimeId)
    {
        if (string.IsNullOrWhiteSpace(paneRuntimeId))
        {
            return false;
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var rootElement = AutomationElement.FromHandle(wtHwnd);
            if (rootElement == null)
            {
                return false;
            }

            var condition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.TabItem);
            var tabItems = rootElement.FindAll(TreeScope.Descendants, condition);
            if (tabItems == null || tabItems.Count == 0)
            {
                return false;
            }

            foreach (AutomationElement tabItem in tabItems)
            {
                if (sw.ElapsedMilliseconds > 250)
                {
                    return false;
                }

                if (string.Equals(GetRuntimeId(tabItem), paneRuntimeId, StringComparison.Ordinal))
                {
                    return TryActivateAndVerify(tabItem, wtHwnd, paneRuntimeId);
                }
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogInformation(
                "Windows Terminal pane focus failed for hwnd {Hwnd}, runtime id {RuntimeId}: {Error}",
                wtHwnd,
                paneRuntimeId,
                ex.Message);
        }

        return false;
    }

    public IReadOnlyList<(string Name, Action Select)> EnumerateTabs(IntPtr wtHwnd)
    {
        return this.EnumeratePanes(wtHwnd).Panes.Select(pane => (pane.Name, pane.Select)).ToList();
    }

    private static string? GetRuntimeId(AutomationElement element)
    {
        try
        {
            var runtimeId = element.GetRuntimeId();
            return runtimeId == null || runtimeId.Length == 0
                ? null
                : string.Join(".", runtimeId);
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return null;
    }

    private static bool IsSelected(AutomationElement tabItem)
    {
        try
        {
            if (tabItem.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? selectionPattern))
            {
                return ((SelectionItemPattern)selectionPattern).Current.IsSelected;
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

    private static Action CreateSelectAction(AutomationElement tabItem)
    {
        return () => _ = TryActivateAndVerify(tabItem, IntPtr.Zero, GetRuntimeId(tabItem) ?? string.Empty);
    }

    private static bool TryActivateAndVerify(AutomationElement tabItem, IntPtr wtHwnd, string paneRuntimeId)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (TrySelectionItemSelect(tabItem) && WaitUntilSelected(tabItem, 200))
            {
                return true;
            }

            if (TryInvoke(tabItem) && WaitUntilSelected(tabItem, 200))
            {
                return true;
            }
        }

        if (IsSelected(tabItem))
        {
            return true;
        }

        Program.Logger.LogWarning(
            "Windows Terminal pane activation did not select hwnd {Hwnd}, runtime id {RuntimeId}",
            wtHwnd,
            paneRuntimeId);
        return false;
    }

    private static bool TrySelectionItemSelect(AutomationElement tabItem)
    {
        try
        {
            if (tabItem.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? selectionPattern))
            {
                ((SelectionItemPattern)selectionPattern).Select();
                return true;
            }
        }
        catch (ElementNotAvailableException ex)
        {
            Program.Logger.LogInformation("Windows Terminal SelectionItemPattern.Select failed: {Error}", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            Program.Logger.LogInformation("Windows Terminal SelectionItemPattern.Select failed: {Error}", ex.Message);
        }

        return false;
    }

    private static bool TryInvoke(AutomationElement tabItem)
    {
        try
        {
            if (tabItem.TryGetCurrentPattern(InvokePattern.Pattern, out object? invokePattern))
            {
                ((InvokePattern)invokePattern).Invoke();
                return true;
            }
        }
        catch (ElementNotAvailableException ex)
        {
            Program.Logger.LogInformation("Windows Terminal InvokePattern.Invoke failed: {Error}", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            Program.Logger.LogInformation("Windows Terminal InvokePattern.Invoke failed: {Error}", ex.Message);
        }

        return false;
    }

    private static bool WaitUntilSelected(AutomationElement tabItem, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        do
        {
            if (IsSelected(tabItem))
            {
                return true;
            }

            Thread.Sleep(25);
        } while (Environment.TickCount64 < deadline);

        return false;
    }
}
