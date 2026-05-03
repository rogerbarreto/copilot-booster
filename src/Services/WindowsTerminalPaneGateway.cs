using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

internal sealed class WindowsTerminalPaneGateway : IWindowsTerminalPaneGateway
{
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_TAB = 0x09;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

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
                panes.Add(new WindowsTerminalPaneInfo(
                    name,
                    hwnd,
                    current.ProcessId,
                    isSelected,
                    select,
                    GetRuntimeId(tabItem),
                    GetPaneRootProcessId(tabItem, current.ProcessId)));
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
            var tabItems = EnumerateTabItems(wtHwnd, 250);
            if (tabItems.Count == 0)
            {
                RuntimeDiagnosticLog.Write("WT focus dispatch hwnd={0} target={1} tabs=none", wtHwnd, paneRuntimeId);
                return false;
            }

            RuntimeDiagnosticLog.Write(
                "WT focus dispatch hwnd={0} target={1} tabs=[{2}]",
                wtHwnd,
                paneRuntimeId,
                FormatTabDiagnostics(tabItems));

            var targetIndex = tabItems.FindIndex(tab => string.Equals(GetRuntimeId(tab), paneRuntimeId, StringComparison.Ordinal));
            if (targetIndex < 0)
            {
                RuntimeDiagnosticLog.Write("WT focus dispatch target runtime not found hwnd={0} target={1}", wtHwnd, paneRuntimeId);
                return false;
            }

            if (TryActivateAndVerify(tabItems[targetIndex], wtHwnd, paneRuntimeId))
            {
                RuntimeDiagnosticLog.Write(
                    "WT focus dispatch selected target via UIA hwnd={0} target={1} postSelected={2}",
                    wtHwnd,
                    paneRuntimeId,
                    GetSelectedRuntimeId(wtHwnd) ?? "none");
                return true;
            }

            var currentIndex = tabItems.FindIndex(IsSelected);
            if (TryFocusPaneWithKeyboardFallback(wtHwnd, paneRuntimeId, currentIndex, targetIndex, tabItems.Count))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogInformation(
                "Windows Terminal pane focus failed for hwnd {Hwnd}, runtime id {RuntimeId}: {Error}",
                wtHwnd,
                paneRuntimeId,
                ex.Message);
            RuntimeDiagnosticLog.Write("WT focus dispatch failed hwnd={0} target={1} error={2}", wtHwnd, paneRuntimeId, ex.Message);
        }

        return false;
    }

    public IReadOnlyList<(string Name, Action Select)> EnumerateTabs(IntPtr wtHwnd)
    {
        return this.EnumeratePanes(wtHwnd).Panes.Select(pane => (pane.Name, pane.Select)).ToList();
    }

    private static List<AutomationElement> EnumerateTabItems(IntPtr wtHwnd, int timeoutMs)
    {
        var rootElement = AutomationElement.FromHandle(wtHwnd);
        if (rootElement == null)
        {
            return [];
        }

        var condition = new PropertyCondition(
            AutomationElement.ControlTypeProperty,
            ControlType.TabItem);
        var rawTabItems = rootElement.FindAll(TreeScope.Descendants, condition);
        var tabItems = new List<AutomationElement>();
        if (rawTabItems == null || rawTabItems.Count == 0)
        {
            return tabItems;
        }

        var sw = Stopwatch.StartNew();
        foreach (AutomationElement tabItem in rawTabItems)
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                break;
            }

            tabItems.Add(tabItem);
        }

        return tabItems;
    }

    private static string FormatTabDiagnostics(IReadOnlyList<AutomationElement> tabItems)
    {
        return string.Join(
            "; ",
            tabItems.Select((tab, index) =>
            {
                try
                {
                    return $"#{index}:name='{tab.Current.Name}',runtime='{GetRuntimeId(tab) ?? "null"}',selected={IsSelected(tab)}";
                }
                catch (ElementNotAvailableException)
                {
                    return $"#{index}:<not-available>";
                }
                catch (InvalidOperationException)
                {
                    return $"#{index}:<invalid>";
                }
            }));
    }

    private static string? GetSelectedRuntimeId(IntPtr wtHwnd)
    {
        foreach (var tab in EnumerateTabItems(wtHwnd, 250))
        {
            if (IsSelected(tab))
            {
                return GetRuntimeId(tab);
            }
        }

        return null;
    }

    public static string ReadWindowText(IntPtr wtHwnd, int maxLength = 20_000)
    {
        try
        {
            var rootElement = AutomationElement.FromHandle(wtHwnd);
            if (rootElement == null)
            {
                return string.Empty;
            }

            var sw = Stopwatch.StartNew();
            var parts = new List<string>();
            int remainingLength = maxLength;
            CollectAutomationText(rootElement, parts, sw, ref remainingLength);
            return string.Join(Environment.NewLine, parts.Distinct(StringComparer.Ordinal));
        }
        catch (Exception ex)
        {
            Program.Logger.LogInformation("Windows Terminal text read failed for hwnd {Hwnd}: {Error}", wtHwnd, ex.Message);
            return string.Empty;
        }
    }

    private static void CollectAutomationText(
        AutomationElement element,
        List<string> parts,
        Stopwatch sw,
        ref int remainingLength)
    {
        if (remainingLength <= 0 || sw.ElapsedMilliseconds > 500)
        {
            return;
        }

        AddElementText(element, parts, ref remainingLength);

        AutomationElement? child;
        try
        {
            child = TreeWalker.RawViewWalker.GetFirstChild(element);
        }
        catch (ElementNotAvailableException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        while (child != null && remainingLength > 0 && sw.ElapsedMilliseconds <= 500)
        {
            CollectAutomationText(child, parts, sw, ref remainingLength);
            try
            {
                child = TreeWalker.RawViewWalker.GetNextSibling(child);
            }
            catch (ElementNotAvailableException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }
    }

    private static void AddElementText(AutomationElement element, List<string> parts, ref int remainingLength)
    {
        try
        {
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPattern))
            {
                AddText(((TextPattern)textPattern).DocumentRange.GetText(remainingLength), parts, ref remainingLength);
            }

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
            {
                AddText(((ValuePattern)valuePattern).Current.Value, parts, ref remainingLength);
            }

            AddText(element.Current.Name, parts, ref remainingLength);
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void AddText(string? text, List<string> parts, ref int remainingLength)
    {
        if (string.IsNullOrWhiteSpace(text) || remainingLength <= 0)
        {
            return;
        }

        if (text.Length > remainingLength)
        {
            text = text[..remainingLength];
        }

        parts.Add(text);
        remainingLength -= text.Length;
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

    private static int? GetPaneRootProcessId(AutomationElement tabItem, int wtProcessId)
    {
        var direct = TryGetNativeWindowProcessId(tabItem, wtProcessId);
        if (direct.HasValue)
        {
            return direct;
        }

        try
        {
            var descendants = tabItem.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement descendant in descendants)
            {
                var pid = TryGetNativeWindowProcessId(descendant, wtProcessId);
                if (pid.HasValue)
                {
                    return pid;
                }
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return null;
    }

    private static int? TryGetNativeWindowProcessId(AutomationElement element, int wtProcessId)
    {
        try
        {
            var hwnd = element.Current.NativeWindowHandle == 0 ? IntPtr.Zero : new IntPtr(element.Current.NativeWindowHandle);
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            var pid = WindowFocusService.GetWindowProcessId(hwnd);
            return pid > 0 && pid != wtProcessId ? pid : null;
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

    private static bool TryFocusPaneWithKeyboardFallback(
        IntPtr wtHwnd,
        string paneRuntimeId,
        int currentIndex,
        int targetIndex,
        int tabCount)
    {
        if (currentIndex < 0 || targetIndex < 0 || tabCount <= 0)
        {
            RuntimeDiagnosticLog.Write(
                "WT Ctrl+Tab fallback unavailable hwnd={0} target={1} currentIndex={2} targetIndex={3} tabCount={4}",
                wtHwnd,
                paneRuntimeId,
                currentIndex,
                targetIndex,
                tabCount);
            return false;
        }

        var forwardSteps = (targetIndex - currentIndex + tabCount) % tabCount;
        var backwardSteps = (currentIndex - targetIndex + tabCount) % tabCount;
        var backwards = backwardSteps < forwardSteps;
        var steps = backwards ? backwardSteps : forwardSteps;
        RuntimeDiagnosticLog.Write(
            "WT Ctrl+Tab fallback hwnd={0} target={1} currentIndex={2} targetIndex={3} steps={4} backwards={5}",
            wtHwnd,
            paneRuntimeId,
            currentIndex,
            targetIndex,
            steps,
            backwards);

        if (steps == 0)
        {
            return string.Equals(GetSelectedRuntimeId(wtHwnd), paneRuntimeId, StringComparison.Ordinal);
        }

        _ = WindowFocusService.TryFocusWindowHandle(wtHwnd);
        for (var i = 0; i < steps; i++)
        {
            if (!SendCtrlTab(backwards))
            {
                return false;
            }

            Thread.Sleep(80);
        }

        var selectedRuntimeId = WaitForSelectedRuntimeId(wtHwnd, paneRuntimeId, 750);
        RuntimeDiagnosticLog.Write(
            "WT Ctrl+Tab fallback post-select hwnd={0} target={1} selected={2}",
            wtHwnd,
            paneRuntimeId,
            selectedRuntimeId ?? "none");
        return string.Equals(selectedRuntimeId, paneRuntimeId, StringComparison.Ordinal);
    }

    private static string? WaitForSelectedRuntimeId(IntPtr wtHwnd, string expectedRuntimeId, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        string? selectedRuntimeId;
        do
        {
            selectedRuntimeId = GetSelectedRuntimeId(wtHwnd);
            if (string.Equals(selectedRuntimeId, expectedRuntimeId, StringComparison.Ordinal))
            {
                return selectedRuntimeId;
            }

            Thread.Sleep(50);
        } while (Environment.TickCount64 < deadline);

        return selectedRuntimeId;
    }

    private static bool SendCtrlTab(bool backwards)
    {
        var inputs = backwards
            ? new[]
            {
                KeyDown(VK_CONTROL),
                KeyDown(VK_SHIFT),
                KeyDown(VK_TAB),
                KeyUp(VK_TAB),
                KeyUp(VK_SHIFT),
                KeyUp(VK_CONTROL)
            }
            :
            [
                KeyDown(VK_CONTROL),
                KeyDown(VK_TAB),
                KeyUp(VK_TAB),
                KeyUp(VK_CONTROL)
            ];

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            RuntimeDiagnosticLog.Write("WT Ctrl+Tab SendInput sent {0}/{1}; lastError={2}", sent, inputs.Length, Marshal.GetLastWin32Error());
            return false;
        }

        return true;
    }

    private static INPUT KeyDown(ushort virtualKey)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = virtualKey }
        };
    }

    private static INPUT KeyUp(ushort virtualKey)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = KEYEVENTF_KEYUP }
        };
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
