using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace CopilotBooster.Forms;

/// <summary>
/// A TabControl subclass that renders correctly in dark mode by using UserPaint
/// to eliminate the white header background and border that WinForms draws by default.
/// In light mode, falls back to standard owner-draw rendering.
/// Supports drag-and-drop tab reordering (excludes the "+" tab).
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class DarkTabControl : TabControl
{
    private static readonly Color s_darkBackground = Color.FromArgb(0x1E, 0x1E, 0x1E);
    private static readonly Color s_darkTabUnselected = Color.FromArgb(0x2D, 0x2D, 0x2D);
    private static readonly Color s_darkBorder = Color.FromArgb(0x44, 0x44, 0x44);

    private int _dragTabIndex = -1;
    private int _dropTargetIndex = -1;
    private bool _isDragging;
    private Point _dragStartPoint;

    /// <summary>
    /// Raised after the user reorders tabs via drag-and-drop.
    /// The event args contain the old and new index.
    /// </summary>
    internal event EventHandler<TabReorderedEventArgs>? TabReordered;

    internal DarkTabControl()
    {
        this.DrawMode = TabDrawMode.OwnerDrawFixed;
        this.AllowDrop = true;

        if (Application.IsDarkModeEnabled)
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
        }
    }

    private static bool IsAddTab(TabPage page) =>
        page.Tag == null && page.Text.Trim() == "+";

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        int index = this.GetTabIndexAtPoint(e.Location);
        if (index >= 0 && !IsAddTab(this.TabPages[index]))
        {
            this._dragTabIndex = index;
            this._dragStartPoint = e.Location;
            this._isDragging = false;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (this._dragTabIndex < 0 || e.Button != MouseButtons.Left)
        {
            return;
        }

        // Start dragging after a small threshold to avoid accidental drags
        if (!this._isDragging)
        {
            int dx = Math.Abs(e.X - this._dragStartPoint.X);
            if (dx < 8)
            {
                return;
            }

            this._isDragging = true;
            this.Cursor = Cursors.Hand;
        }

        int target = this.GetTabIndexAtPoint(e.Location);
        if (target >= 0 && !IsAddTab(this.TabPages[target]) && target != this._dragTabIndex)
        {
            if (target != this._dropTargetIndex)
            {
                this._dropTargetIndex = target;
                this.Invalidate();
            }
        }
        else if (this._dropTargetIndex != -1)
        {
            this._dropTargetIndex = -1;
            this.Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (this._isDragging && this._dragTabIndex >= 0 && this._dropTargetIndex >= 0
            && this._dragTabIndex != this._dropTargetIndex)
        {
            int oldIndex = this._dragTabIndex;
            int newIndex = this._dropTargetIndex;
            this.TabReordered?.Invoke(this, new TabReorderedEventArgs(oldIndex, newIndex));
        }

        this._dragTabIndex = -1;
        this._dropTargetIndex = -1;
        this._isDragging = false;
        this.Cursor = Cursors.Default;
        this.Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (this._isDragging)
        {
            this._dragTabIndex = -1;
            this._dropTargetIndex = -1;
            this._isDragging = false;
            this.Cursor = Cursors.Default;
            this.Invalidate();
        }
    }

    internal int GetTabIndexAtPoint(Point pt)
    {
        for (int i = 0; i < this.TabCount; i++)
        {
            if (this.GetTabRect(i).Contains(pt))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Application.IsDarkModeEnabled)
        {
            base.OnPaint(e);
            return;
        }

        var g = e.Graphics;

        // Fill entire control background
        using (var bgBrush = new SolidBrush(s_darkBackground))
        {
            g.FillRectangle(bgBrush, this.ClientRectangle);
        }

        if (this.TabCount == 0)
        {
            return;
        }

        // Content area border — use the native display rectangle which accounts for tab height
        var displayRect = this.DisplayRectangle;
        var contentRect = Rectangle.Inflate(displayRect, 1, 1);
        using (var borderPen = new Pen(s_darkBorder))
        {
            g.DrawRectangle(borderPen, contentRect);
        }

        // Draw each tab using native bounds
        for (int i = 0; i < this.TabCount; i++)
        {
            var bounds = this.GetTabRect(i);
            bool isAddTab = this.TabPages[i].Tag == null && this.TabPages[i].Text.Trim() == "+";

            // Shrink the "+" tab visually
            if (isAddTab)
            {
                int shrink = (bounds.Width - 28) / 2;
                if (shrink > 0)
                {
                    bounds = new Rectangle(bounds.X + shrink, bounds.Y, 28, bounds.Height);
                }
            }

            this.DrawTabItem(g, i, bounds);
        }

        // Draw drop indicator line during drag
        if (this._isDragging && this._dropTargetIndex >= 0 && this._dropTargetIndex < this.TabCount)
        {
            var targetBounds = this.GetTabRect(this._dropTargetIndex);
            int indicatorX = this._dropTargetIndex > this._dragTabIndex
                ? targetBounds.Right
                : targetBounds.Left;

            using var pen = new Pen(Color.DodgerBlue, 2);
            g.DrawLine(pen, indicatorX, targetBounds.Top + 2, indicatorX, targetBounds.Bottom - 2);
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (Application.IsDarkModeEnabled)
        {
            return;
        }

        // Light mode owner-draw
        bool selected = e.Index == this.SelectedIndex;
        var back = selected ? SystemColors.Window : Color.FromArgb(220, 220, 220);
        var fore = SystemColors.ControlText;
        using var brush = new SolidBrush(back);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var text = this.TabPages[e.Index].Text;
        TextRenderer.DrawText(e.Graphics, text, this.Font, e.Bounds, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // Draw drop indicator in light mode
        if (this._isDragging && this._dropTargetIndex == e.Index)
        {
            int indicatorX = this._dropTargetIndex > this._dragTabIndex
                ? e.Bounds.Right
                : e.Bounds.Left;
            using var pen = new Pen(Color.DodgerBlue, 2);
            e.Graphics.DrawLine(pen, indicatorX, e.Bounds.Top + 2, indicatorX, e.Bounds.Bottom - 2);
        }
    }

    private void DrawTabItem(Graphics g, int index, Rectangle bounds)
    {
        bool selected = index == this.SelectedIndex;

        var back = selected ? s_darkBackground : s_darkTabUnselected;
        var fore = selected ? Color.White : Color.LightGray;

        using (var brush = new SolidBrush(back))
        {
            g.FillRectangle(brush, bounds);
        }

        using (var pen = new Pen(s_darkBorder))
        {
            g.DrawRectangle(pen, bounds);
        }

        // Erase bottom border on selected tab to merge with content area
        if (selected)
        {
            using var brush = new SolidBrush(s_darkBackground);
            g.FillRectangle(brush, bounds.Left + 1, bounds.Bottom, bounds.Width - 1, 1);
        }

        var text = this.TabPages[index].Text;
        TextRenderer.DrawText(g, text, this.Font, bounds, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>
/// Event arguments for tab reorder events.
/// </summary>
internal sealed class TabReorderedEventArgs : EventArgs
{
    internal int OldIndex { get; }
    internal int NewIndex { get; }

    internal TabReorderedEventArgs(int oldIndex, int newIndex)
    {
        this.OldIndex = oldIndex;
        this.NewIndex = newIndex;
    }
}
