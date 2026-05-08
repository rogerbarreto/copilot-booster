using System.Windows.Forms;

namespace CopilotBooster.Services;

internal interface IMessageBox
{
    void Show(string title, string body);
}

internal sealed class MessageBoxAdapter : IMessageBox
{
    private readonly IWin32Window? _owner;

    internal MessageBoxAdapter(IWin32Window? owner = null)
    {
        this._owner = owner;
    }

    public void Show(string title, string body)
    {
        MessageBox.Show(
            this._owner,
            body,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
