using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

internal sealed class WindowsTerminalPaneGateway : IWindowsTerminalPaneGateway
{
    public IReadOnlyList<(string Name, Action Select)> EnumerateTabs(IntPtr wtHwnd)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var tabs = new List<(string Name, Action Select)>();

            var rootElement = AutomationElement.FromHandle(wtHwnd);
            if (rootElement == null)
            {
                return Array.Empty<(string, Action)>();
            }

            var condition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.TabItem);

            var tabItems = rootElement.FindAll(TreeScope.Descendants, condition);
            if (tabItems == null || tabItems.Count == 0)
            {
                return Array.Empty<(string, Action)>();
            }

            foreach (AutomationElement tabItem in tabItems)
            {
                if (sw.ElapsedMilliseconds > 250)
                {
                    break;
                }

                var name = tabItem.Current.Name;
                var select = CreateSelectAction(tabItem);
                tabs.Add((name, select));
            }

            return tabs;
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning(
                "UIA tab enumeration failed for hwnd {Hwnd}: {Error}",
                wtHwnd,
                ex.Message);
            return Array.Empty<(string, Action)>();
        }
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
