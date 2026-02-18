using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
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
    internal TextBox SearchBox = null!;
    internal DataGridView SessionGrid = null!;
    internal SessionGridVisuals GridVisuals = null!;
    internal Label LoadingOverlay = null!;
    internal TabControl SessionTabs = null!;
    internal TabPage ActiveTab = null!;
    internal TabPage ArchivedTab = null!;
    internal Button NewSessionButton = null!;

    /// <summary>
    /// Gets whether the archived tab is currently selected.
    /// </summary>
    internal bool IsArchivedTabSelected => this.SessionTabs.SelectedTab == this.ArchivedTab;

    /// <summary>Fired when the user clicks Refresh.</summary>
    internal event Action? OnRefreshRequested;

    /// <summary>Fired when the user double-clicks a session row. Arg = session id.</summary>
    internal event Action<string>? OnSessionDoubleClicked;

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
    internal event Action<string>? OnDeleteSession;
    internal event Action<string>? OnOpenFilesFolder;
    internal event Action<string>? OnOpenPlan;
    internal event Action<string>? OnOpenCwdExplorer;
    internal event Action<string>? OnArchiveSession;
    internal event Action<string>? OnUnarchiveSession;
    internal event Action<string>? OnPinSession;
    internal event Action<string>? OnUnpinSession;

    /// <summary>
    /// Fired for IDE context-menu clicks.
    /// Args: sessionId, IDE entry, useRepoRoot.
    /// </summary>
    internal event Action<string, IdeEntry, bool>? OnOpenInIde;

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
    /// Callback to determine if a session is archived.
    /// </summary>
    internal Func<string, bool>? IsSessionArchived;

    /// <summary>
    /// Callback to determine if a session is pinned.
    /// </summary>
    internal Func<string, bool>? IsSessionPinned;

    /// <summary>
    /// Callback to determine if a session has an open Edge workspace.
    /// </summary>
    internal Func<string, bool>? IsEdgeOpen;

    internal ExistingSessionsVisuals(Control parentControl, ActiveStatusTracker activeTracker)
    {
        this.InitializeSessionGrid();
        var searchPanel = this.BuildSearchPanel();
        this.GridVisuals = new SessionGridVisuals(this.SessionGrid, activeTracker);
        this.BuildGridContextMenu();

        // Sub-tabs: Active / Archived
        this.ActiveTab = new TabPage("Active");
        this.ArchivedTab = new TabPage("Archived");
        this.SessionTabs = new TabControl { Dock = DockStyle.Fill };
        if (!Application.IsDarkModeEnabled)
        {
            this.SessionTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            this.SessionTabs.DrawItem += (s, e) =>
            {
                bool selected = e.Index == this.SessionTabs.SelectedIndex;
                var back = selected ? SystemColors.Window : Color.FromArgb(220, 220, 220);
                var fore = SystemColors.ControlText;
                using var brush = new SolidBrush(back);
                e.Graphics.FillRectangle(brush, e.Bounds);
                var text = this.SessionTabs.TabPages[e.Index].Text;
                TextRenderer.DrawText(e.Graphics, text, this.SessionTabs.Font, e.Bounds, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }
        this.SessionTabs.TabPages.Add(this.ActiveTab);
        this.SessionTabs.TabPages.Add(this.ArchivedTab);
        this.ActiveTab.Controls.Add(this.SessionGrid);
        this.SessionTabs.SelectedIndexChanged += (s, e) =>
        {
            // Move the grid to the newly selected tab
            var selectedTab = this.SessionTabs.SelectedTab!;
            selectedTab.Controls.Add(this.SessionGrid);
            this.OnTabChanged?.Invoke();
        };

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
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            RowHeadersVisible = false,
            BorderStyle = BorderStyle.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            CellBorderStyle = DataGridViewCellBorderStyle.Single,
            GridColor = Application.IsDarkModeEnabled ? Color.FromArgb(80, 80, 80) : SystemColors.ControlLight,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                WrapMode = DataGridViewTriState.True,
                Padding = new Padding(4, 4, 4, 4),
                SelectionBackColor = Application.IsDarkModeEnabled ? Color.FromArgb(0x11, 0x11, 0x11) : Color.FromArgb(200, 220, 245),
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
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        this.SessionGrid.Columns.Add("Session", "Session");
        this.SessionGrid.Columns.Add("CWD", "CWD");
        this.SessionGrid.Columns.Add("Date", "Date");
        var runningAppsCol = new DataGridViewTextBoxColumn
        {
            Name = "RunningApps",
            HeaderText = "Running",
            ToolTipText = "Applications running in session context",
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
        };
        runningAppsCol.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        this.SessionGrid.Columns.Add(runningAppsCol);
        this.SessionGrid.Columns["Session"]!.Width = 300;
        this.SessionGrid.Columns["Session"]!.MinimumWidth = 100;
        this.SessionGrid.Columns["CWD"]!.Width = 110;
        this.SessionGrid.Columns["CWD"]!.MinimumWidth = 60;
        this.SessionGrid.Columns["Date"]!.Width = 160;
        this.SessionGrid.Columns["Date"]!.MinimumWidth = 100;
        this.SessionGrid.Columns["RunningApps"]!.Width = 110;
        this.SessionGrid.Columns["RunningApps"]!.MinimumWidth = 60;

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
                var otherWidth = this.SessionGrid.Columns["Status"]!.Width
                    + this.SessionGrid.Columns["CWD"]!.Width
                    + this.SessionGrid.Columns["Date"]!.Width
                    + this.SessionGrid.Columns["RunningApps"]!.Width
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
            if (e.Column.Name != "Session")
            {
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
                var glyphY = e.CellBounds.Top + (e.CellBounds.Height - 8) / 2;
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

        this.NewSessionButton = new Button
        {
            Text = "New Session",
            Width = 100,
            Height = 27,
            Location = new Point(5, 3)
        };
        this.NewSessionButton.Click += (s, e) => this.OnNewSessionClicked?.Invoke();

        var searchLabel = new Label
        {
            Text = "Search:",
            AutoSize = true,
            Location = new Point(112, 9)
        };
        var btnRefreshTop = new Button
        {
            Text = "Refresh",
            Width = 65,
            Height = 27,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(searchPanel.Width - 105, 3)
        };
        searchPanel.Resize += (s, e) => btnRefreshTop.Left = searchPanel.ClientSize.Width - 105;
        btnRefreshTop.Click += (s, e) => this.OnRefreshRequested?.Invoke();

        var btnSettings = new Button
        {
            Text = "⚙",
            Width = 32,
            Height = 27,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(searchPanel.Width - 37, 3),
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 12f),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnSettings.FlatAppearance.BorderSize = 0;
        searchPanel.Resize += (s, e) => btnSettings.Left = searchPanel.ClientSize.Width - 37;
        btnSettings.Click += (s, e) => this.OnSettingsClicked?.Invoke();
        this.SearchBox = new TextBox
        {
            Location = new Point(162, 4),
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
        searchPanel.Resize += (s, e) => searchBorder.Width = searchPanel.ClientSize.Width - 275;
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
        searchPanel.Controls.Add(btnSettings);
        searchPanel.Controls.Add(btnRefreshTop);
        searchPanel.Controls.Add(searchLabel);
        searchPanel.Controls.Add(this.NewSessionButton);
        return searchPanel;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Bitmap? TryExtractIcon(string filePath, int index)
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

    private static Bitmap? TryGetExeIcon(string exePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                return TryExtractIcon(exePath, 0);
            }
        }
        catch { /* ignore icon extraction failures */ }
        return null;
    }

    private void BuildGridContextMenu()
    {
        var gridContextMenu = new ContextMenuStrip();
        var shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
        var imageres = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "imageres.dll");
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

        // --- Session operations (top group) ---
        var menuOpenSession = new ToolStripMenuItem("Open Session") { Image = TryExtractIcon(shell32, 137) };
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

        var menuPinSession = new ToolStripMenuItem("Pin Session") { Image = TryExtractIcon(shell32, 173) };
        menuPinSession.Click += (s, e) =>
        {
            foreach (var sid in this.GridVisuals.GetSelectedSessionIds())
            {
                this.OnPinSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuPinSession);

        var menuUnpinSession = new ToolStripMenuItem("Unpin Session") { Image = TryExtractIcon(shell32, 173) };
        menuUnpinSession.Click += (s, e) =>
        {
            foreach (var sid in this.GridVisuals.GetSelectedSessionIds())
            {
                this.OnUnpinSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuUnpinSession);

        var menuArchiveSession = new ToolStripMenuItem("Archive Session") { Image = TryExtractIcon(shell32, 145) };
        menuArchiveSession.Click += (s, e) =>
        {
            foreach (var sid in this.GridVisuals.GetSelectedSessionIds())
            {
                this.OnArchiveSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuArchiveSession);

        var menuUnarchiveSession = new ToolStripMenuItem("Unarchive Session") { Image = TryExtractIcon(shell32, 145) };
        menuUnarchiveSession.Click += (s, e) =>
        {
            foreach (var sid in this.GridVisuals.GetSelectedSessionIds())
            {
                this.OnUnarchiveSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuUnarchiveSession);

        var menuDeleteSession = new ToolStripMenuItem("Delete Session") { Image = TryExtractIcon(shell32, 131) };
        menuDeleteSession.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnDeleteSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuDeleteSession);

        // --- New session operations ---
        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuOpenNewSession = new ToolStripMenuItem("Open as New Copilot Session") { Image = TryExtractIcon(shell32, 1) };
        menuOpenNewSession.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenAsNewSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenNewSession);

        var menuOpenNewSessionWorkspace = new ToolStripMenuItem("Open as New Copilot Session Workspace") { Image = TryExtractIcon(shell32, 1) };
        menuOpenNewSessionWorkspace.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenAsNewSessionWorkspace?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenNewSessionWorkspace);

        // --- Terminal ---
        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuOpenTerminal = new ToolStripMenuItem("Open Terminal") { Image = TryExtractIcon(cmdExe, 0) };
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
        var ideRepoMenuItems = new List<ToolStripMenuItem>();

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

        if (Program._settings.Ides.Count > 0)
        {
            foreach (var ide in Program._settings.Ides)
            {
                var capturedIde = ide;
                var ideIcon = TryGetExeIcon(ide.Path);

                var menuIdeCwd = new ToolStripMenuItem($"Open in {ide.Description} (CWD)") { Image = ideIcon };
                menuIdeCwd.Click += (s, e) =>
                {
                    var sid = this.GridVisuals.GetSelectedSessionId();
                    if (sid != null)
                    {
                        this.OnOpenInIde?.Invoke(sid, capturedIde, false);
                    }
                };
                gridContextMenu.Items.Add(menuIdeCwd);

                var menuIdeRepo = new ToolStripMenuItem($"Open in {ide.Description} (Repo Root)") { Image = ideIcon?.Clone() as Image };
                menuIdeRepo.Click += (s, e) =>
                {
                    var sid = this.GridVisuals.GetSelectedSessionId();
                    if (sid != null)
                    {
                        this.OnOpenInIde?.Invoke(sid, capturedIde, true);
                    }
                };
                gridContextMenu.Items.Add(menuIdeRepo);
                ideRepoMenuItems.Add(menuIdeRepo);
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

        var menuSaveEdgeTabs = new ToolStripMenuItem("Save Edge State") { Image = TryExtractIcon(imageres, 67) };
        menuSaveEdgeTabs.ToolTipText = "Saves all open Edge tab URLs so they can be restored next time you open Edge for this session";
        menuSaveEdgeTabs.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnSaveEdgeTabs?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuSaveEdgeTabs);

        // --- Files ---
        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuOpenFilesFolder = new ToolStripMenuItem("Open Files") { Image = TryExtractIcon(shell32, 4) };
        menuOpenFilesFolder.ToolTipText = "Open artifacts folder dedicated to this session";
        menuOpenFilesFolder.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenFilesFolder?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenFilesFolder);

        var menuOpenPlan = new ToolStripMenuItem("Open Copilot Plan.md") { Image = TryExtractIcon(shell32, 70) };
        menuOpenPlan.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenPlan?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenPlan);

        gridContextMenu.Opening += (s, e) =>
        {
            var selectedIds = this.GridVisuals.GetSelectedSessionIds();
            bool isMultiSelect = selectedIds.Count > 1;

            // Single-select only items — disabled in multi-select
            menuOpenSession.Enabled = !isMultiSelect;
            editMenuItem.Enabled = !isMultiSelect;
            menuOpenNewSession.Enabled = !isMultiSelect;
            menuOpenNewSessionWorkspace.Enabled = !isMultiSelect;
            menuOpenTerminal.Enabled = !isMultiSelect;
            menuOpenEdge.Enabled = !isMultiSelect;
            menuSaveEdgeTabs.Enabled = !isMultiSelect;
            menuOpenCwdExplorer.Enabled = !isMultiSelect;
            menuOpenFilesFolder.Enabled = !isMultiSelect;
            menuOpenPlan.Enabled = !isMultiSelect;
            menuDeleteSession.Enabled = !isMultiSelect;
            foreach (var item in ideRepoMenuItems)
            {
                item.Enabled = !isMultiSelect;
            }

            // IDE CWD items — disable in multi-select
            foreach (ToolStripItem item in gridContextMenu.Items)
            {
                if (item is ToolStripMenuItem mi && mi.Text is string text && text.StartsWith("Open in ") && text.EndsWith("(CWD)") && mi != menuOpenCwdExplorer)
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
            foreach (var item in ideRepoMenuItems)
            {
                item.Visible = !isMultiSelect || isSubfolder;
            }

            // Plan visibility
            bool hasPlan = !isMultiSelect && sessionId != null && this.HasPlanFile != null && this.HasPlanFile(sessionId);
            menuOpenPlan.Visible = hasPlan;

            // Save Edge Tabs — only visible when Edge is open for this session
            bool edgeOpen = !isMultiSelect && sessionId != null && this.IsEdgeOpen != null && this.IsEdgeOpen(sessionId);
            menuSaveEdgeTabs.Visible = edgeOpen;

            // Archive/Unarchive visibility — respect current tab context
            if (isMultiSelect)
            {
                bool onArchivedTab = this.IsArchivedTabSelected;
                menuArchiveSession.Visible = !onArchivedTab;
                menuUnarchiveSession.Visible = onArchivedTab;
                menuPinSession.Visible = true;
                menuUnpinSession.Visible = true;
            }
            else
            {
                bool isArchived = sessionId != null && this.IsSessionArchived != null && this.IsSessionArchived(sessionId);
                menuArchiveSession.Visible = !isArchived;
                menuUnarchiveSession.Visible = isArchived;

                bool isPinned = sessionId != null && this.IsSessionPinned != null && this.IsSessionPinned(sessionId);
                menuPinSession.Visible = !isPinned;
                menuUnpinSession.Visible = isPinned;
            }
        };

        this.SessionGrid.ContextMenuStrip = gridContextMenu;

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
        };
    }

    /// <summary>
    /// Updates the tab titles with session counts.
    /// </summary>
    internal void UpdateTabCounts(int activeCount, int archivedCount)
    {
        this.ActiveTab.Text = $"Active ({activeCount})";
        this.ArchivedTab.Text = $"Archived ({archivedCount})";
    }
}
