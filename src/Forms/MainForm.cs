using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
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
    private readonly ListView _sessionListView;
    private List<NamedSession> _cachedSessions = new();

    // New Session tab controls
    private readonly ListView _cwdListView;
    private readonly Button _btnCreateWorkspace = null!;
    private readonly Dictionary<string, bool> _cwdGitStatus = new(StringComparer.OrdinalIgnoreCase);

    // Settings tab controls
    private readonly ListBox _toolsList;
    private readonly ListBox _dirsList;
    private readonly ListView _idesList;
    private readonly TextBox _workDirBox;

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

        this._sessionListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Tile,
            FullRowSelect = true,
            MultiSelect = false,
            TileSize = new Size(560, 65)
        };
        this._sessionListView.Columns.Add("Session");
        this._sessionListView.Columns.Add("CWD");
        this._sessionListView.Columns.Add("Date");

        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(5, 5, 5, 2) };
        var searchLabel = new Label { Text = "Search:", AutoSize = true, Location = new Point(5, 7) };
        this._searchBox = new TextBox
        {
            Location = new Point(55, 4),
            Width = 400,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            PlaceholderText = "Filter sessions..."
        };
        this._searchBox.TextChanged += (s, e) => this.RefreshSessionList();
        searchPanel.Controls.AddRange([searchLabel, this._searchBox]);

        this._sessionListView.DoubleClick += (s, e) =>
        {
            if (this._sessionListView.SelectedItems.Count > 0)
            {
                this.SelectedSessionId = this._sessionListView.SelectedItems[0].Tag as string;
                this.LaunchSessionAndClose();
            }
        };

        var sessionButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5)
        };

        var btnOpenSession = new Button { Text = "Open Session", Width = 100 };
        btnOpenSession.Click += (s, e) =>
        {
            if (this._sessionListView.SelectedItems.Count > 0)
            {
                this.SelectedSessionId = this._sessionListView.SelectedItems[0].Tag as string;
                this.LaunchSessionAndClose();
            }
        };

        var openSessionMenu = new ContextMenuStrip();
        var menuOpenNewSession = new ToolStripMenuItem("Open as New Session");
        menuOpenNewSession.Click += (s, e) =>
        {
            if (this._sessionListView.SelectedItems.Count > 0
                && this._sessionListView.SelectedItems[0].Tag is string sessionId)
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
                    this.Close();
                }
            }
        };
        openSessionMenu.Items.Add(menuOpenNewSession);

        var menuOpenNewSessionWorkspace = new ToolStripMenuItem("Open as New Session Workspace");
        menuOpenNewSessionWorkspace.Click += (s, e) =>
        {
            if (this._sessionListView.SelectedItems.Count > 0
                && this._sessionListView.SelectedItems[0].Tag is string sessionId)
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
                            this.Close();
                        }
                    }
                }
            }
        };
        openSessionMenu.Items.Add(menuOpenNewSessionWorkspace);

        openSessionMenu.Opening += (s, e) =>
        {
            bool isGit = false;
            if (this._sessionListView.SelectedItems.Count > 0
                && this._sessionListView.SelectedItems[0].Tag is string sessionId)
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

        var btnOpenArrow = new Button { Text = "▾", Width = 20 };
        btnOpenArrow.Click += (s, e) =>
        {
            openSessionMenu.Show(btnOpenArrow, new Point(0, btnOpenArrow.Height));
        };

        var splitOpenPanel = new Panel { Width = 122, Height = btnOpenSession.Height };
        splitOpenPanel.Controls.Add(btnOpenArrow);
        splitOpenPanel.Controls.Add(btnOpenSession);
        btnOpenSession.Location = new Point(0, 0);
        btnOpenArrow.Location = new Point(btnOpenSession.Width, 0);

        var btnRefresh = new Button { Text = "Refresh", Width = 80 };
        btnRefresh.Click += (s, e) =>
        {
            this._cachedSessions = LoadNamedSessions();
            this.RefreshSessionList();
        };

        sessionButtonPanel.Controls.Add(splitOpenPanel);

        if (Program._settings.Ides.Count > 0)
        {
            var btnIde = new Button { Text = "Open in IDE", Width = 100 };
            btnIde.Click += (s, e) =>
            {
                if (this._sessionListView.SelectedItems.Count > 0)
                {
                    if (this._sessionListView.SelectedItems[0].Tag is string sid)
                    {
                        IdePickerForm.OpenIdeForSession(sid);
                    }
                }
            };
            sessionButtonPanel.Controls.Add(btnIde);
        }

        sessionButtonPanel.Controls.Add(btnRefresh);

        this._sessionsTab.Controls.Add(this._sessionListView);
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
        this._cwdListView.Columns.Add("# Sessions created", 120, HorizontalAlignment.Center);
        this._cwdListView.Columns.Add("Git", 50, HorizontalAlignment.Center);

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
        this.Controls.Add(this._mainTabs);

        if (initialTab >= 0 && initialTab < this._mainTabs.TabPages.Count)
        {
            this._mainTabs.SelectedIndex = initialTab;
        }
    }

    private void LaunchSessionAndClose()
    {
        if (this.SelectedSessionId != null)
        {
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            Process.Start(new ProcessStartInfo(exePath, $"--resume {this.SelectedSessionId}")
            {
                UseShellExecute = false
            });
        }
        this.Close();
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

    private void RefreshSessionList()
    {
        this._sessionListView.Items.Clear();
        var query = this._searchBox.Text;
        var isSearching = !string.IsNullOrWhiteSpace(query);

        var displayed = isSearching
            ? SessionService.SearchSessions(this._cachedSessions, query)
            : this._cachedSessions.Take(50).ToList();

        foreach (var session in displayed)
        {
            var item = new ListViewItem(session.Summary) { Tag = session.Id };
            item.SubItems.Add(session.Cwd);
            var dateText = session.LastModified.ToString("yyyy-MM-dd HH:mm");
            if (!string.IsNullOrEmpty(session.Cwd) && GitService.IsGitRepository(session.Cwd))
            {
                dateText += " - Git";
            }

            item.SubItems.Add(dateText);
            this._sessionListView.Items.Add(item);
        }
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

        var sortedCwds = cwdCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var cwd in sortedCwds)
        {
            var isGit = GitService.IsGitRepository(cwd);
            this._cwdGitStatus[cwd] = isGit;

            var item = new ListViewItem(cwd) { Tag = cwd };
            item.SubItems.Add(cwdCounts[cwd].ToString());
            item.SubItems.Add(isGit ? "Yes" : "");
            this._cwdListView.Items.Add(item);
        }

        if (this._cwdListView.Items.Count > 0)
        {
            this._cwdListView.Items[0].Selected = true;
        }
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
    internal static List<NamedSession> LoadNamedSessions() => SessionService.LoadNamedSessions(Program.SessionStateDir);

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
