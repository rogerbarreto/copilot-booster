using System.Windows.Forms;

namespace CopilotBooster.Services;

internal interface IConfirmDialog
{
    bool Confirm(string title, string body, string yesLabel, string noLabel);
}

internal sealed class MessageBoxConfirmDialog : IConfirmDialog
{
    private readonly IWin32Window? _owner;

    internal MessageBoxConfirmDialog(IWin32Window? owner = null)
    {
        this._owner = owner;
    }

    public bool Confirm(string title, string body, string yesLabel, string noLabel)
    {
        var message = $"{body}\n\nYes = {yesLabel}\nNo = {noLabel}";
        var result = MessageBox.Show(
            this._owner,
            message,
            title,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        return result == DialogResult.Yes;
    }
}
