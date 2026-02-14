using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms;
using CopilotApp.Services;

namespace CopilotApp.Forms;

/// <summary>
/// Manages the New Session tab's CWD list and column sorting.
/// </summary>
[ExcludeFromCodeCoverage]
internal class NewSessionTabBuilder
{
    private static readonly string[] s_cwdColumnBaseNames = { "Directory", "# Sessions created", "Git" };

    /// <summary>
    /// Gets the column sorter to assign as <see cref="ListView.ListViewItemSorter"/>.
    /// </summary>
    internal ListViewColumnSorter Sorter { get; } = new(column: 1, order: SortOrder.Descending);

    /// <summary>
    /// Populates the CWD list view from session data.
    /// </summary>
    internal void Populate(ListView cwdListView, Dictionary<string, bool> cwdGitStatus, SessionData data)
    {
        cwdListView.Items.Clear();
        cwdGitStatus.Clear();

        foreach (var kv in data.CwdSessionCounts)
        {
            var cwd = kv.Key;
            var isGit = data.CwdGitStatus.TryGetValue(cwd, out bool g) && g;
            cwdGitStatus[cwd] = isGit;

            var item = new ListViewItem(cwd) { Tag = cwd };
            item.SubItems.Add(kv.Value.ToString());
            item.SubItems.Add(isGit ? "Yes" : "");
            cwdListView.Items.Add(item);
        }

        cwdListView.Sort();

        if (cwdListView.Items.Count > 0)
        {
            cwdListView.Items[0].Selected = true;
        }
    }

    /// <summary>
    /// Handles column click events to toggle sort order and update column headers.
    /// </summary>
    internal void OnColumnClick(ListView cwdListView, ColumnClickEventArgs e)
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
            this.Sorter.Order = e.Column == 1 ? SortOrder.Descending : SortOrder.Ascending;
        }

        for (int i = 0; i < s_cwdColumnBaseNames.Length; i++)
        {
            cwdListView.Columns[i].Text = i == this.Sorter.SortColumn
                ? s_cwdColumnBaseNames[i] + (this.Sorter.Order == SortOrder.Ascending ? " ▲" : " ▼")
                : s_cwdColumnBaseNames[i];
        }

        cwdListView.Sort();
    }
}
