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
    private List<NamedSession> _cachedSessions = new();
    private HashSet<string> _activeSessionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ActiveProcess>> _trackedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EdgeWorkspaceService> _edgeWorkspaces = new(StringComparer.OrdinalIgnoreCase);
    private bool _edgeInitialScanDone;
    private readonly Timer? _activeStatusTimer;

    // New Session tab controls
    private readonly ListView _cwdListView;
    private readonly Button _btnCreateWorkspace = null!;
    private readonly Dictionary<string, bool> _cwdGitStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly ListViewColumnSorter _cwdSorter = new(column: 1, order: SortOrder.Descending);

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
        this.Size = new Size(700, 550);
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
        this._sessionGrid.Columns["Session"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        this._sessionGrid.Columns["CWD"]!.Width = 110;
        this._sessionGrid.Columns["Date"]!.Width = 130;
        this._sessionGrid.Columns["Active"]!.Width = 100;

        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(5, 5, 5, 2) };
        var searchLabel = new Label { Text = "Search:", AutoSize = true, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft };
        var btnRefreshTop = new Button
        {
            Text = "Refresh",
            Width = 65,
            Dock = DockStyle.Right
        };
        btnRefreshTop.Click += (s, e) =>
        {
            this._cachedSessions = LoadNamedSessions();
            this.RefreshSessionList();
        };
        this._searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Filter sessions..."
        };
        this._searchBox.TextChanged += (s, e) => this.RefreshSessionList();
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

        this._sessionGrid.CellClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 3)
            {
                return;
            }

            var row = this._sessionGrid.Rows[e.RowIndex];
            var activeText = row.Cells[3].Value as string;
            if (!string.IsNullOrEmpty(activeText) && row.Tag is string sessionId)
            {
                var cellRect = this._sessionGrid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                this.FocusActiveProcess(sessionId, cellRect.Location);
            }
        };

        this._sessionGrid.CellMouseMove += (s, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 3)
            {
                var activeText = this._sessionGrid.Rows[e.RowIndex].Cells[3].Value as string;
                this._sessionGrid.Cursor = !string.IsNullOrEmpty(activeText) ? Cursors.Hand : Cursors.Default;
            }
            else
            {
                this._sessionGrid.Cursor = Cursors.Default;
            }
        };

        this._sessionGrid.CellMouseLeave += (s, e) =>
        {
            this._sessionGrid.Cursor = Cursors.Default;
        };

        // Right-click context menu with Edit option
        var gridContextMenu = new ContextMenuStrip();
        var editMenuItem = new ToolStripMenuItem("Edit");
        editMenuItem.Click += (s, e) =>
        {
            var sid = this.GetSelectedSessionId();
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
                    this._cachedSessions = LoadNamedSessions();
                    this.RefreshSessionList();
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

        var btnOpenSession = new Button { Text = "Open Session", Width = 100, Height = 28 };
        btnOpenSession.Click += (s, e) =>
        {
            var sid = this.GetSelectedSessionId();
            if (sid != null)
            {
                this.SelectedSessionId = sid;
                this.LaunchSession();
            }
        };

        var openSessionMenu = new ContextMenuStrip();
        var menuOpenNewSession = new ToolStripMenuItem("Open as New Session");
        menuOpenNewSession.Click += (s, e) =>
        {
            var sessionId = this.GetSelectedSessionId();
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
        openSessionMenu.Items.Add(menuOpenNewSession);

        var menuOpenNewSessionWorkspace = new ToolStripMenuItem("Open as New Session Workspace");
        menuOpenNewSessionWorkspace.Click += (s, e) =>
        {
            var sessionId = this.GetSelectedSessionId();
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
        openSessionMenu.Items.Add(menuOpenNewSessionWorkspace);

        openSessionMenu.Opening += (s, e) =>
        {
            bool isGit = false;
            var sessionId = this.GetSelectedSessionId();
            if (sessionId != null)
            {
                var workspaceFile = Path.Combine(Program.SessionStateDir, sessionId, "workspace.yaml");
                if (File.Exists(workspaceFile))
                {
                    foreach (var line in File.ReadLines(workspaceFile))
                    {
                        if (line.StartsWith("cwd:"))
                        {
                            var cwd = line.Substring("cwd:".Length).Trim();
                            isGit = !string.IsNullOrEmpty(cwd) && GitService.IsGitRepository(cwd);
                            break;
                        }
                    }
                }
            }

            menuOpenNewSessionWorkspace.Visible = isGit;
        };

        var btnOpenArrow = new Button { Text = "▾", Width = 20, Height = 28 };
        btnOpenArrow.Click += (s, e) =>
        {
            openSessionMenu.Show(btnOpenArrow, new Point(0, btnOpenArrow.Height));
        };

        var splitOpenPanel = new Panel { Width = 122, Height = btnOpenSession.Height };
        splitOpenPanel.Controls.Add(btnOpenArrow);
        splitOpenPanel.Controls.Add(btnOpenSession);
        btnOpenSession.Location = new Point(0, 0);
        btnOpenArrow.Location = new Point(btnOpenSession.Width, 0);

        sessionButtonPanel.Controls.Add(splitOpenPanel);

        if (Program._settings.Ides.Count > 0)
        {
            var btnIde = new Button { Text = "Open in IDE", Width = 100, Height = 28 };
            btnIde.Click += (s, e) =>
            {
                var sid = this.GetSelectedSessionId();
                if (sid != null)
                {
                    IdePickerForm.OpenIdeForSession(sid, (ideName, pid, folderPath) =>
                    {
                        if (!this._trackedProcesses.ContainsKey(sid))
                        {
                            this._trackedProcesses[sid] = new List<ActiveProcess>();
                        }
                        this._trackedProcesses[sid].Add(new ActiveProcess(ideName, pid, folderPath));
                        this.RefreshActiveStatus();
                    });
                }
            };
            sessionButtonPanel.Controls.Add(btnIde);
        }

        var btnEdge = new Button { Text = "Open in Edge", Width = 100, Height = 28 };
        btnEdge.Click += async (s, e) =>
        {
            var sid = this.GetSelectedSessionId();
            if (sid == null)
            {
                return;
            }

            if (this._edgeWorkspaces.TryGetValue(sid, out var existing) && existing.IsOpen)
            {
                existing.Focus();
                return;
            }

            var workspace = new EdgeWorkspaceService(sid);
            workspace.WindowClosed += () =>
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(() => this.OnEdgeWorkspaceClosed(sid));
                }
                else
                {
                    this.OnEdgeWorkspaceClosed(sid);
                }
            };
            this._edgeWorkspaces[sid] = workspace;

            await workspace.OpenAsync().ConfigureAwait(true);
            this.RefreshActiveStatus();
        };
        sessionButtonPanel.Controls.Add(btnEdge);

        this._sessionsTab.Controls.Add(this._sessionGrid);
        this._sessionsTab.Controls.Add(searchPanel);
        this._sessionsTab.Controls.Add(sessionButtonPanel);

        this._cachedSessions = LoadNamedSessions();
        this.RefreshSessionList();

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

        var toolsButtons = CreateListButtons(this._toolsList, "Tool name:", "Add Tool", addBrowse: false);
        toolsTab.Controls.Add(this._toolsList);
        toolsTab.Controls.Add(toolsButtons);

        // --- Allowed Directories tab ---
        var dirsTab = new TabPage("Allowed Directories");
        this._dirsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var dir in Program._settings.AllowedDirs)
        {
            this._dirsList.Items.Add(dir);
        }

        var dirsButtons = CreateListButtons(this._dirsList, "Directory path:", "Add Directory", addBrowse: true);
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

        var ideButtons = this.CreateIdeButtons();
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
        btnCancel.Click += (s, e) => this.ReloadSettingsUI();

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
        this._cwdListView.ListViewItemSorter = this._cwdSorter;
        this._cwdListView.ColumnClick += this.OnCwdColumnClick;

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

        this.RefreshNewSessionList();

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

        this._activeStatusTimer = new Timer { Interval = 3000 };
        this._activeStatusTimer.Tick += (s, e) => this.RefreshActiveStatus();
        this._activeStatusTimer.Start();

        this.Shown += (s, e) => _ = this.CheckForUpdateInBackgroundAsync();
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

    private void FocusActiveProcess(string sessionId, Point clickLocation)
    {
        var focusTargets = new List<(string name, Action focus)>();

        if (this._activeSessionIds.Contains(sessionId))
        {
            var activeSessions = SessionService.GetActiveSessions(Program.PidRegistryFile, Program.SessionStateDir);
            var session = activeSessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null && session.CopilotPid > 0)
            {
                var pid = session.CopilotPid;
                focusTargets.Add(("Terminal", () => WindowFocusService.TryFocusProcessWindow(pid)));
            }
        }

        if (this._trackedProcesses.TryGetValue(sessionId, out var procs))
        {
            foreach (var proc in procs)
            {
                try
                {
                    var p = Process.GetProcessById(proc.Pid);
                    if (!p.HasExited)
                    {
                        var capturedProc = proc;
                        focusTargets.Add((proc.Name, () =>
                        {
                            if (capturedProc.FolderPath != null)
                            {
                                var folderName = Path.GetFileName(capturedProc.FolderPath.TrimEnd('\\'));
                                WindowFocusService.TryFocusWindowByTitle(folderName);
                            }
                            else
                            {
                                WindowFocusService.TryFocusProcessWindow(capturedProc.Pid);
                            }
                        }
                        ));
                    }
                }
                catch { }
            }
        }

        if (this._edgeWorkspaces.TryGetValue(sessionId, out var ws) && ws.IsOpen)
        {
            focusTargets.Add(("Edge", () => ws.Focus()));
        }

        if (focusTargets.Count == 0)
        {
            return;
        }

        if (focusTargets.Count == 1)
        {
            focusTargets[0].focus();
            return;
        }

        // Multiple targets — show context menu
        var menu = new ContextMenuStrip();
        foreach (var (name, focus) in focusTargets)
        {
            var menuItem = new ToolStripMenuItem($"Focus: {name}");
            var capturedFocus = focus;
            menuItem.Click += (s, e) => capturedFocus();
            menu.Items.Add(menuItem);
        }
        menu.Show(this._sessionGrid, clickLocation);
    }

    /// <summary>
    /// Switches the main tab control to the specified tab and brings the form to the foreground.
    /// </summary>
    /// <param name="tabIndex">The zero-based index of the tab to activate.</param>
    public void SwitchToTab(int tabIndex)
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
            this._cachedSessions = LoadNamedSessions();
            this.RefreshSessionList();
        }

        if (this.WindowState == FormWindowState.Minimized)
        {
            this.WindowState = FormWindowState.Normal;
        }

        this.BringToFront();
        this.Activate();
    }

    private static readonly Color s_activeRowColor = Color.FromArgb(232, 245, 255);

    private void RefreshSessionList()
    {
        this._sessionGrid.Rows.Clear();
        this._activeSessionIds = LoadActiveSessionIds();
        var query = this._searchBox.Text;
        var isSearching = !string.IsNullOrWhiteSpace(query);

        var displayed = isSearching
            ? SessionService.SearchSessions(this._cachedSessions, query)
            : this._cachedSessions.Take(50).ToList();

        foreach (var session in displayed)
        {
            var dateText = session.LastModified.ToString("yyyy-MM-dd HH:mm");
            var cwdText = session.Folder;
            if (session.IsGitRepo)
            {
                cwdText += " \u2387";
            }

            var activeText = this.BuildActiveText(session.Id);
            var rowIndex = this._sessionGrid.Rows.Add(session.Summary, cwdText, dateText, activeText);
            var row = this._sessionGrid.Rows[rowIndex];
            row.Tag = session.Id;

            if (!string.IsNullOrEmpty(activeText))
            {
                row.DefaultCellStyle.BackColor = s_activeRowColor;
                row.Cells[3].Style.ForeColor = Color.FromArgb(0, 102, 204);
                row.Cells[3].Style.Font = new Font(this._sessionGrid.Font, FontStyle.Underline);
            }
        }
    }

    private string BuildActiveText(string sessionId)
    {
        var parts = new List<string>();

        if (this._activeSessionIds.Contains(sessionId))
        {
            parts.Add("Terminal");
        }

        if (this._trackedProcesses.TryGetValue(sessionId, out var procs))
        {
            foreach (var proc in procs)
            {
                try
                {
                    var p = Process.GetProcessById(proc.Pid);
                    if (!p.HasExited)
                    {
                        parts.Add(proc.Name);
                    }
                }
                catch { }
            }
        }

        if (this._edgeWorkspaces.TryGetValue(sessionId, out var ws) && ws.IsOpen)
        {
            parts.Add("Edge");
        }
        else if (!this._edgeInitialScanDone && !this._edgeWorkspaces.ContainsKey(sessionId))
        {
            // On first refresh, probe for Edge workspaces opened before app restart
            var probe = new EdgeWorkspaceService(sessionId);
            if (probe.IsOpen)
            {
                probe.WindowClosed += () =>
                {
                    if (this.InvokeRequired)
                    {
                        this.BeginInvoke(() => this.OnEdgeWorkspaceClosed(sessionId));
                    }
                    else
                    {
                        this.OnEdgeWorkspaceClosed(sessionId);
                    }
                };
                this._edgeWorkspaces[sessionId] = probe;
                parts.Add("Edge");
            }
        }

        return string.Join("\n", parts);
    }

    private void OnEdgeWorkspaceClosed(string sessionId)
    {
        this._edgeWorkspaces.Remove(sessionId);
        this.RefreshActiveStatus();
    }

    private static HashSet<string> LoadActiveSessionIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var activeSessions = SessionService.GetActiveSessions(Program.PidRegistryFile, Program.SessionStateDir);
            foreach (var s in activeSessions)
            {
                ids.Add(s.Id);
            }
        }
        catch { }
        return ids;
    }

    private void RefreshActiveStatus()
    {
        this._activeSessionIds = LoadActiveSessionIds();

        // Clean up dead IDE processes — try to re-match by window title for IDEs that use launcher shims (e.g. VSCode)
        foreach (var kvp in this._trackedProcesses)
        {
            for (int i = kvp.Value.Count - 1; i >= 0; i--)
            {
                var proc = kvp.Value[i];
                bool alive;
                try { alive = !Process.GetProcessById(proc.Pid).HasExited; }
                catch { alive = false; }

                if (!alive && proc.FolderPath != null)
                {
                    var folderName = Path.GetFileName(proc.FolderPath.TrimEnd('\\'));
                    var matchPid = WindowFocusService.FindProcessIdByWindowTitle(folderName);
                    if (matchPid > 0)
                    {
                        proc.Pid = matchPid;
                    }
                    else
                    {
                        kvp.Value.RemoveAt(i);
                    }
                }
                else if (!alive)
                {
                    kvp.Value.RemoveAt(i);
                }
            }
        }

        // Clean up closed Edge workspaces
        var closedEdge = new List<string>();
        foreach (var kvp in this._edgeWorkspaces)
        {
            if (!kvp.Value.IsOpen)
            {
                closedEdge.Add(kvp.Key);
            }
        }

        foreach (var id in closedEdge)
        {
            this._edgeWorkspaces.Remove(id);
        }

        foreach (DataGridViewRow row in this._sessionGrid.Rows)
        {
            if (row.Tag is not string sessionId)
            {
                continue;
            }

            var activeText = this.BuildActiveText(sessionId);
            row.Cells[3].Value = activeText;
            row.DefaultCellStyle.BackColor = string.IsNullOrEmpty(activeText) ? SystemColors.Window : s_activeRowColor;
            if (!string.IsNullOrEmpty(activeText))
            {
                row.Cells[3].Style.ForeColor = Color.FromArgb(0, 102, 204);
                row.Cells[3].Style.Font = new Font(this._sessionGrid.Font, FontStyle.Underline);
            }
            else
            {
                row.Cells[3].Style.ForeColor = SystemColors.ControlText;
                row.Cells[3].Style.Font = this._sessionGrid.Font;
            }
        }

        this._edgeInitialScanDone = true;
    }

    private void RefreshNewSessionList()
    {
        this._cwdListView.Items.Clear();
        this._cwdGitStatus.Clear();

        var cwdCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(Program.SessionStateDir))
        {
            foreach (var dir in Directory.GetDirectories(Program.SessionStateDir))
            {
                var wsFile = Path.Combine(dir, "workspace.yaml");
                if (!File.Exists(wsFile))
                {
                    continue;
                }

                try
                {
                    foreach (var line in File.ReadAllLines(wsFile))
                    {
                        if (line.StartsWith("cwd:"))
                        {
                            var cwd = line[4..].Trim();
                            if (!string.IsNullOrEmpty(cwd) && Directory.Exists(cwd))
                            {
                                cwdCounts.TryGetValue(cwd, out int count);
                                cwdCounts[cwd] = count + 1;
                            }
                            break;
                        }
                    }
                }
                catch { }
            }
        }

        foreach (var kv in cwdCounts)
        {
            var cwd = kv.Key;
            var isGit = GitService.IsGitRepository(cwd);
            this._cwdGitStatus[cwd] = isGit;

            var item = new ListViewItem(cwd) { Tag = cwd };
            item.SubItems.Add(kv.Value.ToString());
            item.SubItems.Add(isGit ? "Yes" : "");
            this._cwdListView.Items.Add(item);
        }

        this._cwdListView.Sort();

        if (this._cwdListView.Items.Count > 0)
        {
            this._cwdListView.Items[0].Selected = true;
        }
    }

    private static readonly string[] s_cwdColumnBaseNames = { "Directory", "# Sessions created", "Git" };

    private void OnCwdColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (e.Column == this._cwdSorter.SortColumn)
        {
            this._cwdSorter.Order = this._cwdSorter.Order == SortOrder.Ascending
                ? SortOrder.Descending
                : SortOrder.Ascending;
        }
        else
        {
            this._cwdSorter.SortColumn = e.Column;
            // Session count defaults to descending; others to ascending
            this._cwdSorter.Order = e.Column == 1 ? SortOrder.Descending : SortOrder.Ascending;
        }

        for (int i = 0; i < s_cwdColumnBaseNames.Length; i++)
        {
            this._cwdListView.Columns[i].Text = i == this._cwdSorter.SortColumn
                ? s_cwdColumnBaseNames[i] + (this._cwdSorter.Order == SortOrder.Ascending ? " ▲" : " ▼")
                : s_cwdColumnBaseNames[i];
        }

        this._cwdListView.Sort();
    }

    private string? GetSelectedSessionId()
    {
        if (this._sessionGrid.CurrentRow != null)
        {
            return this._sessionGrid.CurrentRow.Tag as string;
        }
        return null;
    }

    private void ReloadSettingsUI()
    {
        var fresh = LauncherSettings.Load();

        this._toolsList.Items.Clear();
        foreach (var tool in fresh.AllowedTools)
        {
            this._toolsList.Items.Add(tool);
        }

        this._dirsList.Items.Clear();
        foreach (var dir in fresh.AllowedDirs)
        {
            this._dirsList.Items.Add(dir);
        }

        this._idesList.Items.Clear();
        foreach (var ide in fresh.Ides)
        {
            var item = new ListViewItem(ide.Description);
            item.SubItems.Add(ide.Path);
            this._idesList.Items.Add(item);
        }

        this._workDirBox.Text = fresh.DefaultWorkDir;
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

    private static FlowLayoutPanel CreateListButtons(ListBox listBox, string promptText, string addTitle, bool addBrowse)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 100,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(4)
        };

        var btnAdd = new Button { Text = "Add", Width = 88 };
        btnAdd.Click += (s, e) =>
        {
            if (addBrowse)
            {
                using var fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(fbd.SelectedPath))
                {
                    listBox.Items.Add(fbd.SelectedPath);
                    listBox.SelectedIndex = listBox.Items.Count - 1;
                }
            }
            else
            {
                var value = PromptInput(addTitle, promptText, "");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    listBox.Items.Add(value);
                    listBox.SelectedIndex = listBox.Items.Count - 1;
                }
            }
            listBox.Focus();
        };

        var btnEdit = new Button { Text = "Edit", Width = 88 };
        btnEdit.Click += (s, e) =>
        {
            if (listBox.SelectedIndex < 0)
            {
                return;
            }

            var current = listBox.SelectedItem?.ToString() ?? "";

            if (addBrowse)
            {
                using var fbd = new FolderBrowserDialog { SelectedPath = current };
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    listBox.Items[listBox.SelectedIndex] = fbd.SelectedPath;
                }
            }
            else
            {
                var value = PromptInput("Edit", promptText, current);
                if (value != null)
                {
                    listBox.Items[listBox.SelectedIndex] = value;
                }
            }
            listBox.Focus();
        };

        var btnRemove = new Button { Text = "Remove", Width = 88 };
        btnRemove.Click += (s, e) =>
        {
            int idx = listBox.SelectedIndex;
            if (idx >= 0)
            {
                listBox.Items.RemoveAt(idx);
                if (listBox.Items.Count > 0)
                {
                    listBox.SelectedIndex = Math.Min(idx, listBox.Items.Count - 1);
                }

                listBox.Focus();
            }
        };

        var btnUp = new Button { Text = "Move Up", Width = 88 };
        btnUp.Click += (s, e) =>
        {
            int idx = listBox.SelectedIndex;
            if (idx > 0)
            {
                var item = listBox.Items[idx];
                listBox.Items.RemoveAt(idx);
                listBox.Items.Insert(idx - 1, item);
                listBox.SelectedIndex = idx - 1;
                listBox.Focus();
            }
        };

        var btnDown = new Button { Text = "Move Down", Width = 88 };
        btnDown.Click += (s, e) =>
        {
            int idx = listBox.SelectedIndex;
            if (idx >= 0 && idx < listBox.Items.Count - 1)
            {
                var item = listBox.Items[idx];
                listBox.Items.RemoveAt(idx);
                listBox.Items.Insert(idx + 1, item);
                listBox.SelectedIndex = idx + 1;
                listBox.Focus();
            }
        };

        panel.Controls.AddRange([btnAdd, btnEdit, btnRemove, btnUp, btnDown]);
        return panel;
    }

    private FlowLayoutPanel CreateIdeButtons()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 100,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(4)
        };

        var btnAdd = new Button { Text = "Add", Width = 88 };
        btnAdd.Click += (s, e) =>
        {
            var result = PromptIdeEntry("Add IDE", "", "");
            if (result != null)
            {
                var item = new ListViewItem(result.Value.desc);
                item.SubItems.Add(result.Value.path);
                this._idesList.Items.Add(item);
                item.Selected = true;
                this._idesList.Focus();
            }
        };

        var btnEdit = new Button { Text = "Edit", Width = 88 };
        btnEdit.Click += (s, e) =>
        {
            if (this._idesList.SelectedItems.Count == 0)
            {
                return;
            }

            var sel = this._idesList.SelectedItems[0];
            var result = PromptIdeEntry("Edit IDE", sel.SubItems[1].Text, sel.Text);
            if (result != null)
            {
                sel.Text = result.Value.desc;
                sel.SubItems[1].Text = result.Value.path;
            }
            this._idesList.Focus();
        };

        var btnRemove = new Button { Text = "Remove", Width = 88 };
        btnRemove.Click += (s, e) =>
        {
            if (this._idesList.SelectedItems.Count > 0)
            {
                int idx = this._idesList.SelectedIndices[0];
                this._idesList.Items.RemoveAt(idx);
                if (this._idesList.Items.Count > 0)
                {
                    int newIdx = Math.Min(idx, this._idesList.Items.Count - 1);
                    this._idesList.Items[newIdx].Selected = true;
                }
                this._idesList.Focus();
            }
        };

        panel.Controls.AddRange([btnAdd, btnEdit, btnRemove]);
        return panel;
    }

    private static (string path, string desc)? PromptIdeEntry(string title, string defaultPath, string defaultDesc)
    {
        var form = new Form
        {
            Text = title,
            Size = new Size(500, 190),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var lblDesc = new Label { Text = "Description:", Location = new Point(12, 15), AutoSize = true };
        var txtDesc = new TextBox { Text = defaultDesc, Location = new Point(12, 35), Width = 455 };

        var lblPath = new Label { Text = "Executable path:", Location = new Point(12, 65), AutoSize = true };
        var txtPath = new TextBox { Text = defaultPath, Location = new Point(12, 85), Width = 410 };
        var btnBrowse = new Button { Text = "...", Location = new Point(428, 84), Width = 40 };
        btnBrowse.Click += (s, e) =>
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
                FileName = txtPath.Text
            };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                txtPath.Text = ofd.FileName;
            }
        };

        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(310, 118), Width = 75 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(392, 118), Width = 75 };

        form.Controls.AddRange([lblDesc, txtDesc, lblPath, txtPath, btnBrowse, btnOk, btnCancel]);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(txtPath.Text))
        {
            return (txtPath.Text, txtDesc.Text);
        }

        return null;
    }

    private static string? PromptInput(string title, string label, string defaultValue)
    {
        var form = new Form
        {
            Text = title,
            Size = new Size(450, 150),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var lbl = new Label { Text = label, Location = new Point(12, 15), AutoSize = true };
        var txt = new TextBox { Text = defaultValue, Location = new Point(12, 38), Width = 405 };
        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(260, 72), Width = 75 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(342, 72), Width = 75 };

        form.Controls.AddRange([lbl, txt, btnOk, btnCancel]);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? txt.Text : null;
    }
}
