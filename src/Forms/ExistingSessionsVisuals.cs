using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopilotBooster.Models;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Builds and owns the "Existing Sessions" tab UI controls.
/// Pure visuals — no service calls, no file I/O.
/// </summary>
[ExcludeFromCodeCoverage]
internal class ExistingSessionsVisuals
{
    private Image? _teamsIcon;
    private bool _suppressColumnOrderSave;
    private bool _cwdWidthRestored;
    private readonly LauncherSettings _settings;

    internal TextBox SearchBox = null!;
    internal DataGridView SessionGrid = null!;
    internal SessionGridVisuals GridVisuals = null!;
    internal Label LoadingOverlay = null!;
    internal DarkTabControl SessionTabs = null!;
    internal Button NewSessionButton = null!;
    internal Button SettingsButton = null!;

    /// <summary>Current sort column name. Defaults to "RunningApps".</summary>
    internal string SortColumn = "RunningApps";

    /// <summary>Current sort direction. Defaults to Descending (running first).</summary>
    internal SortOrder SortDirection = SortOrder.Descending;

    /// <summary>
    /// Gets the name of the currently selected session tab.
    /// </summary>
    internal string SelectedTabName
    {
        get
        {
            var tag = this.SessionTabs.SelectedTab?.Tag as string;
            return tag ?? this._settings.SessionTabs[0];
        }
    }

    /// <summary>Fired when the user double-clicks a session row. Arg = session id.</summary>
    internal event Action<string>? OnSessionDoubleClicked;

    /// <summary>
    /// Selects a session row by ID. If the session is on a different tab, switches to that tab first.
    /// Returns true if the session was found and selected.
    /// </summary>
    internal bool SelectSessionById(string sessionId, List<NamedSession> allSessions)
    {
        // Check if session is on current tab
        for (int i = 0; i < this.SessionGrid.Rows.Count; i++)
        {
            if (this.SessionGrid.Rows[i].Tag is string sid && string.Equals(sid, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                this.GridVisuals.SelectRowByIndex(i);
                return true;
            }
        }

        // Session not on current tab — find its tab and switch
        var session = allSessions.FirstOrDefault(s => string.Equals(s.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        if (session == null)
        {
            return false;
        }

        var targetTab = session.Tab;
        if (string.Equals(targetTab, this.SelectedTabName, StringComparison.OrdinalIgnoreCase))
        {
            return false; // Same tab but not visible (filtered out by search?)
        }

        // Switch tab
        foreach (TabPage page in this.SessionTabs.TabPages)
        {
            if (string.Equals(page.Tag as string, targetTab, StringComparison.OrdinalIgnoreCase))
            {
                this.SessionTabs.SelectedTab = page;
                break;
            }
        }

        // After tab switch, the grid will be repopulated — try selecting again
        for (int i = 0; i < this.SessionGrid.Rows.Count; i++)
        {
            if (this.SessionGrid.Rows[i].Tag is string sid2 && string.Equals(sid2, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                this.GridVisuals.SelectRowByIndex(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Fired when the user filters sessions via the search box.</summary>
    internal event Action? OnSearchChanged;

    /// <summary>Fired when the session tab (Active/Archived) changes.</summary>
    internal event Action? OnTabChanged;

    /// <summary>Fired when the New Session button is clicked.</summary>
    internal event Action? OnNewSessionClicked;

    /// <summary>Fired when the Settings button is clicked.</summary>
    internal event Action? OnSettingsClicked;

    // Context menu events — arg is always the selected session id.
    internal event Action<string>? OnOpenSession;
    internal event Action<string>? OnEditSession;
    internal event Action<string>? OnOpenAsNewSession;
    internal event Action<string>? OnOpenAsNewSessionWorkspace;
    internal event Action<string>? OnOpenTerminal;
    internal event Action<string>? OnOpenEdge;
    internal event Action<string>? OnSaveEdgeTabs;
    internal event Action<string>? OnOpenTeams;
    internal event Action<List<string>>? OnDeleteSessions;
    internal event Action<string>? OnOpenSessionFolder;
    internal event Action<string>? OnOpenFile;
    internal event Action<string>? OnOpenCwdExplorer;
    internal event Action<string, string>? OnMoveToTab;
    internal event Action<string>? OnPinSession;
    internal event Action<string>? OnUnpinSession;

    /// <summary>Fired when the user wants to open a session by entering its ID.</summary>
    internal event Action<string>? OnOpenSessionById;

    /// <summary>Fired when the user copies a session ID to clipboard.</summary>
    internal event Action<string>? OnCopySessionId;

    /// <summary>Fired when the user clicks a column header to change sort order.</summary>
    internal event Action? OnSortChanged;

    /// <summary>
    /// Fired for IDE context-menu clicks.
    /// Args: sessionId, IDE entry, useRepoRoot.
    /// </summary>
    internal event Action<string, IdeEntry, bool>? OnOpenInIde;

    /// <summary>
    /// Fired when user selects a specific file to open in an IDE.
    /// Args: sessionId, IDE entry, file full path.
    /// </summary>
    internal event Action<string, IdeEntry, string>? OnOpenInIdeFile;

    /// <summary>Fired when user selects "Add PR..." from context menu. Args: sessionId.</summary>
    internal event Action<string>? OnAddPr;

    /// <summary>Fired when user selects "Add Issue..." from context menu. Args: sessionId.</summary>
    internal event Action<string>? OnAddIssue;

    /// <summary>Fired when user selects "Show CI Jobs" for a tracked PR. Args: sessionId, prNumber.</summary>
    internal event Action<string, int>? OnShowCiJobs;

    /// <summary>Fired when user selects "Open in Edge" for a tracked item. Args: sessionId, type, number.</summary>
    internal event Action<string, string, int>? OnOpenGitHubItem;

    /// <summary>Fired when user selects "Remove" for a tracked item. Args: sessionId, type, number.</summary>
    internal event Action<string, string, int>? OnRemoveGitHubItem;

    /// <summary>Fired when a window is pinned to a session via drag-drop. Args: sessionId, hwnd, title.</summary>
    internal event Action<string, IntPtr, string>? OnWindowPinned;

    /// <summary>
    /// Callback to determine git-root visibility for context menu.
    /// Returns (hasGitRoot, isSubfolder).
    /// </summary>
    internal Func<string, (bool hasGitRoot, bool isSubfolder)>? GetGitRootInfo;

    /// <summary>
    /// Callback to determine if a session has a plan.md file.
    /// </summary>
    internal Func<string, bool>? HasPlanFile;

    /// <summary>
    /// Callback to list all files in a session's folder (plan.md + files subfolder).
    /// Returns (relativePath, fullPath) tuples.
    /// </summary>
    internal Func<string, List<(string Name, string FullPath)>>? GetSessionFiles;

    /// <summary>
    /// Callback to determine if a session is pinned.
    /// </summary>
    internal Func<string, bool>? IsSessionPinned;

    /// <summary>
    /// Callback to determine if a session has an open Edge workspace.
    /// </summary>
    internal Func<string, bool>? IsEdgeOpen;

    /// <summary>
    /// Callback to determine if a session has an open Teams window.
    /// </summary>
    internal Func<string, bool>? IsTeamsOpen;

    /// <summary>
    /// Callback to get a session's CWD and optional git root path.
    /// Returns (cwd, gitRoot) where gitRoot may be null.
    /// </summary>
    internal Func<string, (string? cwd, string? gitRoot)>? GetSessionPaths;

    internal ExistingSessionsVisuals(Control parentControl, ActiveStatusTracker activeTracker, LauncherSettings? settings = null)
    {
        this._settings = settings ?? Program._settings;
        this.InitializeSessionGrid();
        var searchPanel = this.BuildSearchPanel();
        this.GridVisuals = new SessionGridVisuals(this.SessionGrid, activeTracker, this._settings);
        if (this._cwdWidthRestored)
        {
            this.GridVisuals.CwdManuallyResized = true;
        }

        this.BuildGridContextMenu();

        // Dynamic session tabs from settings — reduced padding keeps the "+" tab compact
        this.SessionTabs = new DarkTabControl { Dock = DockStyle.Fill, Padding = new Point(12, 3) };
        this.BuildSessionTabs();
        this.SessionTabs.Selecting += (s, e) =>
        {
            // Block selection of the "+" tab — defer the prompt to after the event returns
            if (e.TabPage?.Tag == null && e.TabPage?.Text.Trim() == "+")
            {
                e.Cancel = true;
                this.SessionTabs.BeginInvoke(this.PromptAddTab);
            }
        };
        this.SessionTabs.SelectedIndexChanged += (s, e) =>
        {
            var selectedTab = this.SessionTabs.SelectedTab;
            if (selectedTab == null || selectedTab.Tag == null)
            {
                return;
            }

            // Move the grid to the newly selected tab
            this._suppressColumnOrderSave = true;
            selectedTab.Controls.Add(this.SessionGrid);
            this._suppressColumnOrderSave = false;
            this.OnTabChanged?.Invoke();
        };
        this.SessionTabs.TabReordered += (s, e) =>
        {
            var tabs = this._settings.SessionTabs;
            if (e.OldIndex < 0 || e.OldIndex >= tabs.Count || e.NewIndex < 0 || e.NewIndex >= tabs.Count)
            {
                return;
            }

            var tab = tabs[e.OldIndex];
            tabs.RemoveAt(e.OldIndex);
            tabs.Insert(e.NewIndex, tab);
            this._settings.Save();
            this.BuildSessionTabs();
            this.OnTabChanged?.Invoke();
        };

        this.SetupDragToTab();

        this.LoadingOverlay = new Label
        {
            Text = "Loading sessions...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14f, FontStyle.Regular)
        };

        parentControl.Controls.Add(this.LoadingOverlay);
        this.LoadingOverlay.BringToFront();
        parentControl.Controls.Add(this.SessionTabs);
        parentControl.Controls.Add(searchPanel);
    }

    private void InitializeSessionGrid()
    {
        this.SessionGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            RowHeadersVisible = false,
            BorderStyle = BorderStyle.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            CellBorderStyle = DataGridViewCellBorderStyle.Single,
            GridColor = Application.IsDarkModeEnabled ? Color.FromArgb(0x2A, 0x2A, 0x2A) : SystemColors.ControlDark,
            BackgroundColor = Application.IsDarkModeEnabled ? Color.FromArgb(0x11, 0x11, 0x11) : SystemColors.Window,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                WrapMode = DataGridViewTriState.True,
                Padding = new Padding(4, 4, 4, 4),
                BackColor = Application.IsDarkModeEnabled ? Color.FromArgb(0x11, 0x11, 0x11) : SystemColors.Window,
                ForeColor = Application.IsDarkModeEnabled ? Color.FromArgb(0xCC, 0xCC, 0xCC) : SystemColors.ControlText,
                SelectionBackColor = Application.IsDarkModeEnabled ? Color.FromArgb(0x38, 0x46, 0x59) : Color.FromArgb(200, 220, 245),
                SelectionForeColor = Application.IsDarkModeEnabled ? Color.White : Color.Black
            },
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold),
                BackColor = Application.IsDarkModeEnabled ? Color.FromArgb(0x22, 0x22, 0x22) : Color.FromArgb(210, 210, 210),
                ForeColor = Application.IsDarkModeEnabled ? Color.White : SystemColors.ControlText,
                SelectionBackColor = Application.IsDarkModeEnabled ? Color.FromArgb(0x22, 0x22, 0x22) : Color.FromArgb(210, 210, 210),
                SelectionForeColor = Application.IsDarkModeEnabled ? Color.White : SystemColors.ControlText
            },
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        this.SessionGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "",
            Width = 30,
            MinimumWidth = 30,
            Resizable = DataGridViewTriState.False,
            Frozen = true,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        this.SessionGrid.Columns.Add("Session", "Session");
        this.SessionGrid.Columns.Add("CWD", "CWD");
        this.SessionGrid.Columns.Add("Date", "Date");
        this.SessionGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Context",
            HeaderText = "Ctx.",
            ToolTipText = "Session Context Content",
            Width = 55,
            MinimumWidth = 40,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        var runningAppsCol = new DataGridViewTextBoxColumn
        {
            Name = "RunningApps",
            HeaderText = "Running",
            ToolTipText = "Applications running in session context",
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
        };
        runningAppsCol.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        this.SessionGrid.Columns.Add(runningAppsCol);
        this.SessionGrid.Columns["Session"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        this.SessionGrid.Columns["Session"]!.MinimumWidth = 30;
        var savedCwdWidth = this._settings.CwdColumnWidth;
        this.SessionGrid.Columns["CWD"]!.Width = savedCwdWidth > 0 ? savedCwdWidth : 100;
        this.SessionGrid.Columns["CWD"]!.MinimumWidth = 30;
        this._cwdWidthRestored = savedCwdWidth > 0;
        var dateWidth = GetDateColumnWidth(this._settings.DateFormat, this.SessionGrid.Font);
        this.SessionGrid.Columns["Date"]!.Width = dateWidth;
        this.SessionGrid.Columns["Date"]!.MinimumWidth = dateWidth;
        this.SessionGrid.Columns["Date"]!.Resizable = DataGridViewTriState.False;
        this.SessionGrid.Columns["Date"]!.HeaderCell.ToolTipText = "Date Created";
        this.SessionGrid.Columns["Context"]!.Resizable = DataGridViewTriState.False;
        this.SessionGrid.Columns["RunningApps"]!.Width = 110;
        this.SessionGrid.Columns["RunningApps"]!.MinimumWidth = 60;
        this.SessionGrid.Columns["RunningApps"]!.Resizable = DataGridViewTriState.False;
        var githubCol = new DataGridViewTextBoxColumn
        {
            Name = "GitHub",
            HeaderText = "GitHub",
            ToolTipText = "Tracked PRs and Issues",
            Width = 80,
            MinimumWidth = 50,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
        };
        githubCol.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        this.SessionGrid.Columns.Add(githubCol);

        // Restore saved column display order
        var savedOrder = this._settings.SessionColumnOrder;
        if (savedOrder.Count > 0)
        {
            // Default column order (non-frozen) for inserting new columns at correct positions
            var defaultOrder = new List<string> { "Session", "CWD", "Date", "Context", "RunningApps", "GitHub" };

            // Build full order: start from saved, insert missing columns at their default position
            var fullOrder = new List<string>(savedOrder);
            foreach (var name in defaultOrder)
            {
                if (!fullOrder.Contains(name, StringComparer.OrdinalIgnoreCase)
                    && this.SessionGrid.Columns[name] is { } missingCol && !missingCol.Frozen)
                {
                    // Find the best insertion point based on default order
                    int defaultIdx = defaultOrder.IndexOf(name);
                    int insertAt = fullOrder.Count;
                    for (int i = defaultIdx + 1; i < defaultOrder.Count; i++)
                    {
                        int pos = fullOrder.FindIndex(n => string.Equals(n, defaultOrder[i], StringComparison.OrdinalIgnoreCase));
                        if (pos >= 0)
                        {
                            insertAt = pos;
                            break;
                        }
                    }
                    fullOrder.Insert(insertAt, name);
                }
            }

            int displayIndex = 1; // 0 is Status (frozen)
            foreach (var name in fullOrder)
            {
                if (this.SessionGrid.Columns[name] is { } col && !col.Frozen)
                {
                    col.DisplayIndex = displayIndex++;
                }
            }
        }

        // Save column order when user drags columns
        bool savingColumnOrder = false;
        this.SessionGrid.ColumnDisplayIndexChanged += (s, e) =>
        {
            if (savingColumnOrder || this._suppressColumnOrderSave)
            {
                return;
            }

            savingColumnOrder = true;
            this.SessionGrid.BeginInvoke(() =>
            {
                var order = this.SessionGrid.Columns.Cast<DataGridViewColumn>()
                    .Where(c => !c.Frozen)
                    .OrderBy(c => c.DisplayIndex)
                    .Select(c => c.Name)
                    .ToList();
                this._settings.SessionColumnOrder = order;
                this._settings.Save();
                savingColumnOrder = false;
            });
        };

        // Column header click → sort by that column
        this.SessionGrid.Columns["RunningApps"]!.HeaderCell.SortGlyphDirection = SortOrder.Descending;
        this.SessionGrid.ColumnHeaderMouseClick += (s, e) =>
        {
            var col = this.SessionGrid.Columns[e.ColumnIndex];
            if (col.Frozen)
            {
                return;
            }

            if (col.Name == this.SortColumn)
            {
                this.SortDirection = this.SortDirection == SortOrder.Ascending
                    ? SortOrder.Descending
                    : SortOrder.Ascending;
            }
            else
            {
                this.SortColumn = col.Name;
                this.SortDirection = col.Name is "Date" or "RunningApps"
                    ? SortOrder.Descending
                    : SortOrder.Ascending;
            }

            foreach (DataGridViewColumn c in this.SessionGrid.Columns)
            {
                if (c.SortMode == DataGridViewColumnSortMode.NotSortable)
                {
                    continue;
                }

                c.HeaderCell.SortGlyphDirection = c.Name == this.SortColumn
                    ? this.SortDirection
                    : SortOrder.None;
            }

            this.OnSortChanged?.Invoke();
        };

        // Window pin drag-and-drop: accept HWND drops from Win+Alt+C context menu
        bool adjustingSessionWidth = false;
        void AdjustSessionColumnWidth()
        {
            if (adjustingSessionWidth)
            {
                return;
            }
            adjustingSessionWidth = true;
            try
            {
                var ctxCol = this.SessionGrid.Columns["Context"]!;
                var otherWidth = this.SessionGrid.Columns["Status"]!.Width
                    + this.SessionGrid.Columns["CWD"]!.Width
                    + this.SessionGrid.Columns["Date"]!.Width
                    + ctxCol.Width
                    + this.SessionGrid.Columns["RunningApps"]!.Width
                    + this.SessionGrid.Columns["GitHub"]!.Width
                    + (this.SessionGrid.RowHeadersVisible ? this.SessionGrid.RowHeadersWidth : 0)
                    + SystemInformation.VerticalScrollBarWidth + 2;
                var fill = this.SessionGrid.ClientSize.Width - otherWidth;
                if (fill >= this.SessionGrid.Columns["Session"]!.MinimumWidth)
                {
                    this.SessionGrid.Columns["Session"]!.Width = fill;
                }
            }
            finally { adjustingSessionWidth = false; }
        }
        this.SessionGrid.Resize += (s, e) => AdjustSessionColumnWidth();
        this.SessionGrid.ColumnWidthChanged += (s, e) =>
        {
            if (e.Column.Name == "CWD" && !adjustingSessionWidth)
            {
                this.GridVisuals.CwdManuallyResized = true;
                this._settings.CwdColumnWidth = e.Column.Width;
                this._settings.Save();
                AdjustSessionColumnWidth();
            }
        };

        this.SessionGrid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0)
            {
                var sid = this.SessionGrid.Rows[e.RowIndex].Tag as string;
                if (sid != null)
                {
                    this.OnSessionDoubleClicked?.Invoke(sid);
                }
            }
        };

        this.SessionGrid.CellPainting += (s, e) =>
        {
            if (e.RowIndex != -1)
            {
                return;
            }

            e.PaintBackground(e.ClipBounds, false);
            var borderColor = Application.IsDarkModeEnabled ? Color.FromArgb(80, 80, 80) : SystemColors.ControlDark;
            var textColor = Application.IsDarkModeEnabled ? Color.White : SystemColors.ControlText;
            using var borderPen = new Pen(borderColor);
            e.Graphics!.DrawLine(borderPen, e.CellBounds.Right - 1, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
            e.Graphics.DrawLine(borderPen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
            var textBounds = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + 2, e.CellBounds.Width - 20, e.CellBounds.Height - 4);
            TextRenderer.DrawText(e.Graphics, e.Value?.ToString() ?? "", e.CellStyle!.Font, textBounds, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            var col = this.SessionGrid.Columns[e.ColumnIndex];
            if (col.HeaderCell.SortGlyphDirection != SortOrder.None)
            {
                var glyphX = e.CellBounds.Right - 16;
                var glyphY = e.CellBounds.Top + ((e.CellBounds.Height - 8) / 2);
                using var brush = new SolidBrush(textColor);
                if (col.HeaderCell.SortGlyphDirection == SortOrder.Ascending)
                {
                    e.Graphics.FillPolygon(brush, [new Point(glyphX, glyphY + 8), new Point(glyphX + 4, glyphY), new Point(glyphX + 8, glyphY + 8)]);
                }
                else
                {
                    e.Graphics.FillPolygon(brush, [new Point(glyphX, glyphY), new Point(glyphX + 4, glyphY + 8), new Point(glyphX + 8, glyphY)]);
                }
            }
            e.Handled = true;
        };
    }

    private Panel BuildSearchPanel()
    {
        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 34 };

        var copilotIcon = TryGetExeIcon(Program.CopilotExePath);

        this.NewSessionButton = new Button
        {
            Width = 32,
            Height = 27,
            Location = new Point(5, 3),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            ImageAlign = ContentAlignment.MiddleCenter
        };
        this.NewSessionButton.FlatAppearance.BorderSize = 0;
        this.NewSessionButton.FlatAppearance.BorderColor = this.NewSessionButton.BackColor;
        this.NewSessionButton.FlatAppearance.MouseOverBackColor = Color.Transparent;
        this.NewSessionButton.FlatAppearance.MouseDownBackColor = Color.Transparent;
        if (copilotIcon != null)
        {
            this.NewSessionButton.Image = new Bitmap(copilotIcon, 20, 20);
        }
        else
        {
            this.NewSessionButton.Text = "+";
            this.NewSessionButton.Font = new Font(SystemFonts.DefaultFont.FontFamily, 12f, FontStyle.Bold);
        }

        var newSessionTooltip = new ToolTip();
        newSessionTooltip.SetToolTip(this.NewSessionButton, "Create new or open existing Copilot CLI sessions");

        var newSessionMenu = new ContextMenuStrip();
        var menuNew = new ToolStripMenuItem("New") { Image = copilotIcon?.Clone() as Image };
        menuNew.Click += (s, e) => this.OnNewSessionClicked?.Invoke();
        var menuOpenExisting = new ToolStripMenuItem("Open Existing") { Image = copilotIcon?.Clone() as Image };
        menuOpenExisting.Click += (s, e) =>
        {
            var inputForm = new Form
            {
                Text = "Open Session by Id",
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                Width = 500,
                Height = 170,
                TopMost = Program._settings.AlwaysOnTop
            };
            if (Program.AppIcon != null)
            {
                inputForm.Icon = Program.AppIcon;
            }

            var lblId = new Label { Text = "Session Id", AutoSize = true, Location = new Point(14, 12) };
            var txtId = new TextBox { PlaceholderText = "Paste or type the session Id", Location = new Point(14, 34), Width = 450 };
            var btnOk = new Button { Text = "Open", Width = 80, Location = new Point(300, 70), DialogResult = DialogResult.None };
            var btnCancel = new Button { Text = "Cancel", Width = 80, Location = new Point(390, 70), DialogResult = DialogResult.Cancel };
            btnOk.Click += (_, _) =>
            {
                var id = txtId.Text.Trim();
                if (!string.IsNullOrEmpty(id))
                {
                    inputForm.DialogResult = DialogResult.OK;
                    inputForm.Close();
                }
            };
            inputForm.Controls.AddRange([lblId, SettingsVisuals.WrapWithBorder(txtId), btnOk, btnCancel]);
            inputForm.AcceptButton = btnOk;
            inputForm.CancelButton = btnCancel;
            if (inputForm.ShowDialog() == DialogResult.OK)
            {
                var sessionId = txtId.Text.Trim();
                var sessionDir = Path.Combine(Program.SessionStateDir, sessionId);
                if (!Directory.Exists(sessionDir) || !File.Exists(Path.Combine(sessionDir, "workspace.yaml")))
                {
                    MessageBox.Show($"Session not found:\n\n{sessionId}", "Open Session", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (SessionArchiveService.IsDeleted(Program.SessionStateFile, sessionId))
                {
                    MessageBox.Show($"This session has been deleted:\n\n{sessionId}", "Open Session", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                this.OnOpenSessionById?.Invoke(sessionId);
            }
        };
        newSessionMenu.Items.Add(menuNew);
        newSessionMenu.Items.Add(menuOpenExisting);

        this.NewSessionButton.Click += (s, e) =>
        {
            newSessionMenu.ShowOnCurrentScreen(this.NewSessionButton, new Point(0, this.NewSessionButton.Height));
        };

        var searchLabel = new Label
        {
            Text = "Search:",
            AutoSize = true,
            Location = new Point(44, 9),
            Margin = new Padding(0, 0, 8, 0)
        };
        var shell32Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
        var settingsIcon = TryExtractIcon(shell32Path, 314);

        this.SettingsButton = new Button
        {
            Width = 32,
            Height = 27,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(searchPanel.Width - 37, 3),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            ImageAlign = ContentAlignment.MiddleCenter,
            TabStop = false
        };
        this.SettingsButton.FlatAppearance.BorderSize = 0;
        this.SettingsButton.FlatAppearance.MouseOverBackColor = Color.Transparent;
        this.SettingsButton.FlatAppearance.MouseDownBackColor = Color.Transparent;
        this.SettingsButton.Paint += (s, e) =>
        {
            var btn = (Button)s!;
            e.Graphics.Clear(btn.Parent?.BackColor ?? btn.BackColor);
            if (btn.Image != null)
            {
                var x = (btn.Width - btn.Image.Width) / 2;
                var y = (btn.Height - btn.Image.Height) / 2;
                e.Graphics.DrawImage(btn.Image, x, y);
            }
        };
        if (settingsIcon != null)
        {
            this.SettingsButton.Image = new Bitmap(settingsIcon, 20, 20);
        }
        else
        {
            this.SettingsButton.Text = "⚙";
            this.SettingsButton.Font = new Font(SystemFonts.DefaultFont.FontFamily, 12f);
        }

        var settingsTooltip = new ToolTip();
        settingsTooltip.SetToolTip(this.SettingsButton, "Settings");
        searchPanel.Resize += (s, e) => this.SettingsButton.Left = searchPanel.ClientSize.Width - 37;
        this.SettingsButton.Click += (s, e) => this.OnSettingsClicked?.Invoke();
        this.SearchBox = new TextBox
        {
            Location = new Point(102, 4),
            Width = 100,
            Height = 20,
            Multiline = true,
            WordWrap = false,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            PlaceholderText = "Filter sessions..."
        };
        this.SearchBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
            }
        };
        var searchBorder = SettingsVisuals.WrapWithBorder(this.SearchBox);
        searchPanel.Resize += (s, e) => searchBorder.Width = searchPanel.ClientSize.Width - 142;
        var debounceTimer = new Timer { Interval = 500 };
        debounceTimer.Tick += (s, e) =>
        {
            debounceTimer.Stop();
            this.OnSearchChanged?.Invoke();
        };
        this.SearchBox.TextChanged += (s, e) =>
        {
            debounceTimer.Stop();
            debounceTimer.Start();
        };
        searchPanel.Controls.Add(searchBorder);
        searchPanel.Controls.Add(this.SettingsButton);
        searchPanel.Controls.Add(searchLabel);
        searchPanel.Controls.Add(this.NewSessionButton);
        return searchPanel;
    }

    /// <summary>
    /// Computes the Date column width based on the configured date format.
    /// Uses a sample date string to measure text width.
    /// </summary>
    private static int GetDateColumnWidth(string format, Font font)
    {
        var sample = new DateTime(2026, 12, 28, 23, 59, 0).ToString(format);
        var padding = format.Contains("yyyy") ? 45 : format.Contains("tt") ? 40 : 30;
        var width = TextRenderer.MeasureText(sample, font).Width + padding;
        return Math.Max(width, 80);
    }

    /// <summary>
    /// Sets up drag-from-grid to drop-on-tab for moving sessions between tabs.
    /// </summary>
    private void SetupDragToTab()
    {
        const string DragFormat = "CopilotBooster.SessionIds";
        Point dragStart = Point.Empty;
        bool dragInitiated = false;
        List<int> preservedSelection = [];

        // Capture multi-selection before the grid clears it on left-click
        this.SessionGrid.CellMouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && e.RowIndex >= 0
                && this.SessionGrid.SelectedRows.Count > 1
                && this.SessionGrid.Rows[e.RowIndex].Selected)
            {
                preservedSelection = this.SessionGrid.SelectedRows
                    .Cast<DataGridViewRow>().Select(r => r.Index).ToList();
            }
            else
            {
                preservedSelection = [];
            }
        };

        // Initiate drag from grid rows
        this.SessionGrid.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && e.Clicks == 1)
            {
                var hitTest = this.SessionGrid.HitTest(e.X, e.Y);
                if (hitTest.Type == DataGridViewHitTestType.Cell)
                {
                    dragStart = e.Location;
                    dragInitiated = false;
                }
            }
        };

        this.SessionGrid.MouseMove += (s, e) =>
        {
            if (e.Button != MouseButtons.Left || dragInitiated || dragStart == Point.Empty)
            {
                return;
            }

            if (Math.Abs(e.X - dragStart.X) < 8 && Math.Abs(e.Y - dragStart.Y) < 8)
            {
                return;
            }

            // Restore multi-selection that the grid cleared on MouseDown
            if (preservedSelection.Count > 1)
            {
                foreach (var idx in preservedSelection)
                {
                    if (idx >= 0 && idx < this.SessionGrid.Rows.Count)
                    {
                        this.SessionGrid.Rows[idx].Selected = true;
                    }
                }

                preservedSelection = [];
            }

            var ids = this.GridVisuals.GetSelectedSessionIds();
            if (ids.Count > 0)
            {
                dragInitiated = true;
                var data = new DataObject(DragFormat, string.Join("|", ids));
                this.SessionGrid.DoDragDrop(data, DragDropEffects.Move);
                dragStart = Point.Empty;
                dragInitiated = false;
            }
        };

        this.SessionGrid.MouseUp += (s, e) =>
        {
            dragStart = Point.Empty;
            preservedSelection = [];
        };

        // Accept drop on session tabs
        this.SessionTabs.AllowDrop = true;

        this.SessionTabs.DragEnter += (s, e) =>
        {
            e.Effect = e.Data?.GetDataPresent(DragFormat) == true
                ? DragDropEffects.Move
                : DragDropEffects.None;
        };

        int highlightTabIndex = -1;

        this.SessionTabs.DragOver += (s, e) =>
        {
            if (e.Data?.GetDataPresent(DragFormat) != true)
            {
                return;
            }

            var pt = this.SessionTabs.PointToClient(new Point(e.X, e.Y));
            int tabIndex = this.SessionTabs.GetTabIndexAtPoint(pt);

            if (tabIndex >= 0 && tabIndex < this.SessionTabs.TabCount)
            {
                var page = this.SessionTabs.TabPages[tabIndex];
                var tabName = page.Tag as string;
                if (tabName != null && !string.Equals(tabName, this.SelectedTabName, StringComparison.OrdinalIgnoreCase))
                {
                    e.Effect = DragDropEffects.Move;
                    if (tabIndex != highlightTabIndex)
                    {
                        highlightTabIndex = tabIndex;
                        this.SessionTabs.Invalidate();
                    }
                    return;
                }
            }

            e.Effect = DragDropEffects.None;
            if (highlightTabIndex != -1)
            {
                highlightTabIndex = -1;
                this.SessionTabs.Invalidate();
            }
        };

        this.SessionTabs.DragLeave += (s, e) =>
        {
            if (highlightTabIndex != -1)
            {
                highlightTabIndex = -1;
                this.SessionTabs.Invalidate();
            }
        };

        this.SessionTabs.DragDrop += (s, e) =>
        {
            highlightTabIndex = -1;
            this.SessionTabs.Invalidate();

            if (e.Data?.GetDataPresent(DragFormat) != true)
            {
                return;
            }

            var pt = this.SessionTabs.PointToClient(new Point(e.X, e.Y));
            int tabIndex = this.SessionTabs.GetTabIndexAtPoint(pt);

            if (tabIndex < 0 || tabIndex >= this.SessionTabs.TabCount)
            {
                return;
            }

            var tabName = this.SessionTabs.TabPages[tabIndex].Tag as string;
            if (tabName == null || string.Equals(tabName, this.SelectedTabName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var idsStr = e.Data.GetData(DragFormat) as string;
            if (string.IsNullOrEmpty(idsStr))
            {
                return;
            }

            foreach (var sid in idsStr.Split('|'))
            {
                if (!string.IsNullOrEmpty(sid))
                {
                    this.OnMoveToTab?.Invoke(sid, tabName);
                }
            }
        };
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    internal static Bitmap? TryExtractIcon(string filePath, int index)
    {
        try
        {
            var hIcon = ExtractIcon(IntPtr.Zero, filePath, index);
            if (hIcon != IntPtr.Zero)
            {
                using var icon = Icon.FromHandle(hIcon);
                var bmp = new Bitmap(icon.ToBitmap(), 16, 16);
                DestroyIcon(hIcon);
                return bmp;
            }
        }
        catch { /* ignore extraction failures */ }
        return null;
    }

    internal static Bitmap? TryGetExeIcon(string exePath)
    {
        try
        {
            exePath = exePath.Trim('"');
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                return TryExtractIcon(exePath, 0);
            }
        }
        catch { /* ignore icon extraction failures */ }
        return null;
    }

    private async Task LoadTeamsIconAsync()
    {
        try
        {
            this._teamsIcon = await TeamsWindowService.GetCachedIconAsync().ConfigureAwait(false);
        }
        catch { /* icon is optional */ }
    }

    internal void BuildGridContextMenu()
    {
        // Start loading Teams icon asynchronously
        _ = this.LoadTeamsIconAsync();

        var gridContextMenu = new ContextMenuStrip();
        var shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
        var imageres = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "imageres.dll");

        var appIcon = Program.AppIcon != null ? new Bitmap(Program.AppIcon.ToBitmap(), 16, 16) : null;
        var copilotIcon = TryGetExeIcon(Program.CopilotExePath) ?? appIcon;

        // --- Session ID header (copy to clipboard) ---
        var menuSessionIdHeader = new ToolStripMenuItem("") { Enabled = true, Image = TryExtractIcon(shell32, 134) };
        menuSessionIdHeader.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                Clipboard.SetText(sid);
                this.OnCopySessionId?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuSessionIdHeader);
        gridContextMenu.Items.Add(new ToolStripSeparator());

        // --- Session operations (top group) ---
        var menuOpenFiles = new ToolStripMenuItem("Open Files") { Image = TryExtractIcon(shell32, 250) };
        menuOpenFiles.DropDownItems.Add(new ToolStripMenuItem("Loading...") { Enabled = false });
        menuOpenFiles.DropDownOpening += (s, e) =>
        {
            menuOpenFiles.DropDownItems.Clear();
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid == null)
            {
                return;
            }

            // Top item: open session folder in Explorer
            var openFolder = new ToolStripMenuItem("Open Session Folder") { Image = TryExtractIcon(shell32, 3) };
            openFolder.Click += (_, _) => this.OnOpenSessionFolder?.Invoke(sid);
            menuOpenFiles.DropDownItems.Add(openFolder);

            // List session files
            var files = this.GetSessionFiles?.Invoke(sid);
            if (files is { Count: > 0 })
            {
                menuOpenFiles.DropDownItems.Add(new ToolStripSeparator());
                foreach (var (name, fullPath) in files)
                {
                    var capturedPath = fullPath;
                    Image? fileIcon = null;
                    try
                    {
                        var ico = Icon.ExtractAssociatedIcon(fullPath);
                        if (ico != null)
                        {
                            fileIcon = new Bitmap(ico.ToBitmap(), 16, 16);
                        }
                    }
                    catch
                    {
                        // Ignore icon extraction failures
                    }

                    var fileItem = new ToolStripMenuItem(name) { Image = fileIcon };
                    fileItem.Click += (_, _) => this.OnOpenFile?.Invoke(capturedPath);
                    menuOpenFiles.DropDownItems.Add(fileItem);
                }
            }
        };
        gridContextMenu.Items.Add(menuOpenFiles);

        var menuOpenSession = new ToolStripMenuItem("Open Session") { Image = copilotIcon };
        menuOpenSession.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenSession);

        var editMenuItem = new ToolStripMenuItem("Edit Session") { Image = TryExtractIcon(shell32, 269) };
        editMenuItem.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnEditSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(editMenuItem);

        var menuPinSession = new ToolStripMenuItem("Pin Session") { Image = TryExtractIcon(imageres, 234) };
        menuPinSession.Click += (s, e) =>
        {
            foreach (var sid in this.GridVisuals.GetSelectedSessionIds())
            {
                this.OnPinSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuPinSession);

        var menuUnpinSession = new ToolStripMenuItem("Unpin Session") { Image = TryExtractIcon(imageres, 234) };
        menuUnpinSession.Click += (s, e) =>
        {
            foreach (var sid in this.GridVisuals.GetSelectedSessionIds())
            {
                this.OnUnpinSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuUnpinSession);

        var menuMoveToTab = new ToolStripMenuItem("Move to") { Image = TryExtractIcon(shell32, 265) };
        gridContextMenu.Items.Add(menuMoveToTab);

        // --- GitHub tracking ---
        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuGitHub = new ToolStripMenuItem("GitHub");
        gridContextMenu.Items.Add(menuGitHub);

        // Populate GitHub submenu dynamically when opening
        gridContextMenu.Opening += (s, e) =>
        {
            menuGitHub.DropDownItems.Clear();

            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid == null)
            {
                return;
            }

            // Add PR / Add Issue
            var addPr = new ToolStripMenuItem("Add PR...");
            addPr.Click += (_, _) => this.OnAddPr?.Invoke(sid);
            menuGitHub.DropDownItems.Add(addPr);

            var addIssue = new ToolStripMenuItem("Add Issue...");
            addIssue.Click += (_, _) => this.OnAddIssue?.Invoke(sid);
            menuGitHub.DropDownItems.Add(addIssue);

            // List tracked items
            var data = GitHubTrackingService.Load(sid);
            if (data != null && data.Items.Count > 0)
            {
                menuGitHub.DropDownItems.Add(new ToolStripSeparator());

                foreach (var item in data.Items)
                {
                    var prefix = item.IsPr ? "PR" : "Issue";
                    var stateHint = item.State != "open" ? $" ({item.State})" : "";
                    var itemMenu = new ToolStripMenuItem($"{prefix} #{item.Number}{stateHint} — {item.Title}");

                    if (item.IsPr)
                    {
                        var showCi = new ToolStripMenuItem("Show CI Jobs");
                        var capturedNumber = item.Number;
                        showCi.Click += (_, _) => this.OnShowCiJobs?.Invoke(sid, capturedNumber);
                        itemMenu.DropDownItems.Add(showCi);
                    }

                    var openInBrowser = new ToolStripMenuItem("Open in Browser");
                    var capturedType = item.Type;
                    var capturedNum = item.Number;
                    openInBrowser.Click += (_, _) => this.OnOpenGitHubItem?.Invoke(sid, capturedType, capturedNum);
                    itemMenu.DropDownItems.Add(openInBrowser);

                    itemMenu.DropDownItems.Add(new ToolStripSeparator());

                    var remove = new ToolStripMenuItem("Remove");
                    remove.Click += (_, _) => this.OnRemoveGitHubItem?.Invoke(sid, capturedType, capturedNum);
                    itemMenu.DropDownItems.Add(remove);

                    menuGitHub.DropDownItems.Add(itemMenu);
                }
            }
        };

        // --- New session operations ---
        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuOpenNewSession = new ToolStripMenuItem("Open as New Copilot Session") { Image = copilotIcon?.Clone() as Image };
        menuOpenNewSession.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenAsNewSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenNewSession);

        var menuOpenNewSessionWorkspace = new ToolStripMenuItem("Open as New Copilot Session Workspace") { Image = copilotIcon?.Clone() as Image };
        menuOpenNewSessionWorkspace.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenAsNewSessionWorkspace?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenNewSessionWorkspace);

        var menuOpen = new ToolStripMenuItem("Open") { Image = copilotIcon?.Clone() as Image };
        var menuOpenNew = new ToolStripMenuItem("Open New") { Image = copilotIcon?.Clone() as Image };
        menuOpenNew.Click += (s, e) => this.OnNewSessionClicked?.Invoke();
        var menuOpenById = new ToolStripMenuItem("Open by Id") { Image = copilotIcon?.Clone() as Image };
        menuOpenById.Click += (s, e) =>
        {
            var inputForm = new Form
            {
                Text = "Open Session by Id",
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                Width = 500,
                Height = 170,
                TopMost = Program._settings.AlwaysOnTop
            };
            if (Program.AppIcon != null)
            {
                inputForm.Icon = Program.AppIcon;
            }

            var lblId = new Label { Text = "Session Id", AutoSize = true, Location = new Point(14, 12) };
            var txtId = new TextBox { PlaceholderText = "Paste or type the session Id", Location = new Point(14, 34), Width = 450 };
            var btnOk = new Button { Text = "Open", Width = 80, Location = new Point(300, 70), DialogResult = DialogResult.None };
            var btnCancel = new Button { Text = "Cancel", Width = 80, Location = new Point(390, 70), DialogResult = DialogResult.Cancel };
            btnOk.Click += (_, _) =>
            {
                var id = txtId.Text.Trim();
                if (!string.IsNullOrEmpty(id))
                {
                    inputForm.DialogResult = DialogResult.OK;
                    inputForm.Close();
                }
            };
            inputForm.Controls.AddRange([lblId, SettingsVisuals.WrapWithBorder(txtId), btnOk, btnCancel]);
            inputForm.AcceptButton = btnOk;
            inputForm.CancelButton = btnCancel;
            if (inputForm.ShowDialog() == DialogResult.OK)
            {
                var sessionId = txtId.Text.Trim();
                var sessionDir = Path.Combine(Program.SessionStateDir, sessionId);
                if (!Directory.Exists(sessionDir) || !File.Exists(Path.Combine(sessionDir, "workspace.yaml")))
                {
                    MessageBox.Show($"Session not found:\n\n{sessionId}", "Open Session", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (SessionArchiveService.IsDeleted(Program.SessionStateFile, sessionId))
                {
                    MessageBox.Show($"This session has been deleted:\n\n{sessionId}", "Open Session", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                this.OnOpenSessionById?.Invoke(sessionId);
            }
        };
        menuOpen.DropDownItems.Add(menuOpenNew);
        menuOpen.DropDownItems.Add(menuOpenById);
        gridContextMenu.Items.Add(menuOpen);

        // --- Terminal ---
        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuOpenTerminal = new ToolStripMenuItem("Open Terminal") { Image = TryExtractIcon(imageres, 264) };
        menuOpenTerminal.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenTerminal?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenTerminal);

        // --- Explorer & IDEs ---

        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuOpenCwdExplorer = new ToolStripMenuItem("Open in Explorer (CWD)") { Image = TryExtractIcon(shell32, 3) };
        menuOpenCwdExplorer.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenCwdExplorer?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenCwdExplorer);

        if (this._settings.Ides.Count > 0)
        {
            foreach (var ide in this._settings.Ides)
            {
                var capturedIde = ide;
                var ideIcon = TryGetExeIcon(ide.Path);

                var menuIde = new ToolStripMenuItem($"Open in {ide.Description}") { Image = ideIcon };
                menuIde.DropDownItems.Add(new ToolStripMenuItem("Loading...") { Enabled = false });
                menuIde.DropDownOpening += (s, e) => this.PopulateIdeUnifiedSubMenu(menuIde, capturedIde, ideIcon);
                gridContextMenu.Items.Add(menuIde);
            }
        }

        // --- Edge ---
        gridContextMenu.Items.Add(new ToolStripSeparator());

        var edgeIcon = TryGetExeIcon(EdgeWorkspaceService.FindEdgePath() ?? "");

        var menuOpenEdge = new ToolStripMenuItem("Open in Edge") { Image = edgeIcon };
        menuOpenEdge.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenEdge?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenEdge);

        var menuSaveEdgeTabs = new ToolStripMenuItem("Save Edge State")
        {
            Image = TryExtractIcon(shell32, 258), ToolTipText = "Saves all open Edge tab URLs so they can be restored next time you open Edge for this session"
        };
        menuSaveEdgeTabs.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnSaveEdgeTabs?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuSaveEdgeTabs);

        // --- Teams ---
        var menuOpenTeams = new ToolStripMenuItem("Open in Teams") { Image = this._teamsIcon };
        menuOpenTeams.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenTeams?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenTeams);

        // --- Delete (last) ---
        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuDeleteSession = new ToolStripMenuItem("Delete Session") { Image = TryExtractIcon(shell32, 131) };
        menuDeleteSession.Click += (s, e) =>
        {
            var sids = this.GridVisuals.GetSelectedSessionIds();
            if (sids.Count > 0)
            {
                this.OnDeleteSessions?.Invoke(sids);
            }
        };
        gridContextMenu.Items.Add(menuDeleteSession);

        gridContextMenu.Opening += (s, e) =>
        {
            var selectedIds = this.GridVisuals.GetSelectedSessionIds();
            if (selectedIds.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            bool isMultiSelect = selectedIds.Count > 1;

            // Update session ID header
            var headerSid = this.GridVisuals.GetSelectedSessionId();
            if (headerSid != null && !isMultiSelect)
            {
                menuSessionIdHeader.Text = $"Id: {headerSid}";
                menuSessionIdHeader.ToolTipText = headerSid;
                menuSessionIdHeader.Visible = true;
            }
            else
            {
                menuSessionIdHeader.Visible = false;
            }

            // Single-select only items — disabled in multi-select
            menuOpenSession.Enabled = !isMultiSelect;
            editMenuItem.Enabled = !isMultiSelect;
            menuOpenNewSession.Enabled = !isMultiSelect;
            menuOpenNewSessionWorkspace.Enabled = !isMultiSelect;
            menuOpenTerminal.Enabled = !isMultiSelect;
            menuOpenEdge.Enabled = !isMultiSelect;
            menuSaveEdgeTabs.Enabled = !isMultiSelect;
            menuOpenTeams.Enabled = !isMultiSelect;
            menuOpenCwdExplorer.Enabled = !isMultiSelect;
            menuOpenFiles.Enabled = !isMultiSelect;
            menuDeleteSession.Enabled = true;
            menuDeleteSession.Text = isMultiSelect ? $"Delete Sessions ({selectedIds.Count})" : "Delete Session";

            // IDE items — disable in multi-select
            foreach (ToolStripItem item in gridContextMenu.Items)
            {
                if (item is ToolStripMenuItem mi && mi.Text is string text && text.StartsWith("Open in ") && mi != menuOpenCwdExplorer)
                {
                    mi.Enabled = !isMultiSelect;
                }
            }

            // Single-select visibility logic
            bool hasGitRoot = false;
            bool isSubfolder = false;
            var sessionId = this.GridVisuals.GetSelectedSessionId();
            if (!isMultiSelect && sessionId != null && this.GetGitRootInfo != null)
            {
                (hasGitRoot, isSubfolder) = this.GetGitRootInfo(sessionId);
            }
            menuOpenNewSessionWorkspace.Visible = !isMultiSelect && hasGitRoot;

            // Save Edge Tabs — only visible when Edge is open for this session
            bool edgeOpen = !isMultiSelect && sessionId != null && this.IsEdgeOpen != null && this.IsEdgeOpen(sessionId);
            menuSaveEdgeTabs.Visible = edgeOpen;

            // Teams — update label based on open state
            bool teamsOpen = !isMultiSelect && sessionId != null && this.IsTeamsOpen != null && this.IsTeamsOpen(sessionId);
            menuOpenTeams.Text = teamsOpen ? "Focus Teams" : "Open in Teams";

            // "Move to" submenu — show all tabs except current
            menuMoveToTab.DropDownItems.Clear();
            var currentTab = this.SelectedTabName;
            foreach (var tabName in this._settings.SessionTabs)
            {
                if (string.Equals(tabName, currentTab, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var capturedTab = tabName;
                var tabItem = new ToolStripMenuItem(tabName);
                tabItem.Click += (s2, e2) =>
                {
                    foreach (var sid in this.GridVisuals.GetSelectedSessionIds())
                    {
                        this.OnMoveToTab?.Invoke(sid, capturedTab);
                    }
                };
                menuMoveToTab.DropDownItems.Add(tabItem);
            }

            // Pin/Unpin visibility
            if (!isMultiSelect)
            {
                bool isPinned = sessionId != null && this.IsSessionPinned != null && this.IsSessionPinned(sessionId);
                menuPinSession.Visible = !isPinned;
                menuUnpinSession.Visible = isPinned;
            }
            else
            {
                menuPinSession.Visible = true;
                menuUnpinSession.Visible = true;
            }
        };

        this.SessionGrid.ContextMenuStrip = gridContextMenu;
        gridContextMenu.ConstrainToParentScreen(this.SessionGrid);

        this.SessionGrid.CellMouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                if (!this.SessionGrid.Rows[e.RowIndex].Selected)
                {
                    this.SessionGrid.ClearSelection();
                    this.SessionGrid.Rows[e.RowIndex].Selected = true;
                    this.SessionGrid.CurrentCell = this.SessionGrid.Rows[e.RowIndex].Cells[0];
                }
            }
            else if (e.Button == MouseButtons.Right && e.RowIndex < 0)
            {
                this.SessionGrid.ClearSelection();
            }
        };

        this.SessionGrid.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                var hitTest = this.SessionGrid.HitTest(e.X, e.Y);
                if (hitTest.Type is not DataGridViewHitTestType.Cell and not DataGridViewHitTestType.RowHeader)
                {
                    this.SessionGrid.ClearSelection();
                }
            }
        };
    }

    /// <summary>
    /// Populates a unified IDE sub-menu with CWD/Repo Root folders and matched files.
    /// When CWD and Repo Root are the same, shows a single "(CWD / Repo Root)" entry.
    /// </summary>
    private void PopulateIdeUnifiedSubMenu(ToolStripMenuItem parentItem, IdeEntry ide, Image? icon)
    {
        parentItem.DropDownItems.Clear();

        var sid = this.GridVisuals.GetSelectedSessionId();
        if (sid == null || this.GetSessionPaths == null)
        {
            return;
        }

        var (cwd, gitRoot) = this.GetSessionPaths(sid);
        if (string.IsNullOrEmpty(cwd))
        {
            return;
        }

        bool sameRoot = !string.IsNullOrEmpty(gitRoot) &&
            string.Equals(Path.GetFullPath(cwd!), Path.GetFullPath(gitRoot!), StringComparison.OrdinalIgnoreCase);

        if (sameRoot || string.IsNullOrEmpty(gitRoot))
        {
            var label = sameRoot ? "📁 (CWD / Repo Root)" : "📁 (CWD)";
            var folderItem = new ToolStripMenuItem(label) { Image = icon?.Clone() as Image };
            folderItem.Click += (s, e) => this.OnOpenInIde?.Invoke(sid, ide, false);
            parentItem.DropDownItems.Add(folderItem);

            this.AddFileSearchResults(parentItem, ide, cwd!, sid, icon);
        }
        else
        {
            var cwdItem = new ToolStripMenuItem("📁 (CWD)") { Image = icon?.Clone() as Image };
            cwdItem.Click += (s, e) => this.OnOpenInIde?.Invoke(sid, ide, false);
            parentItem.DropDownItems.Add(cwdItem);

            this.AddFileSearchResults(parentItem, ide, cwd!, sid, icon);

            parentItem.DropDownItems.Add(new ToolStripSeparator());

            var repoItem = new ToolStripMenuItem("📁 (Repo Root)") { Image = icon?.Clone() as Image };
            repoItem.Click += (s, e) => this.OnOpenInIde?.Invoke(sid, ide, true);
            parentItem.DropDownItems.Add(repoItem);

            this.AddFileSearchResults(parentItem, ide, gitRoot!, sid, icon);
        }
    }

    private void AddFileSearchResults(ToolStripMenuItem parentItem, IdeEntry ide, string directory, string sid, Image? icon)
    {
        var files = IdeFileSearchService.Search(directory, ide.FilePattern, this._settings.IdeSearchIgnoredDirs);
        if (files.Count > 0)
        {
            parentItem.DropDownItems.Add(new ToolStripSeparator());
            foreach (var file in files)
            {
                var capturedFile = Path.Combine(directory, file);
                var fileItem = new ToolStripMenuItem(file) { Image = icon?.Clone() as Image };
                fileItem.Click += (s, e) => this.OnOpenInIdeFile?.Invoke(sid, ide, capturedFile);
                parentItem.DropDownItems.Add(fileItem);
            }
        }
    }

    /// <summary>
    /// Updates the tab titles with session counts.
    /// </summary>
    internal void UpdateTabCounts(Dictionary<string, int> countsByTab)
    {
        foreach (TabPage tab in this.SessionTabs.TabPages)
        {
            if (tab.Tag is string tabName)
            {
                var count = countsByTab.GetValueOrDefault(tabName);
                tab.Text = $"{tabName} ({count})";
            }
        }
    }

    /// <summary>
    /// Rebuilds session tabs from current settings. Preserves selected tab if still present.
    /// </summary>
    internal void BuildSessionTabs()
    {
        var previousTab = this.SessionTabs.SelectedTab?.Tag as string;
        this.SessionTabs.TabPages.Clear();

        foreach (var tabName in this._settings.SessionTabs)
        {
            var page = new TabPage(tabName) { Tag = tabName, UseVisualStyleBackColor = true };
            this.SessionTabs.TabPages.Add(page);
        }

        // Add the "+" tab for quick tab creation
        if (this._settings.SessionTabs.Count < this._settings.MaxSessionTabs)
        {
            var addPage = new TabPage("+") { ToolTipText = "Add a new tab", UseVisualStyleBackColor = true };
            this.SessionTabs.TabPages.Add(addPage);
        }

        // Restore selection or default to first tab
        if (previousTab != null)
        {
            foreach (TabPage page in this.SessionTabs.TabPages)
            {
                if (string.Equals(page.Tag as string, previousTab, StringComparison.OrdinalIgnoreCase))
                {
                    this.SessionTabs.SelectedTab = page;
                    break;
                }
            }
        }

        // Ensure the grid is parented on the selected tab.
        var targetTab = this.SessionTabs.SelectedTab ??
            (this.SessionTabs.TabPages.Count > 0 ? this.SessionTabs.TabPages[0] : null);
        if (targetTab != null && targetTab.Tag != null)
        {
            this._suppressColumnOrderSave = true;
            targetTab.Controls.Add(this.SessionGrid);
            this._suppressColumnOrderSave = false;
        }
    }

    private void PromptAddTab()
    {
        if (this._settings.SessionTabs.Count >= this._settings.MaxSessionTabs)
        {
            MessageBox.Show($"Maximum of {this._settings.MaxSessionTabs} tabs allowed.", "Limit Reached", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var name = SettingsVisuals.PromptInput("Add Tab", "Tab name (max 20 chars):", "");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        if (name.Length > 20)
        {
            name = name[..20];
        }

        if (this._settings.SessionTabs.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A tab with that name already exists.", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        this._settings.SessionTabs.Add(name);
        this._settings.Save();
        this.SessionTabs.SuspendLayout();
        this.BuildSessionTabs();

        foreach (TabPage page in this.SessionTabs.TabPages)
        {
            if (string.Equals(page.Tag as string, name, StringComparison.OrdinalIgnoreCase))
            {
                this.SessionTabs.SelectedTab = page;
                break;
            }
        }

        this.SessionTabs.ResumeLayout(true);
        this.OnTabChanged?.Invoke();
    }
}
