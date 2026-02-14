using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopilotApp.Models;
using CopilotApp.Services;

namespace CopilotApp.Forms;

/// <summary>
/// Main application form providing session management and settings configuration.
/// </summary>
[ExcludeFromCodeCoverage]
internal class MainForm : Form
{
    private readonly TabControl _mainTabs;
    private readonly TabPage _sessionsTab;
    private readonly TabPage _newSessionTab;
    private readonly TabPage _settingsTab;

    // Sessions tab controls
    private readonly TextBox _searchBox;
    private readonly DataGridView _sessionGrid;
    private readonly SessionGridController _gridController;
    private List<NamedSession> _cachedSessions = new();
    private readonly ActiveStatusTracker _activeTracker = new();
    private readonly Timer? _activeStatusTimer;

    // New Session tab controls
    private readonly ListView _cwdListView;
    private readonly Button _btnCreateWorkspace = null!;
    private readonly Dictionary<string, bool> _cwdGitStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly NewSessionTabBuilder _newSessionTabBuilder = new();
    private readonly SessionDataService _sessionDataService = new();

    // Settings tab controls
    private readonly ListBox _toolsList;
    private readonly ListBox _dirsList;
    private readonly ListView _idesList;
    private readonly TextBox _workDirBox;

    // Update banner
    private readonly LinkLabel _updateLabel;

    /// <summary>
    /// Gets the identifier of the currently selected session.
    /// </summary>
    public string? SelectedSessionId { get; private set; }

    /// <summary>
    /// Gets the working directory chosen from the New Session tab.
    /// </summary>
    public string? NewSessionCwd { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainForm"/> class.
    /// </summary>
    /// <param name="initialTab">The zero-based index of the tab to display on startup.</param>
    public MainForm(int initialTab = 0)
    {
        this.Text = "Copilot App";
        this.Size = new Size(1000, 550);
        this.MinimumSize = new Size(550, 400);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.Sizable;

        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null)
            {
                this.Icon = icon;
            }
        }
        catch { }

        this._mainTabs = new TabControl { Dock = DockStyle.Fill };

        // ===== Sessions Tab =====
        this._sessionsTab = new TabPage("Existing Sessions");

        this._sessionGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True, Padding = new Padding(4, 4, 4, 4) },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) },
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        this._sessionGrid.Columns.Add("Session", "Session");
        this._sessionGrid.Columns.Add("CWD", "CWD");
        this._sessionGrid.Columns.Add("Date", "Date");
        this._sessionGrid.Columns.Add("Active", "Active");
        this._sessionGrid.Columns["Session"]!.Width = 300;
        this._sessionGrid.Columns["Session"]!.MinimumWidth = 100;
        this._sessionGrid.Columns["CWD"]!.Width = 110;
        this._sessionGrid.Columns["CWD"]!.MinimumWidth = 60;
        this._sessionGrid.Columns["Date"]!.Width = 130;
        this._sessionGrid.Columns["Date"]!.MinimumWidth = 80;
        this._sessionGrid.Columns["Active"]!.Width = 100;
        this._sessionGrid.Columns["Active"]!.MinimumWidth = 60;

        // Adjust Session column to fill remaining space on form resize or column drag
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
                var otherWidth = this._sessionGrid.Columns["CWD"]!.Width
                    + this._sessionGrid.Columns["Date"]!.Width
                    + this._sessionGrid.Columns["Active"]!.Width
                    + (this._sessionGrid.RowHeadersVisible ? this._sessionGrid.RowHeadersWidth : 0)
                    + SystemInformation.VerticalScrollBarWidth + 2;
                var fill = this._sessionGrid.ClientSize.Width - otherWidth;
                if (fill >= this._sessionGrid.Columns["Session"]!.MinimumWidth)
                {
                    this._sessionGrid.Columns["Session"]!.Width = fill;
                }
            }
            finally { adjustingSessionWidth = false; }
        }
        this._sessionGrid.Resize += (s, e) => AdjustSessionColumnWidth();
        this._sessionGrid.ColumnWidthChanged += (s, e) =>
        {
            if (e.Column.Name != "Session")
            {
                AdjustSessionColumnWidth();
            }
        };

        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(5, 5, 5, 2) };
        var searchLabel = new Label { Text = "Search:", AutoSize = true, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft };
        var btnRefreshTop = new Button
        {
            Text = "Refresh",
            Width = 65,
            Dock = DockStyle.Right
        };
        btnRefreshTop.Click += async (s, e) =>
        {
            this._cachedSessions = await Task.Run(() => LoadNamedSessions()).ConfigureAwait(true);
            var snapshot = this._activeTracker.Refresh(this._cachedSessions);
            this._gridController.Populate(this._cachedSessions, snapshot, this._searchBox.Text);
        };
        this._searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Filter sessions..."
        };
        this._searchBox.TextChanged += (s, e) =>
        {
            // Re-filter cached sessions without reloading from disk
            var snapshot = this._activeTracker.Refresh(this._cachedSessions);
            this._gridController.Populate(this._cachedSessions, snapshot, this._searchBox.Text);
        };
        // Add Refresh and label first (Dock=Right/Left), then textbox fills remaining space
        searchPanel.Controls.Add(this._searchBox);
        searchPanel.Controls.Add(btnRefreshTop);
        searchPanel.Controls.Add(searchLabel);

        this._sessionGrid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0)
            {
                this.SelectedSessionId = this._sessionGrid.Rows[e.RowIndex].Tag as string;
                this.LaunchSession();
            }
        };

        this._gridController = new SessionGridController(this._sessionGrid, this._activeTracker);

        // Right-click context menu with Edit option
        var gridContextMenu = new ContextMenuStrip();
        var editMenuItem = new ToolStripMenuItem("Edit");
        editMenuItem.Click += async (s, e) =>
        {
            var sid = this._gridController.GetSelectedSessionId();
            if (sid == null)
            {
                return;
            }

            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session == null)
            {
                return;
            }

            var edited = SessionEditorForm.ShowEditor(session.Summary, session.Cwd);
            if (edited != null)
            {
                var sessionDir = Path.Combine(Program.SessionStateDir, sid);
                if (SessionService.UpdateSession(sessionDir, edited.Value.Summary, edited.Value.Cwd))
                {
                    this._cachedSessions = await Task.Run(() => LoadNamedSessions()).ConfigureAwait(true);
                    var snapshot = this._activeTracker.Refresh(this._cachedSessions);
                    this._gridController.Populate(this._cachedSessions, snapshot, this._searchBox.Text);
                }
            }
        };
        gridContextMenu.Items.Add(editMenuItem);
        this._sessionGrid.ContextMenuStrip = gridContextMenu;

        this._sessionGrid.CellMouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                this._sessionGrid.ClearSelection();
                this._sessionGrid.Rows[e.RowIndex].Selected = true;
                this._sessionGrid.CurrentCell = this._sessionGrid.Rows[e.RowIndex].Cells[0];
            }
        };

        var sessionButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5)
        };

        var btnOpen = new Button { Text = "Open ▾", Width = 80, Height = 28 };
        var openMenu = new ContextMenuStrip();

        var menuOpenSession = new ToolStripMenuItem("Open Session");
        menuOpenSession.Click += (s, e) =>
        {
            var sid = this._gridController.GetSelectedSessionId();
            if (sid != null)
            {
                this.SelectedSessionId = sid;
                this.LaunchSession();
            }
        };
        openMenu.Items.Add(menuOpenSession);

        var menuOpenNewSession = new ToolStripMenuItem("Open as New Copilot Session");
        menuOpenNewSession.Click += (s, e) =>
        {
            var sessionId = this._gridController.GetSelectedSessionId();
            if (sessionId != null)
            {
                var workspaceFile = Path.Combine(Program.SessionStateDir, sessionId, "workspace.yaml");
                string? selectedCwd = null;
                if (File.Exists(workspaceFile))
                {
                    foreach (var line in File.ReadLines(workspaceFile))
                    {
                        if (line.StartsWith("cwd:"))
                        {
                            selectedCwd = line.Substring("cwd:".Length).Trim();
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(selectedCwd))
                {
                    var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                    Process.Start(new ProcessStartInfo(exePath, selectedCwd) { UseShellExecute = false });
                }
            }
        };
        openMenu.Items.Add(menuOpenNewSession);

        var menuOpenNewSessionWorkspace = new ToolStripMenuItem("Open as New Copilot Session Workspace");
        menuOpenNewSessionWorkspace.Click += (s, e) =>
        {
            var sessionId = this._gridController.GetSelectedSessionId();
            if (sessionId != null)
            {
                var workspaceFile = Path.Combine(Program.SessionStateDir, sessionId, "workspace.yaml");
                string? selectedCwd = null;
                if (File.Exists(workspaceFile))
                {
                    foreach (var line in File.ReadLines(workspaceFile))
                    {
                        if (line.StartsWith("cwd:"))
                        {
                            selectedCwd = line.Substring("cwd:".Length).Trim();
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(selectedCwd))
                {
                    var gitRoot = SessionService.FindGitRoot(selectedCwd);
                    if (gitRoot != null)
                    {
                        var worktreePath = WorkspaceCreatorForm.ShowWorkspaceCreator(gitRoot);
                        if (worktreePath != null)
                        {
                            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                            Process.Start(new ProcessStartInfo(exePath, worktreePath) { UseShellExecute = false });
                        }
                    }
                }
            }
        };
        openMenu.Items.Add(menuOpenNewSessionWorkspace);

        openMenu.Items.Add(new ToolStripSeparator());

        var menuOpenTerminal = new ToolStripMenuItem("Open Terminal");
        menuOpenTerminal.Click += (s, e) =>
        {
            var sid = this._gridController.GetSelectedSessionId();
            if (sid == null)
            {
                return;
            }

            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session == null || string.IsNullOrEmpty(session.Cwd))
            {
                return;
            }

            var proc = TerminalLauncherService.LaunchTerminal(session.Cwd, sid);
            if (proc != null)
            {
                TerminalCacheService.CacheTerminal(Program.TerminalCacheFile, sid, proc.Id);
                this.RefreshActiveStatusAsync();
            }
        };
        openMenu.Items.Add(menuOpenTerminal);

        var ideRepoMenuItems = new List<ToolStripMenuItem>();
        if (Program._settings.Ides.Count > 0)
        {
            openMenu.Items.Add(new ToolStripSeparator());

            foreach (var ide in Program._settings.Ides)
            {
                var capturedIde = ide;

                var menuIdeCwd = new ToolStripMenuItem($"Open in {ide.Description} (CWD)");
                menuIdeCwd.Click += (s, e) =>
                {
                    var sid = this._gridController.GetSelectedSessionId();
                    if (sid == null)
                    {
                        return;
                    }

                    var session = this._cachedSessions.Find(x => x.Id == sid);
                    if (session == null || string.IsNullOrEmpty(session.Cwd))
                    {
                        return;
                    }

                    var proc = IdePickerForm.LaunchIde(capturedIde.Path, session.Cwd);
                    if (proc != null)
                    {
                        this._activeTracker.TrackProcess(sid, new ActiveProcess(capturedIde.Description, proc.Id, session.Cwd));
                        this.RefreshActiveStatusAsync();
                    }
                };
                openMenu.Items.Add(menuIdeCwd);

                var menuIdeRepo = new ToolStripMenuItem($"Open in {ide.Description} (Repo Root)");
                menuIdeRepo.Click += (s, e) =>
                {
                    var sid = this._gridController.GetSelectedSessionId();
                    if (sid == null)
                    {
                        return;
                    }

                    var session = this._cachedSessions.Find(x => x.Id == sid);
                    if (session == null || string.IsNullOrEmpty(session.Cwd))
                    {
                        return;
                    }

                    var repoRoot = SessionService.FindGitRoot(session.Cwd);
                    if (repoRoot == null)
                    {
                        return;
                    }

                    var proc = IdePickerForm.LaunchIde(capturedIde.Path, repoRoot);
                    if (proc != null)
                    {
                        this._activeTracker.TrackProcess(sid, new ActiveProcess(capturedIde.Description, proc.Id, repoRoot));
                        this.RefreshActiveStatusAsync();
                    }
                };
                openMenu.Items.Add(menuIdeRepo);
                ideRepoMenuItems.Add(menuIdeRepo);
            }
        }

        openMenu.Items.Add(new ToolStripSeparator());

        var menuOpenEdge = new ToolStripMenuItem("Open in Edge");
        menuOpenEdge.Click += async (s, e) =>
        {
            var sid = this._gridController.GetSelectedSessionId();
            if (sid == null)
            {
                return;
            }

            if (this._activeTracker.TryGetEdge(sid, out var existing) && existing.IsOpen)
            {
                existing.Focus();
                return;
            }

            var workspace = new EdgeWorkspaceService(sid);
            workspace.WindowClosed += () =>
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(() =>
                    {
                        this._activeTracker.RemoveEdge(sid);
                        this.RefreshActiveStatusAsync();
                    });
                }
                else
                {
                    this._activeTracker.RemoveEdge(sid);
                    this.RefreshActiveStatusAsync();
                }
            };
            this._activeTracker.TrackEdge(sid, workspace);

            await workspace.OpenAsync().ConfigureAwait(true);
            this.RefreshActiveStatusAsync();
        };
        openMenu.Items.Add(menuOpenEdge);

        openMenu.Opening += (s, e) =>
        {
            bool isGit = false;
            var sessionId = this._gridController.GetSelectedSessionId();
            if (sessionId != null)
            {
                var session = this._cachedSessions.Find(x => x.Id == sessionId);
                if (session != null && !string.IsNullOrEmpty(session.Cwd))
                {
                    var repoRoot = SessionService.FindGitRoot(session.Cwd);
                    isGit = repoRoot != null && !string.Equals(repoRoot, session.Cwd, StringComparison.OrdinalIgnoreCase);
                }
            }
            menuOpenNewSessionWorkspace.Visible = isGit;
            foreach (var item in ideRepoMenuItems)
            {
                item.Visible = isGit;
            }
        };

        btnOpen.Click += (s, e) =>
        {
            openMenu.Show(btnOpen, new Point(0, btnOpen.Height));
        };

        sessionButtonPanel.Controls.Add(btnOpen);

        this._sessionsTab.Controls.Add(this._sessionGrid);
        this._sessionsTab.Controls.Add(searchPanel);
        this._sessionsTab.Controls.Add(sessionButtonPanel);

        // ===== Settings Tab =====
        this._settingsTab = new TabPage("Settings");

        var settingsContainer = new Panel { Dock = DockStyle.Fill };

        var settingsTabs = new TabControl { Dock = DockStyle.Fill };

        // --- Allowed Tools tab ---
        var toolsTab = new TabPage("Allowed Tools");
        this._toolsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var tool in Program._settings.AllowedTools)
        {
            this._toolsList.Items.Add(tool);
        }

        var toolsButtons = SettingsTabBuilder.CreateListButtons(this._toolsList, "Tool name:", "Add Tool", addBrowse: false);
        toolsTab.Controls.Add(this._toolsList);
        toolsTab.Controls.Add(toolsButtons);

        // --- Allowed Directories tab ---
        var dirsTab = new TabPage("Allowed Directories");
        this._dirsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var dir in Program._settings.AllowedDirs)
        {
            this._dirsList.Items.Add(dir);
        }

        var dirsButtons = SettingsTabBuilder.CreateListButtons(this._dirsList, "Directory path:", "Add Directory", addBrowse: true);
        dirsTab.Controls.Add(this._dirsList);
        dirsTab.Controls.Add(dirsButtons);

        // --- IDEs tab ---
        var idesTab = new TabPage("IDEs");
        this._idesList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true
        };
        this._idesList.Columns.Add("Description", 200);
        this._idesList.Columns.Add("Path", 400);
        foreach (var ide in Program._settings.Ides)
        {
            var item = new ListViewItem(ide.Description);
            item.SubItems.Add(ide.Path);
            this._idesList.Items.Add(item);
        }

        var ideButtons = SettingsTabBuilder.CreateIdeButtons(this._idesList);
        idesTab.Controls.Add(this._idesList);
        idesTab.Controls.Add(ideButtons);

        settingsTabs.TabPages.Add(toolsTab);
        settingsTabs.TabPages.Add(dirsTab);
        settingsTabs.TabPages.Add(idesTab);

        // --- Default Work Dir ---
        var workDirPanel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 4) };
        var workDirLabel = new Label { Text = "Default Work Dir:", AutoSize = true, Location = new Point(8, 12) };
        this._workDirBox = new TextBox
        {
            Text = Program._settings.DefaultWorkDir,
            Location = new Point(130, 9),
            Width = 400,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        var workDirBrowse = new Button
        {
            Text = "...",
            Width = 30,
            Location = new Point(535, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        workDirBrowse.Click += (s, e) =>
        {
            using var fbd = new FolderBrowserDialog { SelectedPath = this._workDirBox.Text };
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                this._workDirBox.Text = fbd.SelectedPath;
            }
        };
        workDirPanel.Controls.AddRange([workDirLabel, this._workDirBox, workDirBrowse]);

        // --- Bottom buttons ---
        var settingsBottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 6, 8, 6)
        };

        var btnCancel = new Button { Text = "Cancel", Width = 90 };
        btnCancel.Click += (s, e) => SettingsTabBuilder.ReloadSettingsUI(this._toolsList, this._dirsList, this._idesList, this._workDirBox);

        var btnSave = new Button { Text = "Save", Width = 90 };
        btnSave.Click += (s, e) =>
        {
            Program._settings.AllowedTools = this._toolsList.Items.Cast<string>().ToList();
            Program._settings.AllowedDirs = this._dirsList.Items.Cast<string>().ToList();
            Program._settings.DefaultWorkDir = this._workDirBox.Text.Trim();
            Program._settings.Ides = [];
            foreach (ListViewItem item in this._idesList.Items)
            {
                Program._settings.Ides.Add(new IdeEntry { Description = item.Text, Path = item.SubItems[1].Text });
            }

            Program._settings.Save();
            MessageBox.Show("Settings saved.", "Copilot App", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        settingsBottomPanel.Controls.Add(btnCancel);
        settingsBottomPanel.Controls.Add(btnSave);

        settingsContainer.Controls.Add(settingsTabs);
        settingsContainer.Controls.Add(workDirPanel);
        settingsContainer.Controls.Add(settingsBottomPanel);

        this._settingsTab.Controls.Add(settingsContainer);

        // ===== New Session Tab =====
        this._newSessionTab = new TabPage("New Session");

        this._cwdListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true
        };
        this._cwdListView.Columns.Add("Directory", 350);
        this._cwdListView.Columns.Add("# Sessions created ▼", 120, HorizontalAlignment.Center);
        this._cwdListView.Columns.Add("Git", 50, HorizontalAlignment.Center);
        this._cwdListView.ListViewItemSorter = this._newSessionTabBuilder.Sorter;
        this._cwdListView.ColumnClick += (s, e) => this._newSessionTabBuilder.OnColumnClick(this._cwdListView, e);

        this._cwdListView.DoubleClick += (s, e) =>
        {
            if (this._cwdListView.SelectedItems.Count > 0)
            {
                this.NewSessionCwd = this._cwdListView.SelectedItems[0].Tag as string;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        };

        this._cwdListView.SelectedIndexChanged += (s, e) =>
        {
            if (this._cwdListView.SelectedItems.Count > 0
                && this._cwdListView.SelectedItems[0].Tag is string path
                && this._cwdGitStatus.TryGetValue(path, out bool isGit))
            {
                this._btnCreateWorkspace.Enabled = isGit;
            }
            else
            {
                this._btnCreateWorkspace.Enabled = false;
            }
        };

        var newSessionButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5)
        };

        var btnNsCancel = new Button { Text = "Cancel", Width = 80 };
        btnNsCancel.Click += (s, e) => this.Close();

        var btnNsStart = new Button { Text = "Start", Width = 80 };
        btnNsStart.Click += (s, e) =>
        {
            if (this._cwdListView.SelectedItems.Count > 0)
            {
                this.NewSessionCwd = this._cwdListView.SelectedItems[0].Tag as string;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        };

        var btnNsBrowse = new Button { Text = "Browse...", Width = 90 };
        btnNsBrowse.Click += (s, e) =>
        {
            using var fbd = new FolderBrowserDialog { SelectedPath = Program._settings.DefaultWorkDir };
            if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(fbd.SelectedPath))
            {
                this.NewSessionCwd = fbd.SelectedPath;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        };

        this._btnCreateWorkspace = new Button { Text = "Create Workspace", Width = 130, Enabled = false };
        this._btnCreateWorkspace.Click += (s, e) =>
        {
            if (this._cwdListView.SelectedItems.Count > 0
                && this._cwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                var gitRoot = SessionService.FindGitRoot(selectedCwd);
                if (gitRoot != null)
                {
                    var worktreePath = WorkspaceCreatorForm.ShowWorkspaceCreator(gitRoot);
                    if (worktreePath != null)
                    {
                        this.NewSessionCwd = worktreePath;
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                }
            }
        };

        newSessionButtonPanel.Controls.Add(btnNsCancel);
        newSessionButtonPanel.Controls.Add(btnNsStart);
        newSessionButtonPanel.Controls.Add(btnNsBrowse);
        newSessionButtonPanel.Controls.Add(this._btnCreateWorkspace);

        this._newSessionTab.Controls.Add(this._cwdListView);
        this._newSessionTab.Controls.Add(newSessionButtonPanel);

        // ===== Add tabs to main control =====
        this._mainTabs.TabPages.Add(this._newSessionTab);
        this._mainTabs.TabPages.Add(this._sessionsTab);
        this._mainTabs.TabPages.Add(this._settingsTab);

        // ===== Update banner =====
        this._updateLabel = new LinkLabel
        {
            Dock = DockStyle.Bottom,
            TextAlign = ContentAlignment.MiddleCenter,
            Height = 28,
            Visible = false,
            Padding = new Padding(0, 4, 0, 4)
        };
        this._updateLabel.LinkClicked += this.OnUpdateLabelClickedAsync;

        this.Controls.Add(this._mainTabs);
        this.Controls.Add(this._updateLabel);

        if (initialTab >= 0 && initialTab < this._mainTabs.TabPages.Count)
        {
            this._mainTabs.SelectedIndex = initialTab;
        }

        this._activeTracker.OnEdgeWorkspaceClosed = (sid) =>
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(() =>
                {
                    this._activeTracker.RemoveEdge(sid);
                    this.RefreshActiveStatusAsync();
                });
            }
            else
            {
                this._activeTracker.RemoveEdge(sid);
                this.RefreshActiveStatusAsync();
            }
        };

        this._activeStatusTimer = new Timer { Interval = 3000 };
        this._activeStatusTimer.Tick += (s, e) => this.RefreshActiveStatusAsync();
        this._activeStatusTimer.Start();

        this.Shown += async (s, e) =>
        {
            await this.LoadInitialDataAsync().ConfigureAwait(true);
            _ = this.CheckForUpdateInBackgroundAsync();
        };
        this.FormClosed += (s, e) => this._activeStatusTimer?.Stop();
    }

    private async Task CheckForUpdateInBackgroundAsync()
    {
        var update = await UpdateService.CheckForUpdateAsync().ConfigureAwait(false);
        if (update?.InstallerUrl != null)
        {
            this.Invoke(() =>
            {
                this._updateLabel.Text = $"\u2B06 Update available: {update.TagName} \u2014 Click to install";
                this._updateLabel.Tag = update.InstallerUrl;
                this._updateLabel.Visible = true;
            });
        }
    }

    private async void OnUpdateLabelClickedAsync(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (this._updateLabel.Tag is not string url)
        {
            return;
        }

        this._updateLabel.Enabled = false;
        this._updateLabel.Text = "\u2B07 Downloading update...";

        try
        {
            await UpdateService.DownloadAndLaunchInstallerAsync(url).ConfigureAwait(false);
            this.Invoke(() => Application.Exit());
        }
        catch (Exception ex)
        {
            this.Invoke(() =>
            {
                this._updateLabel.Text = $"\u26A0 Download failed: {ex.Message}";
                this._updateLabel.Enabled = true;
            });
        }
    }

    private void LaunchSession()
    {
        if (this.SelectedSessionId != null)
        {
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            Process.Start(new ProcessStartInfo(exePath, $"--resume {this.SelectedSessionId}")
            {
                UseShellExecute = false
            });
        }
    }

    /// <summary>
    /// Switches the main tab control to the specified tab and brings the form to the foreground.
    /// </summary>
    /// <param name="tabIndex">The zero-based index of the tab to activate.</param>
    public async void SwitchToTabAsync(int tabIndex)
    {
        if (tabIndex >= 0 && tabIndex < this._mainTabs.TabPages.Count)
        {
            this._mainTabs.SelectedIndex = tabIndex;
        }

        if (tabIndex == 0)
        {
            this.RefreshNewSessionList();
        }

        if (tabIndex == 1)
        {
            this._cachedSessions = await Task.Run(() => LoadNamedSessions()).ConfigureAwait(true);
            var snapshot = this._activeTracker.Refresh(this._cachedSessions);
            this._gridController.Populate(this._cachedSessions, snapshot, this._searchBox.Text);
        }

        if (this.WindowState == FormWindowState.Minimized)
        {
            this.WindowState = FormWindowState.Normal;
        }

        this.BringToFront();
        this.Activate();
    }

    private bool _refreshInProgress;

    private async void RefreshActiveStatusAsync()
    {
        if (this._refreshInProgress)
        {
            return;
        }

        this._refreshInProgress = true;
        try
        {
            var sessions = await Task.Run(() => LoadNamedSessions()).ConfigureAwait(true);
            this._cachedSessions = sessions;
            var snapshot = await Task.Run(() => this._activeTracker.Refresh(this._cachedSessions)).ConfigureAwait(true);
            this._gridController.ApplySnapshot(this._cachedSessions, snapshot, this._searchBox.Text);
        }
        finally
        {
            this._refreshInProgress = false;
        }
    }

    private async Task LoadInitialDataAsync()
    {
        var sessions = await Task.Run(() => LoadNamedSessions()).ConfigureAwait(true);
        this._cachedSessions = sessions;
        var snapshot = this._activeTracker.Refresh(this._cachedSessions);
        this._gridController.Populate(this._cachedSessions, snapshot, this._searchBox.Text);

        this.RefreshNewSessionList();
    }

    private void RefreshNewSessionList()
    {
        var data = this._sessionDataService.LoadAll(Program.SessionStateDir, Program.PidRegistryFile);
        this._newSessionTabBuilder.Populate(this._cwdListView, this._cwdGitStatus, data);
    }

    /// <summary>
    /// Loads all named sessions from the default session state directory.
    /// </summary>
    /// <returns>A list of named sessions.</returns>
    internal static List<NamedSession> LoadNamedSessions() => SessionService.LoadNamedSessions(Program.SessionStateDir, Program.PidRegistryFile);

    /// <summary>
    /// Loads all named sessions from the specified session state directory.
    /// </summary>
    /// <param name="sessionStateDir">The directory containing session state data.</param>
    /// <returns>A list of named sessions.</returns>
    internal static List<NamedSession> LoadNamedSessions(string sessionStateDir) => SessionService.LoadNamedSessions(sessionStateDir);
}
