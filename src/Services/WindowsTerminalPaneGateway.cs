using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
                panes.Add(new WindowsTerminalPaneInfo(name, hwnd, current.ProcessId, isSelected, select));
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

    public IReadOnlyList<(string Name, Action Select)> EnumerateTabs(IntPtr wtHwnd)
    {
        return this.EnumeratePanes(wtHwnd).Panes.Select(pane => (pane.Name, pane.Select)).ToList();
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
        return () =>
        {
            try
            {
                if (tabItem.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? selectionPattern))
                {
                    ((SelectionItemPattern)selectionPattern).Select();
                    return;
                }
            }
            catch (ElementNotAvailableException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                if (tabItem.TryGetCurrentPattern(InvokePattern.Pattern, out object? invokePattern))
                {
                    ((InvokePattern)invokePattern).Invoke();
                }
            }
            catch (ElementNotAvailableException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        };
    }
}
