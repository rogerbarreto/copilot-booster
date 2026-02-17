using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace CopilotBooster.Forms;

/// <summary>
/// A floating toast notification panel that appears at the bottom of a parent control
/// and auto-dismisses after a configurable duration.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class ToastPanel : Panel
{
    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _dismissTimer;

    private ToastPanel()
    {
        this.Height = 36;
        this.Dock = DockStyle.Bottom;
        this.Visible = false;
        this.Padding = new Padding(12, 0, 12, 0);
        this.BackColor = Application.IsDarkModeEnabled
            ? Color.FromArgb(40, 80, 40)
            : Color.FromArgb(220, 245, 220);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Application.IsDarkModeEnabled ? Color.White : Color.Black,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 9.5f)
        };
        this.Controls.Add(_label);

        _dismissTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _dismissTimer.Tick += (s, e) =>
        {
            _dismissTimer.Stop();
            this.Visible = false;
        };
    }

    /// <summary>
    /// Shows a toast message. Auto-dismisses after <paramref name="durationMs"/> milliseconds.
    /// </summary>
    internal void Show(string message, int durationMs = 3000)
    {
        _dismissTimer.Stop();
        _label.Text = message;
        _dismissTimer.Interval = durationMs;
        this.Visible = true;
        this.BringToFront();
        _dismissTimer.Start();
    }

    /// <summary>
    /// Creates and attaches a <see cref="ToastPanel"/> to the given parent control.
    /// </summary>
    internal static ToastPanel AttachTo(Control parent)
    {
        var toast = new ToastPanel();
        parent.Controls.Add(toast);
        return toast;
    }
}
