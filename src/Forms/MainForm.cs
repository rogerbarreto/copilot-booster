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
    private TextBox _searchBox = null!;
    private DataGridView _sessionGrid = null!;
    private SessionGridController _gridController = null!;
    private Label _loadingOverlay = null!;
    private List<NamedSession> _cachedSessions = new();
    private readonly ActiveStatusTracker _activeTracker = new();
    private Timer? _activeStatusTimer;

    // New Session tab controls
    private ListView _cwdListView = null!;
    private Label _newSessionLoadingOverlay = null!;
    private readonly Dictionary<string, bool> _cwdGitStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly NewSessionTabBuilder _newSessionTabBuilder = new();
    private readonly SessionDataService _sessionDataService = new();

    // Settings tab controls
    private ListBox _toolsList = null!;
    private ListBox _dirsList = null!;
    private ListView _idesList = null!;
    private TextBox _workDirBox = null!;

    // Update banner
    private LinkLabel _updateLabel = null!;

    /// <summary>
    /// Gets the identifier of the currently selected session.
    /// </summary>
    public string? SelectedSessionId { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainForm"/> class.
    /// </summary>
    /// <param name="initialTab">The zero-based index of the tab to display on startup.</param>
    public MainForm(int initialTab = 0)
    {
        this.InitializeFormProperties();

        this._mainTabs = new TabControl { Dock = DockStyle.Fill };
        this._sessionsTab = new TabPage("Existing Sessions");
        this._settingsTab = new TabPage("Settings");
        this._newSessionTab = new TabPage("New Session");

        this.BuildSessionsTab();
        this.BuildSettingsTab();
        this.BuildNewSessionTab();
        this.SetupUpdateBanner();

        this._mainTabs.TabPages.Add(this._newSessionTab);
        this._mainTabs.TabPages.Add(this._sessionsTab);
        this._mainTabs.TabPages.Add(this._settingsTab);
        this.Controls.Add(this._mainTabs);
        this.Controls.Add(this._updateLabel);

        this.SetupTimersAndEvents(initialTab);
    }

    private void InitializeFormProperties()
    {
        this.Text = "Copilot Booster";
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
    }

    private void BuildSessionsTab()
    {
        this.InitializeSessionGrid();
        var searchPanel = this.BuildSearchPanel();
        this._gridController = new SessionGridController(this._sessionGrid, this._activeTracker);
        this.BuildGridContextMenu();

        this._loadingOverlay = new Label
        {
            Text = "Loading sessions...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14f, FontStyle.Regular),
            ForeColor = SystemColors.GrayText,
            BackColor = SystemColors.Window
        };

        this._sessionsTab.Controls.Add(this._loadingOverlay);
        this._loadingOverlay.BringToFront();
        this._sessionsTab.Controls.Add(this._sessionGrid);
        this._sessionsTab.Controls.Add(searchPanel);
    }

    private void InitializeSessionGrid()
    {
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

        this._sessionGrid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0)
            {
                this.SelectedSessionId = this._sessionGrid.Rows[e.RowIndex].Tag as string;
                this.LaunchSession();
            }
        };
    }

    private Panel BuildSearchPanel()
    {
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
            var snapshot = this._activeTracker.Refresh(this._cachedSessions);
            this._gridController.Populate(this._cachedSessions, snapshot, this._searchBox.Text);
        };
        searchPanel.Controls.Add(this._searchBox);
        searchPanel.Controls.Add(btnRefreshTop);
        searchPanel.Controls.Add(searchLabel);
        return searchPanel;
    }

    private void BuildGridContextMenu()
    {
        var gridContextMenu = new ContextMenuStrip();

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
        gridContextMenu.Items.Add(menuOpenSession);

        var editMenuItem = new ToolStripMenuItem("Edit Session");
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

        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuOpenNewSession = new ToolStripMenuItem("Open as New Copilot Session");
        menuOpenNewSession.Click += async (s, e) =>
        {
            var selectedSessionId = this._gridController.GetSelectedSessionId();
            if (selectedSessionId != null)
            {
                var workspaceFile = Path.Combine(Program.SessionStateDir, selectedSessionId, "workspace.yaml");
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
                    var sessionName = NewSessionNameForm.ShowNamePrompt();
                    if (sessionName == null)
                    {
                        return;
                    }

                    var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(selectedCwd, sessionName).ConfigureAwait(true);
                    if (newSessionId != null)
                    {
                        var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                        Process.Start(new ProcessStartInfo(exePath, $"--resume {newSessionId}") { UseShellExecute = false });
                    }
                    else
                    {
                        MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        };
        gridContextMenu.Items.Add(menuOpenNewSession);

        var menuOpenNewSessionWorkspace = new ToolStripMenuItem("Open as New Copilot Session Workspace");
        menuOpenNewSessionWorkspace.Click += async (s, e) =>
        {
            var selectedSessionId = this._gridController.GetSelectedSessionId();
            if (selectedSessionId != null)
            {
                var workspaceFile = Path.Combine(Program.SessionStateDir, selectedSessionId, "workspace.yaml");
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
                        var wsResult = WorkspaceCreatorForm.ShowWorkspaceCreator(gitRoot);
                        if (wsResult != null)
                        {
                            var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(wsResult.Value.WorktreePath, wsResult.Value.SessionName).ConfigureAwait(true);
                            if (newSessionId != null)
                            {
                                var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                                Process.Start(new ProcessStartInfo(exePath, $"--resume {newSessionId}") { UseShellExecute = false });
                            }
                            else
                            {
                                MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                }
            }
        };
        gridContextMenu.Items.Add(menuOpenNewSessionWorkspace);

        gridContextMenu.Items.Add(new ToolStripSeparator());

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
        gridContextMenu.Items.Add(menuOpenTerminal);

        var ideRepoMenuItems = new List<ToolStripMenuItem>();
        if (Program._settings.Ides.Count > 0)
        {
            gridContextMenu.Items.Add(new ToolStripSeparator());

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
                gridContextMenu.Items.Add(menuIdeCwd);

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
                gridContextMenu.Items.Add(menuIdeRepo);
                ideRepoMenuItems.Add(menuIdeRepo);
            }
        }

        gridContextMenu.Items.Add(new ToolStripSeparator());

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
        gridContextMenu.Items.Add(menuOpenEdge);

        gridContextMenu.Opening += (s, e) =>
        {
            bool hasGitRoot = false;
            bool isSubfolder = false;
            var sessionId = this._gridController.GetSelectedSessionId();
            if (sessionId != null)
            {
                var session = this._cachedSessions.Find(x => x.Id == sessionId);
                if (session != null && !string.IsNullOrEmpty(session.Cwd))
                {
                    var repoRoot = SessionService.FindGitRoot(session.Cwd);
                    hasGitRoot = repoRoot != null;
                    isSubfolder = hasGitRoot && !string.Equals(repoRoot, session.Cwd, StringComparison.OrdinalIgnoreCase);
                }
            }
            menuOpenNewSessionWorkspace.Visible = hasGitRoot;
            foreach (var item in ideRepoMenuItems)
            {
                item.Visible = isSubfolder;
            }
        };

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
    }

    private void BuildSettingsTab()
    {
        var settingsContainer = new Panel { Dock = DockStyle.Fill };
        var settingsTabs = new TabControl { Dock = DockStyle.Fill };

        // Allowed Tools
        var toolsTab = new TabPage("Allowed Tools");
        this._toolsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var tool in Program._settings.AllowedTools)
        {
            this._toolsList.Items.Add(tool);
        }
        var toolsButtons = SettingsTabBuilder.CreateListButtons(this._toolsList, "Tool name:", "Add Tool", addBrowse: false);
        toolsTab.Controls.Add(this._toolsList);
        toolsTab.Controls.Add(toolsButtons);

        // Allowed Directories
        var dirsTab = new TabPage("Allowed Directories");
        this._dirsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var dir in Program._settings.AllowedDirs)
        {
            this._dirsList.Items.Add(dir);
        }
        var dirsButtons = SettingsTabBuilder.CreateListButtons(this._dirsList, "Directory path:", "Add Directory", addBrowse: true);
        dirsTab.Controls.Add(this._dirsList);
        dirsTab.Controls.Add(dirsButtons);

        // IDEs
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

        // Default Work Dir
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

        // Bottom buttons
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
            MessageBox.Show("Settings saved.", "Copilot Booster", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        settingsBottomPanel.Controls.Add(btnCancel);
        settingsBottomPanel.Controls.Add(btnSave);

        settingsContainer.Controls.Add(settingsTabs);
        settingsContainer.Controls.Add(workDirPanel);
        settingsContainer.Controls.Add(settingsBottomPanel);

        this._settingsTab.Controls.Add(settingsContainer);
    }

    private void BuildNewSessionTab()
    {
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

        // Right-click context menu
        var cwdContextMenu = new ContextMenuStrip();

        var menuNewSession = new ToolStripMenuItem("New Copilot Session");
        menuNewSession.Click += async (s, e) =>
        {
            if (this._cwdListView.SelectedItems.Count > 0
                && this._cwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                var sessionName = NewSessionNameForm.ShowNamePrompt();
                if (sessionName == null)
                {
                    return;
                }

                var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(selectedCwd, sessionName).ConfigureAwait(true);
                if (newSessionId != null)
                {
                    var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                    Process.Start(new ProcessStartInfo(exePath, $"--resume {newSessionId}") { UseShellExecute = false });
                }
                else
                {
                    MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        };
        cwdContextMenu.Items.Add(menuNewSession);

        var menuNewSessionWorkspace = new ToolStripMenuItem("New Copilot Session Workspace");
        menuNewSessionWorkspace.Click += async (s, e) =>
        {
            if (this._cwdListView.SelectedItems.Count > 0
                && this._cwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                var gitRoot = SessionService.FindGitRoot(selectedCwd);
                if (gitRoot != null)
                {
                    var wsResult = WorkspaceCreatorForm.ShowWorkspaceCreator(gitRoot);
                    if (wsResult != null)
                    {
                        var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(wsResult.Value.WorktreePath, wsResult.Value.SessionName).ConfigureAwait(true);
                        if (newSessionId != null)
                        {
                            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                            Process.Start(new ProcessStartInfo(exePath, $"--resume {newSessionId}") { UseShellExecute = false });
                        }
                        else
                        {
                            MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
        };
        cwdContextMenu.Items.Add(menuNewSessionWorkspace);

        cwdContextMenu.Items.Add(new ToolStripSeparator());

        var menuAddDirectory = new ToolStripMenuItem("Add Directory");
        menuAddDirectory.Click += async (s, e) =>
        {
            using var fbd = new FolderBrowserDialog { SelectedPath = Program._settings.DefaultWorkDir };
            if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(fbd.SelectedPath))
            {
                PinnedDirectoryService.Add(fbd.SelectedPath);
                await this.RefreshNewSessionListAsync().ConfigureAwait(true);
            }
        };
        cwdContextMenu.Items.Add(menuAddDirectory);

        var menuRemoveDirectory = new ToolStripMenuItem("Remove Directory");
        menuRemoveDirectory.Click += async (s, e) =>
        {
            if (this._cwdListView.SelectedItems.Count > 0
                && this._cwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                PinnedDirectoryService.Remove(selectedCwd);
                await this.RefreshNewSessionListAsync().ConfigureAwait(true);
            }
        };
        cwdContextMenu.Items.Add(menuRemoveDirectory);

        cwdContextMenu.Opening += (s, e) =>
        {
            bool hasSelection = this._cwdListView.SelectedItems.Count > 0;
            bool isGit = false;
            bool isPinned = false;
            int sessionCount = 0;

            if (hasSelection && this._cwdListView.SelectedItems[0].Tag is string path)
            {
                if (this._cwdGitStatus.TryGetValue(path, out bool g))
                {
                    isGit = g;
                }

                if (int.TryParse(this._cwdListView.SelectedItems[0].SubItems[1].Text, out int count))
                {
                    sessionCount = count;
                }

                var pinnedDirs = PinnedDirectoryService.GetAll();
                isPinned = pinnedDirs.Exists(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase));
            }

            menuNewSession.Visible = hasSelection;
            menuNewSessionWorkspace.Visible = hasSelection && isGit;
            menuRemoveDirectory.Visible = hasSelection && isPinned && sessionCount == 0;
        };

        this._cwdListView.ContextMenuStrip = cwdContextMenu;

        // Right-click row selection
        this._cwdListView.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                var item = this._cwdListView.GetItemAt(e.X, e.Y);
                if (item != null)
                {
                    item.Selected = true;
                }
            }
        };

        // Double-click triggers New Copilot Session
        this._cwdListView.DoubleClick += async (s, e) =>
        {
            if (this._cwdListView.SelectedItems.Count > 0
                && this._cwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                var sessionName = NewSessionNameForm.ShowNamePrompt();
                if (sessionName == null)
                {
                    return;
                }

                var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(selectedCwd, sessionName).ConfigureAwait(true);
                if (newSessionId != null)
                {
                    var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                    Process.Start(new ProcessStartInfo(exePath, $"--resume {newSessionId}") { UseShellExecute = false });
                }
                else
                {
                    MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        };

        this._newSessionLoadingOverlay = new Label
        {
            Text = "Loading directories...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14f, FontStyle.Regular),
            ForeColor = SystemColors.GrayText,
            BackColor = SystemColors.Window
        };

        this._newSessionTab.Controls.Add(this._newSessionLoadingOverlay);
        this._newSessionLoadingOverlay.BringToFront();
        this._newSessionTab.Controls.Add(this._cwdListView);
    }

    private void SetupUpdateBanner()
    {
        this._updateLabel = new LinkLabel
        {
            Dock = DockStyle.Bottom,
            TextAlign = ContentAlignment.MiddleCenter,
            Height = 28,
            Visible = false,
            Padding = new Padding(0, 4, 0, 4)
        };
        this._updateLabel.LinkClicked += this.OnUpdateLabelClickedAsync;
    }

    private void SetupTimersAndEvents(int initialTab)
    {
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
            await this.RefreshNewSessionListAsync().ConfigureAwait(true);
        }

        if (tabIndex == 1)
        {
            this._cachedSessions = await Task.Run(() => LoadNamedSessions()).ConfigureAwait(true);
            var snapshot = await Task.Run(() => this._activeTracker.Refresh(this._cachedSessions)).ConfigureAwait(true);
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
        var snapshot = await Task.Run(() => this._activeTracker.Refresh(this._cachedSessions)).ConfigureAwait(true);
        this._gridController.Populate(this._cachedSessions, snapshot, this._searchBox.Text);

        this._loadingOverlay.Visible = false;

        await this.RefreshNewSessionListAsync().ConfigureAwait(true);
    }

    private async Task RefreshNewSessionListAsync()
    {
        var pinnedDirs = PinnedDirectoryService.GetAll();
        var data = await Task.Run(() => this._sessionDataService.LoadAll(Program.SessionStateDir, Program.PidRegistryFile, pinnedDirs)).ConfigureAwait(true);
        this._newSessionTabBuilder.Populate(this._cwdListView, this._cwdGitStatus, data);
        this._newSessionLoadingOverlay.Visible = false;
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
