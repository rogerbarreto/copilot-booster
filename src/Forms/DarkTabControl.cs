using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace CopilotBooster.Forms;

/// <summary>
/// A TabControl subclass that renders correctly in dark mode by using UserPaint
/// to eliminate the white header background and border that WinForms draws by default.
/// In light mode, falls back to standard owner-draw rendering.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class DarkTabControl : TabControl
{
    private static readonly Color s_darkBackground = Color.FromArgb(0x1E, 0x1E, 0x1E);
    private static readonly Color s_darkTabUnselected = Color.FromArgb(0x2D, 0x2D, 0x2D);
    private static readonly Color s_darkBorder = Color.FromArgb(0x44, 0x44, 0x44);

    internal DarkTabControl()
    {
        this.DrawMode = TabDrawMode.OwnerDrawFixed;

        if (Application.IsDarkModeEnabled)
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
        }
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
