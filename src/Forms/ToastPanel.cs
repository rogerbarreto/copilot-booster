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
    private readonly Timer _dismissTimer;

    private static readonly Color s_successBackDark = Color.FromArgb(40, 80, 40);
    private static readonly Color s_successBackLight = Color.FromArgb(220, 245, 220);
    private static readonly Color s_warningBackDark = Color.FromArgb(100, 80, 20);
    private static readonly Color s_warningBackLight = Color.FromArgb(255, 248, 200);

    private ToastPanel()
    {
        this.Height = 36;
        this.Dock = DockStyle.Bottom;
        this.Visible = false;
        this.Padding = new Padding(12, 0, 12, 0);
        this.BackColor = Application.IsDarkModeEnabled ? s_successBackDark : s_successBackLight;

        this._label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Application.IsDarkModeEnabled ? Color.White : Color.Black,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 9.5f)
        };
        this.Controls.Add(this._label);

        this._dismissTimer = new Timer { Interval = 3000 };
        this._dismissTimer.Tick += (s, e) =>
        {
            this._dismissTimer.Stop();
            this.Visible = false;
        };
    }

    /// <summary>
    /// Shows a toast message with success styling. Auto-dismisses after <paramref name="durationMs"/> milliseconds.
    /// </summary>
    internal void Show(string message, int durationMs = 3000)
    {
        this.ShowInternal(message, durationMs, isWarning: false);
    }

    /// <summary>
    /// Shows a warning toast message with yellow styling. Auto-dismisses after <paramref name="durationMs"/> milliseconds.
    /// </summary>
    internal void ShowWarning(string message, int durationMs = 5000)
    {
        this.ShowInternal(message, durationMs, isWarning: true);
    }

    private void ShowInternal(string message, int durationMs, bool isWarning)
    {
        this._dismissTimer.Stop();
        this._label.Text = message;
        this._dismissTimer.Interval = durationMs;
        this.BackColor = isWarning
            ? (Application.IsDarkModeEnabled ? s_warningBackDark : s_warningBackLight)
            : (Application.IsDarkModeEnabled ? s_successBackDark : s_successBackLight);
        this.Visible = true;
        this.BringToFront();
        this._dismissTimer.Start();
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
