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
    private readonly ExistingSessionsVisuals _sessionsVisuals = null!;
    private List<NamedSession> _cachedSessions = new();
    private readonly ActiveStatusTracker _activeTracker = new();
    private readonly SessionInteractionManager _interactionManager;
    private Timer? _activeStatusTimer;
    private Timer? _spinnerTimer;
    private BellNotificationService? _bellService;

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
    private CheckBox _notifyOnBellCheck = null!;
    private ComboBox _themeCombo = null!;
    private bool _suppressThemeChange;

    // Update banner
    private LinkLabel _updateLabel = null!;

    // System tray
    private NotifyIcon? _trayIcon;
    private bool _forceClose;

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
        this._interactionManager = new SessionInteractionManager(Program.SessionStateDir, Program.TerminalCacheFile);

        this._mainTabs = new TabControl { Dock = DockStyle.Fill };
        if (!Application.IsDarkModeEnabled)
        {
            this._mainTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            this._mainTabs.DrawItem += (s, e) =>
            {
                bool selected = e.Index == this._mainTabs.SelectedIndex;
                var back = selected ? SystemColors.Window : Color.FromArgb(220, 220, 220);
                var fore = SystemColors.ControlText;
                using var brush = new SolidBrush(back);
                e.Graphics.FillRectangle(brush, e.Bounds);
                var text = this._mainTabs.TabPages[e.Index].Text;
                TextRenderer.DrawText(e.Graphics, text, this._mainTabs.Font, e.Bounds, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }
        this._sessionsTab = new TabPage("Existing Sessions");
        this._settingsTab = new TabPage("Settings");
        this._newSessionTab = new TabPage("New Session");

        this._sessionsVisuals = new ExistingSessionsVisuals(this._sessionsTab, this._activeTracker);
        this.WireSessionsEvents();
        this.BuildSettingsTab();
        this.BuildNewSessionTab();
        this.SetupUpdateBanner();
        this.SetupTrayIcon();

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

    private void SetupTrayIcon()
    {
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show", null, (s, e) => this.RestoreFromTray());
        trayMenu.Items.Add("Settings", null, (s, e) => this.ShowTab(2));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Quit", null, (s, e) =>
        {
            this._forceClose = true;
            Application.Exit();
        });

        // Load icon: try .ico file next to exe, then extract from exe, then form default
        Icon? trayIconImage = null;
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "copilot.ico");
            if (File.Exists(icoPath))
            {
                trayIconImage = new Icon(icoPath);
            }
            else
            {
                trayIconImage = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
        }
        catch { }

        trayIconImage ??= this.Icon ?? SystemIcons.Application;

        this._trayIcon = new NotifyIcon
        {
            Icon = trayIconImage,
            Text = "Copilot Booster",
            ContextMenuStrip = trayMenu,
            Visible = true,
        };
        this._bellService = new BellNotificationService(this._trayIcon, () => Program._settings.NotifyOnBell);
        this._trayIcon.DoubleClick += (s, e) => this.RestoreFromTray();
        this._trayIcon.BalloonTipClicked += (s, e) =>
        {
            if (this._bellService.LastNotifiedSessionId is string sid)
            {
                this._activeTracker.FocusActiveProcess(sid, 0);
            }
        };
    }

    private void ShowTab(int tabIndex)
    {
        this.RestoreFromTray();
        if (tabIndex >= 0 && tabIndex < this._mainTabs.TabPages.Count)
        {
            this._mainTabs.SelectedIndex = tabIndex;
        }
    }

    /// <summary>
    /// Restores the window from the system tray.
    /// </summary>
    private void RestoreFromTray()
    {
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.Activate();
    }

    /// <summary>
    /// Forces a real close (bypassing minimize-to-tray) and exits the application.
    /// </summary>
    internal void ForceClose()
    {
        this._forceClose = true;
        this.Close();
    }

    /// <inheritdoc/>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!this._forceClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.WindowState = FormWindowState.Minimized;
            this.Hide();

            return;
        }

        if (this._trayIcon != null)
        {
            this._trayIcon.Visible = false;
            this._trayIcon.Dispose();
            this._trayIcon = null;
        }

        base.OnFormClosing(e);
    }

    private void WireSessionsEvents()
    {
        this._sessionsVisuals.OnRefreshRequested += async () =>
        {
            this._cachedSessions = await Task.Run(() => LoadNamedSessions()).ConfigureAwait(true);
            var snapshot = this._activeTracker.Refresh(this._cachedSessions);
            this._sessionsVisuals.GridVisuals.Populate(this._cachedSessions, snapshot, this._sessionsVisuals.SearchBox.Text);
        };

        this._sessionsVisuals.OnSearchChanged += () =>
        {
            var snapshot = this._activeTracker.Refresh(this._cachedSessions);
            this._sessionsVisuals.GridVisuals.Populate(this._cachedSessions, snapshot, this._sessionsVisuals.SearchBox.Text);
        };

        this._sessionsVisuals.OnSessionDoubleClicked += (sid) =>
        {
            this.SelectedSessionId = sid;
            this.LaunchSession();
        };

        this._sessionsVisuals.OnOpenSession += (sid) =>
        {
            this.SelectedSessionId = sid;
            this.LaunchSession();
        };

        this._sessionsVisuals.OnEditSession += async (sid) =>
        {
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
                    this._sessionsVisuals.GridVisuals.Populate(this._cachedSessions, snapshot, this._sessionsVisuals.SearchBox.Text);
                }
            }
        };

        this._sessionsVisuals.OnOpenAsNewSession += async (selectedSessionId) =>
        {
            var selectedCwd = this._interactionManager.GetSessionCwd(selectedSessionId);

            if (!string.IsNullOrEmpty(selectedCwd))
            {
                var sessionName = NewSessionNameForm.ShowNamePrompt();
                if (sessionName == null)
                {
                    return;
                }

                var sourceDir = Path.Combine(Program.SessionStateDir, selectedSessionId);
                var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(selectedCwd, sessionName, sourceDir).ConfigureAwait(true);
                if (newSessionId != null)
                {
                    this._interactionManager.LaunchSession(newSessionId);
                    await this.RefreshGridAsync().ConfigureAwait(true);
                }
                else
                {
                    MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        };

        this._sessionsVisuals.OnOpenAsNewSessionWorkspace += async (selectedSessionId) =>
        {
            var selectedCwd = this._interactionManager.GetSessionCwd(selectedSessionId);

            if (!string.IsNullOrEmpty(selectedCwd))
            {
                var gitRoot = SessionService.FindGitRoot(selectedCwd);
                if (gitRoot != null)
                {
                    var wsResult = WorkspaceCreatorVisuals.ShowWorkspaceCreator(gitRoot);
                    if (wsResult != null)
                    {
                        var sourceDir = Path.Combine(Program.SessionStateDir, selectedSessionId);
                        var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(wsResult.Value.WorktreePath, wsResult.Value.SessionName, sourceDir).ConfigureAwait(true);
                        if (newSessionId != null)
                        {
                            this._interactionManager.LaunchSession(newSessionId);
                            await this.RefreshGridAsync().ConfigureAwait(true);
                        }
                        else
                        {
                            MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
        };

        this._sessionsVisuals.OnOpenTerminal += (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session == null || string.IsNullOrEmpty(session.Cwd))
            {
                return;
            }

            var proc = this._interactionManager.OpenTerminal(session.Cwd, sid);
            if (proc != null)
            {
                this.RefreshActiveStatusAsync();
            }
        };

        this._sessionsVisuals.OnOpenInIde += (sid, capturedIde, useRepoRoot) =>
        {
            if (this._activeTracker.TryFocusExistingIde(sid, capturedIde.Description))
            {
                return;
            }

            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session == null || string.IsNullOrEmpty(session.Cwd))
            {
                return;
            }

            var targetPath = useRepoRoot ? SessionService.FindGitRoot(session.Cwd) : session.Cwd;
            if (targetPath == null)
            {
                return;
            }

            var proc = SessionInteractionManager.OpenInIde(capturedIde.Path, targetPath);
            if (proc != null)
            {
                this._activeTracker.TrackProcess(sid, new ActiveProcess(capturedIde.Description, proc.Id, targetPath));
                this.RefreshActiveStatusAsync();
            }
        };

        this._sessionsVisuals.OnOpenEdge += async (sid) =>
        {
            if (this._activeTracker.TryGetEdge(sid, out var existing) && existing.IsOpen)
            {
                existing.Focus();
                return;
            }

            var workspace = SessionInteractionManager.CreateEdgeWorkspace(sid);
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

        this._sessionsVisuals.GetGitRootInfo = (sessionId) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sessionId);
            if (session != null && !string.IsNullOrEmpty(session.Cwd))
            {
                var repoRoot = SessionService.FindGitRoot(session.Cwd);
                var hasGitRoot = repoRoot != null;
                var isSubfolder = hasGitRoot && !string.Equals(repoRoot, session.Cwd, StringComparison.OrdinalIgnoreCase);
                return (hasGitRoot, isSubfolder);
            }
            return (false, false);
        };
    }

    private void BuildSettingsTab()
    {
        var settingsContainer = new Panel { Dock = DockStyle.Fill };
        var settingsTabs = new TabControl { Dock = DockStyle.Fill };
        if (!Application.IsDarkModeEnabled)
        {
            settingsTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            settingsTabs.DrawItem += (s, e) =>
            {
                bool selected = e.Index == settingsTabs.SelectedIndex;
                var back = selected ? SystemColors.Window : Color.FromArgb(220, 220, 220);
                var fore = SystemColors.ControlText;
                using var brush = new SolidBrush(back);
                e.Graphics.FillRectangle(brush, e.Bounds);
                var text = settingsTabs.TabPages[e.Index].Text;
                TextRenderer.DrawText(e.Graphics, text, settingsTabs.Font, e.Bounds, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }

        // Allowed Tools
        var toolsTab = new TabPage("Allowed Tools");
        this._toolsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = Application.IsDarkModeEnabled ? BorderStyle.None : BorderStyle.Fixed3D };
        SettingsTabBuilder.ApplyThemedSelection(this._toolsList);
        foreach (var tool in Program._settings.AllowedTools)
        {
            this._toolsList.Items.Add(tool);
        }
        var toolsButtons = SettingsTabBuilder.CreateListButtons(this._toolsList, "Tool name:", "Add Tool", addBrowse: false);
        toolsTab.Controls.Add(this._toolsList);
        toolsTab.Controls.Add(toolsButtons);

        // Allowed Directories
        var dirsTab = new TabPage("Allowed Directories");
        this._dirsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = Application.IsDarkModeEnabled ? BorderStyle.None : BorderStyle.Fixed3D };
        SettingsTabBuilder.ApplyThemedSelection(this._dirsList);
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
            GridLines = !Application.IsDarkModeEnabled
        };
        this._idesList.Columns.Add("Description", 200);
        this._idesList.Columns.Add("Path", 400);
        foreach (var ide in Program._settings.Ides)
        {
            var item = new ListViewItem(ide.Description);
            item.SubItems.Add(ide.Path);
            this._idesList.Items.Add(item);
        }
        SettingsTabBuilder.ApplyThemedSelection(this._idesList);
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
        workDirPanel.Controls.AddRange([workDirLabel, SettingsTabBuilder.WrapWithBorder(this._workDirBox), workDirBrowse]);

        // Notifications
        var notifyPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        this._notifyOnBellCheck = new CheckBox
        {
            Text = "Notify when session is ready (\U0001F514)",
            Checked = Program._settings.NotifyOnBell,
            AutoSize = true,
            Location = new Point(8, 5)
        };
        notifyPanel.Controls.Add(this._notifyOnBellCheck);

        // Theme
        var themePanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        var themeLabel = new Label { Text = "Theme:", AutoSize = true, Location = new Point(8, 7) };
        this._themeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(130, 4),
            Width = 150
        };
        this._themeCombo.Items.AddRange(new object[] { "System (default)", "Light", "Dark" });
        this._themeCombo.SelectedIndex = ThemeService.ThemeToIndex(Program._settings.Theme);
        this._themeCombo.SelectedIndexChanged += (s, e) =>
        {
            if (this._suppressThemeChange)
            {
                return;
            }

            var theme = ThemeService.IndexToTheme(this._themeCombo.SelectedIndex);
            if (theme == Program._settings.Theme)
            {
                return;
            }

            var result = MessageBox.Show(
                "Theme changed. The application needs to restart to apply the new theme.\n\nRestart now?",
                "Copilot Booster",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                Program._settings.Theme = theme;
                Program._settings.Save();
                Application.Restart();
                Environment.Exit(0);
            }
            else
            {
                this._suppressThemeChange = true;
                this._themeCombo.SelectedIndex = ThemeService.ThemeToIndex(Program._settings.Theme);
                this._suppressThemeChange = false;
            }
        };
        themePanel.Controls.AddRange([themeLabel, this._themeCombo]);

        // Bottom buttons
        var settingsBottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 6, 8, 6)
        };

        var btnCancel = new Button { Text = "Cancel", Width = 90 };
        btnCancel.Click += (s, e) =>
        {
            SettingsTabBuilder.ReloadSettingsUI(this._toolsList, this._dirsList, this._idesList, this._workDirBox, this._themeCombo);
            this._notifyOnBellCheck.Checked = Program._settings.NotifyOnBell;
            this._suppressThemeChange = true;
            this._themeCombo.SelectedIndex = ThemeService.ThemeToIndex(Program._settings.Theme);
            this._suppressThemeChange = false;
        };

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

            Program._settings.NotifyOnBell = this._notifyOnBellCheck.Checked;
            Program._settings.Save();
            MessageBox.Show("Settings saved.", "Copilot Booster", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        settingsBottomPanel.Controls.Add(btnCancel);
        settingsBottomPanel.Controls.Add(btnSave);

        settingsContainer.Controls.Add(settingsTabs);
        settingsContainer.Controls.Add(themePanel);
        settingsContainer.Controls.Add(notifyPanel);
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
            GridLines = !Application.IsDarkModeEnabled
        };
        this._cwdListView.Columns.Add("Directory", 350);
        this._cwdListView.Columns.Add("# Sessions created ▼", 120, HorizontalAlignment.Center);
        this._cwdListView.Columns.Add("Git", 50, HorizontalAlignment.Center);
        this._cwdListView.ListViewItemSorter = this._newSessionTabBuilder.Sorter;
        this._cwdListView.ColumnClick += (s, e) => this._newSessionTabBuilder.OnColumnClick(this._cwdListView, e);
        SettingsTabBuilder.ApplyThemedSelection(this._cwdListView);

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

                var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(selectedCwd, sessionName, CopilotSessionCreatorService.FindTemplateSessionDir()).ConfigureAwait(true);
                if (newSessionId != null)
                {
                    this._interactionManager.LaunchSession(newSessionId);
                    await this.RefreshGridAsync().ConfigureAwait(true);
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
                    var wsResult = WorkspaceCreatorVisuals.ShowWorkspaceCreator(gitRoot);
                    if (wsResult != null)
                    {
                        var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(wsResult.Value.WorktreePath, wsResult.Value.SessionName, CopilotSessionCreatorService.FindTemplateSessionDir()).ConfigureAwait(true);
                        if (newSessionId != null)
                        {
                            this._interactionManager.LaunchSession(newSessionId);
                            await this.RefreshGridAsync().ConfigureAwait(true);
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

        var menuOpenExplorer = new ToolStripMenuItem("Open in Explorer");
        menuOpenExplorer.Click += (s, e) =>
        {
            if (this._cwdListView.SelectedItems.Count > 0
                && this._cwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                SessionInteractionManager.OpenExplorer(selectedCwd);
            }
        };
        cwdContextMenu.Items.Add(menuOpenExplorer);

        var menuOpenTerminalCwd = new ToolStripMenuItem("Open Terminal");
        menuOpenTerminalCwd.Click += (s, e) =>
        {
            if (this._cwdListView.SelectedItems.Count > 0
                && this._cwdListView.SelectedItems[0].Tag is string selectedCwd)
            {
                SessionInteractionManager.OpenTerminalSimple(selectedCwd);
            }
        };
        cwdContextMenu.Items.Add(menuOpenTerminalCwd);

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
            menuOpenExplorer.Visible = hasSelection;
            menuOpenTerminalCwd.Visible = hasSelection;
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

                var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(selectedCwd, sessionName, CopilotSessionCreatorService.FindTemplateSessionDir()).ConfigureAwait(true);
                if (newSessionId != null)
                {
                    this._interactionManager.LaunchSession(newSessionId);
                    await this.RefreshGridAsync().ConfigureAwait(true);
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
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14f, FontStyle.Regular)
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

        this._spinnerTimer = new Timer { Interval = 100 };
        this._spinnerTimer.Tick += (s, e) => this._sessionsVisuals.GridVisuals.AdvanceSpinnerFrame();
        this._spinnerTimer.Start();

        this.Shown += async (s, e) =>
        {
            await this.LoadInitialDataAsync().ConfigureAwait(true);
            _ = this.CheckForUpdateInBackgroundAsync();
        };
        this.FormClosed += (s, e) =>
        {
            this._activeStatusTimer?.Stop();
            this._spinnerTimer?.Stop();
        };
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
            this.Invoke(() =>
            {
                this._forceClose = true;
                Application.Exit();
            });
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
            this._interactionManager.LaunchSession(this.SelectedSessionId);
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
            this._sessionsVisuals.GridVisuals.Populate(this._cachedSessions, snapshot, this._sessionsVisuals.SearchBox.Text);
        }

        if (this.WindowState == FormWindowState.Minimized)
        {
            this.WindowState = FormWindowState.Normal;
        }

        // Restore from tray if hidden
        if (!this.Visible)
        {
            this.Show();
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
            this._sessionsVisuals.GridVisuals.ApplySnapshot(this._cachedSessions, snapshot, this._sessionsVisuals.SearchBox.Text);

            // Bell notification: detect transitions and fire toast
            this._bellService?.CheckAndNotify(snapshot);
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

        // Seed startup Copilot CLI sessions — suppress bell until they transition to working
        var copilotCliSessionIds = snapshot.StatusIconBySessionId
            .Where(kvp => kvp.Value == "bell")
            .Select(kvp => kvp.Key);
        this._activeTracker.InitStartedSessions(copilotCliSessionIds);
        this._bellService?.SeedStartupSessions(copilotCliSessionIds);

        // Re-run refresh with started sessions seeded (bells now suppressed)
        snapshot = await Task.Run(() => this._activeTracker.Refresh(this._cachedSessions)).ConfigureAwait(true);

        this._sessionsVisuals.GridVisuals.Populate(this._cachedSessions, snapshot, this._sessionsVisuals.SearchBox.Text);
        this._sessionsVisuals.LoadingOverlay.Visible = false;

        await this.RefreshNewSessionListAsync().ConfigureAwait(true);
    }

    private async Task RefreshGridAsync()
    {
        this._cachedSessions = await Task.Run(() => LoadNamedSessions()).ConfigureAwait(true);
        var snapshot = this._activeTracker.Refresh(this._cachedSessions);
        this._sessionsVisuals.GridVisuals.Populate(this._cachedSessions, snapshot, this._sessionsVisuals.SearchBox.Text);
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
