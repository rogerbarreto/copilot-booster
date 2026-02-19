using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Builds and owns the "New Session" tab UI controls.
/// Pure visuals — no service calls, no file I/O.
/// </summary>
[ExcludeFromCodeCoverage]
internal class NewSessionVisuals
{
    private static readonly string[] s_cwdColumnBaseNames = { "", "Directory", "# Sessions created", "Git" };

    internal ListView CwdListView = null!;
    internal Label LoadingOverlay = null!;

    /// <summary>
    /// Gets the column sorter to assign as <see cref="ListView.ListViewItemSorter"/>.
    /// </summary>
    internal ListViewColumnSorter Sorter { get; } = new(column: 2, order: SortOrder.Descending);

    // Context menu events — arg is always the selected CWD path.
    internal event Func<string, Task>? OnNewSession;
    internal event Func<string, Task>? OnNewSessionWorkspace;
    internal event Action<string>? OnOpenExplorer;
    internal event Action<string>? OnOpenTerminal;
    internal event Func<Task>? OnAddDirectory;
    internal event Func<string, Task>? OnRemoveDirectory;
    internal event Func<string, Task>? OnDoubleClicked;

    internal async void TriggerAddDirectoryAsync()
    {
        if (this.OnAddDirectory != null)
        {
            await this.OnAddDirectory.Invoke().ConfigureAwait(true);
        }
    }

    internal async void TriggerNewSessionAsync(string selectedCwd)
    {
        if (this.OnNewSession != null)
        {
            await this.OnNewSession.Invoke(selectedCwd).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Callback to determine context menu visibility state.
    /// Returns (isGit, isPinned, sessionCount).
    /// </summary>
    internal Func<string, Dictionary<string, bool>, (bool isGit, bool isPinned, int sessionCount)>? GetCwdMenuInfo;

    private readonly Dictionary<string, bool> _cwdGitStatus = new(StringComparer.OrdinalIgnoreCase);

    internal NewSessionVisuals(Control parentControl)
    {
        this.BuildCwdListView();
        this.BuildCwdContextMenu();

        this.LoadingOverlay = new Label
        {
            Text = "Loading directories...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14f, FontStyle.Regular)
        };

        parentControl.Controls.Add(this.LoadingOverlay);
        this.LoadingOverlay.BringToFront();
        parentControl.Controls.Add(this.CwdListView);
    }

    /// <summary>
    /// Populates the CWD list view from session data.
    /// </summary>
    internal void Populate(SessionData data)
    {
        this.CwdListView.Items.Clear();
        this._cwdGitStatus.Clear();

        foreach (var kv in data.CwdSessionCounts)
        {
            var cwd = kv.Key;
            var isGit = data.CwdGitStatus.TryGetValue(cwd, out bool g) && g;
            this._cwdGitStatus[cwd] = isGit;

            var item = new ListViewItem("▶") { Tag = cwd };
            item.SubItems.Add(cwd);
            item.SubItems.Add(kv.Value.ToString());
            item.SubItems.Add(isGit ? "Yes" : "");
            this.CwdListView.Items.Add(item);
        }

        this.CwdListView.Sort();

        if (this.CwdListView.Items.Count > 0)
        {
            this.CwdListView.Items[0].Selected = true;
        }
    }

    private void BuildCwdListView()
    {
        this.CwdListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = !Application.IsDarkModeEnabled
        };
        this.CwdListView.Columns.Add("", 30, HorizontalAlignment.Center);
        this.CwdListView.Columns.Add("Directory", 330);
        this.CwdListView.Columns.Add("# Sessions created ▼", 120, HorizontalAlignment.Center);
        this.CwdListView.Columns.Add("Git", 50, HorizontalAlignment.Center);
        this.CwdListView.ListViewItemSorter = this.Sorter;
        this.CwdListView.ColumnClick += (s, e) =>
        {
            if (e.Column == 0)
            {
                return; // Don't sort by play column
            }

            this.OnColumnClick(e);
        };
        SettingsVisuals.ApplyThemedSelection(this.CwdListView);

        // Right-click row selection
        this.CwdListView.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                var item = this.CwdListView.GetItemAt(e.X, e.Y);
                if (item != null)
                {
                    item.Selected = true;
                }
            }
        };

        // Click on play column shows context menu
        this.CwdListView.MouseClick += (s, e) =>
        {
            var hit = this.CwdListView.HitTest(e.X, e.Y);
            if (hit.Item != null && hit.SubItem != null && hit.Item.SubItems.IndexOf(hit.SubItem) == 0)
            {
                hit.Item.Selected = true;
                this.CwdListView.ContextMenuStrip?.Show(this.CwdListView, e.Location);
            }
        };

        // Double-click triggers New Copilot Session
        this.CwdListView.DoubleClick += async (s, e) =>
        {
            if (this.CwdListView.SelectedItems.Count > 0
                && this.CwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                if (this.OnDoubleClicked != null)
                {
                    await this.OnDoubleClicked.Invoke(selectedCwd).ConfigureAwait(true);
                }
            }
        };
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    private static Bitmap? TryExtractIcon(string filePath, int index)
    {
        try
        {
            var hIcon = ExtractIcon(IntPtr.Zero, filePath, index);
            if (hIcon != IntPtr.Zero)
            {
                using var icon = Icon.FromHandle(hIcon);
                return new Bitmap(icon.ToBitmap(), 16, 16);
            }
        }
        catch { /* ignore icon extraction failures */ }

        return null;
    }

    private void BuildCwdContextMenu()
    {
        var cwdContextMenu = new ContextMenuStrip();
        var shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
        var imageres = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "imageres.dll");
        var appIcon = Program.AppIcon != null ? new Bitmap(Program.AppIcon.ToBitmap(), 16, 16) : null;

        var menuNewSession = new ToolStripMenuItem("New Copilot Session") { Image = appIcon };
        menuNewSession.Click += async (s, e) =>
        {
            if (this.CwdListView.SelectedItems.Count > 0
                && this.CwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                if (this.OnNewSession != null)
                {
                    await this.OnNewSession.Invoke(selectedCwd).ConfigureAwait(true);
                }
            }
        };
        cwdContextMenu.Items.Add(menuNewSession);

        var menuNewSessionWorkspace = new ToolStripMenuItem("New Copilot Session Workspace") { Image = appIcon?.Clone() as Image };
        menuNewSessionWorkspace.Click += async (s, e) =>
        {
            if (this.CwdListView.SelectedItems.Count > 0
                && this.CwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                if (this.OnNewSessionWorkspace != null)
                {
                    await this.OnNewSessionWorkspace.Invoke(selectedCwd).ConfigureAwait(true);
                }
            }
        };
        cwdContextMenu.Items.Add(menuNewSessionWorkspace);

        cwdContextMenu.Items.Add(new ToolStripSeparator());

        var menuOpenExplorer = new ToolStripMenuItem("Open in Explorer") { Image = TryExtractIcon(shell32, 3) };
        menuOpenExplorer.Click += (s, e) =>
        {
            if (this.CwdListView.SelectedItems.Count > 0
                && this.CwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                this.OnOpenExplorer?.Invoke(selectedCwd);
            }
        };
        cwdContextMenu.Items.Add(menuOpenExplorer);

        var menuOpenTerminalCwd = new ToolStripMenuItem("Open Terminal") { Image = TryExtractIcon(imageres, 264) };
        menuOpenTerminalCwd.Click += (s, e) =>
        {
            if (this.CwdListView.SelectedItems.Count > 0
                && this.CwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                this.OnOpenTerminal?.Invoke(selectedCwd);
            }
        };
        cwdContextMenu.Items.Add(menuOpenTerminalCwd);

        cwdContextMenu.Items.Add(new ToolStripSeparator());

        var menuRemoveDirectory = new ToolStripMenuItem("Remove Directory") { Image = TryExtractIcon(shell32, 131) };
        menuRemoveDirectory.Click += async (s, e) =>
        {
            if (this.CwdListView.SelectedItems.Count > 0
                && this.CwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                if (this.OnRemoveDirectory != null)
                {
                    await this.OnRemoveDirectory.Invoke(selectedCwd).ConfigureAwait(true);
                }
            }
        };
        cwdContextMenu.Items.Add(menuRemoveDirectory);

        cwdContextMenu.Opening += (s, e) =>
        {
            bool hasSelection = this.CwdListView.SelectedItems.Count > 0;
            bool isGit = false;
            bool isPinned = false;
            int sessionCount = 0;

            if (hasSelection && this.CwdListView.SelectedItems[0].Tag is string path
                && this.GetCwdMenuInfo != null)
            {
                (isGit, isPinned, sessionCount) = this.GetCwdMenuInfo(path, this._cwdGitStatus);
            }

            menuNewSession.Visible = hasSelection;
            menuNewSessionWorkspace.Visible = hasSelection && isGit;
            menuOpenExplorer.Visible = hasSelection;
            menuOpenTerminalCwd.Visible = hasSelection;
            menuRemoveDirectory.Visible = hasSelection && isPinned && sessionCount == 0;
        };

        this.CwdListView.ContextMenuStrip = cwdContextMenu;
    }

    /// <summary>
    /// Handles column click events to toggle sort order and update column headers.
    /// </summary>
    private void OnColumnClick(ColumnClickEventArgs e)
    {
        if (e.Column == this.Sorter.SortColumn)
        {
            this.Sorter.Order = this.Sorter.Order == SortOrder.Ascending
                ? SortOrder.Descending
                : SortOrder.Ascending;
        }
        else
        {
            this.Sorter.SortColumn = e.Column;
            // Session count defaults to descending; others to ascending
            this.Sorter.Order = e.Column == 2 ? SortOrder.Descending : SortOrder.Ascending;
        }

        for (int i = 0; i < s_cwdColumnBaseNames.Length; i++)
        {
            this.CwdListView.Columns[i].Text = i == this.Sorter.SortColumn
                ? s_cwdColumnBaseNames[i] + (this.Sorter.Order == SortOrder.Ascending ? " ▲" : " ▼")
                : s_cwdColumnBaseNames[i];
        }

        this.CwdListView.Sort();
    }
}
