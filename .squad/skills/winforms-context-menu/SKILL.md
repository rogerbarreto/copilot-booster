---
name: "winforms-context-menu"
description: "How to add and test nested WinForms context menu items in CopilotBooster"
domain: "winforms-ui"
confidence: "high"
source: "issue-17"
---

## Pattern

* Session row context menu is built in `ExistingSessionsVisuals.BuildGridContextMenu()`.
* The GitHub group is the local `ToolStripMenuItem menuGitHub`. It is populated inside the `gridContextMenu.Opening` handler after resolving `GridVisuals.GetSelectedSessionId()`.
* For nested items, create a `ToolStripMenuItem` parent, add leaf items to `DropDownItems`, then add the parent to `menuGitHub.DropDownItems`.
* Mirror existing event pattern. Add an `internal event Action<string>?`, capture `sid` in the click handler, then invoke the event from the leaf item.
* For tests, expose a small internal builder that returns the submenu for a known session id. Tests can find the leaf item and call `PerformClick()` without showing a real context menu.
* For stateful context menu items, evaluate preconditions in the same path that renders the leaf item during `ContextMenuStrip.Opening`. The session cwd can come from `GetSessionPaths?.Invoke(sid).cwd`.
* Set `ShowItemToolTips = true` on the owning `ContextMenuStrip` and nested `ToolStripDropDown` when disabled `ToolStripMenuItem` tooltips are part of the UX.
* Use a leaf-level test seam such as `GetEvaluatedAiMenuItem(sid, cwd)` when tests need to assert `Enabled` and `ToolTipText` without opening a real menu.

## Example

```csharp
internal event Action<string>? OnExample;

internal ToolStripMenuItem BuildExampleMenuItem(string sid)
{
    var parent = new ToolStripMenuItem("Parent");
    var leaf = new ToolStripMenuItem("Leaf");
    leaf.Click += (_, _) => this.OnExample?.Invoke(sid);
    parent.DropDownItems.Add(leaf);
    return parent;
}
```
