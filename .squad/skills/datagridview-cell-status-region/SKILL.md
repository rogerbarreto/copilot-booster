---
name: "datagridview-cell-status-region"
description: "Two-region DataGridView cell painting, tooltip routing, and click routing"
domain: "winforms-ui"
confidence: "high"
source: "issue-20"
---

## Pattern

Reserve a small status region inside a painted `DataGridView` cell, then route painting, tooltip, cursor, and clicks before existing cell behavior.

## Shape

```csharp
internal static Rectangle GetStatusIconRegion(Rectangle cellBounds)
{
    var size = Math.Min(16, Math.Min(cellBounds.Width, cellBounds.Height));
    return new Rectangle(Math.Max(0, cellBounds.Width - size), 0, size, size);
}
```

## Rules

* Use cell-relative geometry for hit-testing because `DataGridViewCellMouseEventArgs.Location` is cell-relative.
* Paint with absolute geometry by adding `e.CellBounds.X` and `e.CellBounds.Y`.
* Handle the status region first for click and tooltip routing.
* If the region is reserved but inactive, consume the click rather than falling through.
* Use one shared `System.Windows.Forms.Timer` for all animated cells.
* Timer ticks occur on the UI thread. Invalidate only affected cells with `DataGridView.InvalidateCell(columnIndex, rowIndex)`.
* Stop the timer when no visible row still needs animation.
