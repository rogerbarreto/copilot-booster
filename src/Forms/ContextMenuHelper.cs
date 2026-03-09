using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CopilotBooster.Forms;

internal static class ContextMenuHelper
{
    /// <summary>
    /// Clamps a screen-coordinate menu position so the menu stays entirely
    /// within the given working area (typically the screen where the click originated).
    /// </summary>
    internal static Point ClampToScreen(Point screenPoint, Size menuSize, Rectangle workingArea)
    {
        int x = screenPoint.X;
        int y = screenPoint.Y;

        if (x + menuSize.Width > workingArea.Right)
        {
            x = workingArea.Right - menuSize.Width;
        }

        if (y + menuSize.Height > workingArea.Bottom)
        {
            y = workingArea.Bottom - menuSize.Height;
        }

        if (x < workingArea.Left)
        {
            x = workingArea.Left;
        }

        if (y < workingArea.Top)
        {
            y = workingArea.Top;
        }

        return new Point(x, y);
    }

    /// <summary>
    /// Wires the Opened event so the menu and its submenus are always clamped
    /// to the screen that owns <paramref name="parentControl"/>.
    /// Works for both automatic (ContextMenuStrip property) and manual Show calls.
    /// </summary>
    internal static void ConstrainToParentScreen(this ContextMenuStrip menu, Control parentControl)
    {
        menu.Opened += (_, _) =>
        {
            var screen = GetOwnerScreen(parentControl);
            var workingArea = screen.WorkingArea;

            var corrected = ClampToScreen(menu.Location, menu.Size, workingArea);
            if (menu.Location != corrected)
            {
                menu.Location = corrected;
            }

            SetSubmenuDirections(menu, workingArea);
        };

        // Wire submenu position fallback for all existing items
        WireSubmenuConstraints(menu, parentControl);
    }

    /// <summary>
    /// Shows a context menu clamped to the screen where the parent form lives,
    /// preventing it from jumping to an adjacent monitor.
    /// </summary>
    internal static void ShowOnCurrentScreen(this ContextMenuStrip menu, Control control, Point relativePoint)
    {
        var screenPoint = control.PointToScreen(relativePoint);
        var screen = GetOwnerScreen(control);
        var menuSize = menu.PreferredSize;
        var clamped = ClampToScreen(screenPoint, menuSize, screen.WorkingArea);

        var workingArea = screen.WorkingArea;
        void ForcePosition(object? sender, EventArgs args)
        {
            menu.Opened -= ForcePosition;
            var corrected = ClampToScreen(menu.Location, menu.Size, workingArea);
            if (menu.Location != corrected)
            {
                menu.Location = corrected;
            }

            SetSubmenuDirections(menu, workingArea);
        }

        menu.Opened += ForcePosition;
        menu.Show(control, control.PointToClient(clamped));
    }

    private static Screen GetOwnerScreen(Control control)
    {
        var form = control.FindForm();
        return form != null ? Screen.FromControl(form) : Screen.PrimaryScreen!;
    }

    /// <summary>
    /// Sets DropDownDirection on all submenu items so they open to the left
    /// when there's not enough space on the right within the current screen.
    /// </summary>
    private static void SetSubmenuDirections(ToolStrip menu, Rectangle workingArea)
    {
        var spaceOnRight = workingArea.Right - menu.Right;
        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            if (item.HasDropDownItems)
            {
                item.DropDownDirection = spaceOnRight < item.DropDown.PreferredSize.Width
                    ? ToolStripDropDownDirection.Left
                    : ToolStripDropDownDirection.Right;
            }
        }
    }

    /// <summary>
    /// Wires DropDown.Opened on each top-level menu item to force-clamp
    /// submenus that WinForms may still reposition onto another screen.
    /// </summary>
    private static void WireSubmenuConstraints(ContextMenuStrip menu, Control parentControl)
    {
        void WireItem(ToolStripMenuItem item)
        {
            item.DropDown.Opened += (_, _) =>
            {
                var screen = GetOwnerScreen(parentControl);
                var workingArea = screen.WorkingArea;
                var dd = item.DropDown;
                var corrected = ClampToScreen(dd.Location, dd.Size, workingArea);
                if (dd.Location != corrected)
                {
                    dd.Location = corrected;
                }
            };
        }

        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            WireItem(item);
        }

        // Handle items added later (e.g., dynamic menu building)
        menu.ItemAdded += (_, e) =>
        {
            if (e.Item is ToolStripMenuItem menuItem)
            {
                WireItem(menuItem);
            }
        };
    }
}
