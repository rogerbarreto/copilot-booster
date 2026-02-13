using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CopilotApp.Services;

internal class ListViewColumnSorter : IComparer
{
    public int SortColumn { get; set; }
    public SortOrder Order { get; set; }

    private readonly HashSet<int> _numericColumns;

    public ListViewColumnSorter(int column = 1, SortOrder order = SortOrder.Descending, HashSet<int>? numericColumns = null)
    {
        this.SortColumn = column;
        this.Order = order;
        this._numericColumns = numericColumns ?? new HashSet<int> { 1 };
    }

    public int Compare(object? x, object? y)
    {
        if (x is not ListViewItem itemX || y is not ListViewItem itemY)
        {
            return 0;
        }

        string textX = this.SortColumn < itemX.SubItems.Count ? itemX.SubItems[this.SortColumn].Text : "";
        string textY = this.SortColumn < itemY.SubItems.Count ? itemY.SubItems[this.SortColumn].Text : "";

        int result;
        if (this._numericColumns.Contains(this.SortColumn))
        {
            int.TryParse(textX, out int numX);
            int.TryParse(textY, out int numY);
            result = numX.CompareTo(numY);
        }
        else
        {
            result = string.Compare(textX, textY, StringComparison.OrdinalIgnoreCase);
        }

        return this.Order == SortOrder.Descending ? -result : result;
    }
}
