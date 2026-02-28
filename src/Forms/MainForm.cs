using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopilotBooster.Models;
using CopilotBooster.Services;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Forms;

/// <summary>
/// Main application form providing session management and settings configuration.
/// </summary>
[ExcludeFromCodeCoverage]
internal partial class MainForm : Form
{
    private readonly Panel _sessionsPanel;

    // Sessions tab controls
    private readonly ExistingSessionsVisuals _sessionsVisuals = null!;
    private List<NamedSession> _cachedSessions = new();
    private ActiveStatusSnapshot _lastSnapshot = new([], [], []);
    private readonly ActiveStatusTracker _activeTracker = new();
    private readonly SessionRefreshCoordinator _refreshCoordinator;
    private readonly SessionInteractionManager _interactionManager;
    private System.Windows.Forms.Timer? _activeStatusTimer;
    private System.Windows.Forms.Timer? _spinnerTimer;
    private BellNotificationService? _bellService;

    // New Session support
    private readonly SessionDataService _sessionDataService = new();

    // Settings tab controls (created inside dialog)
    private bool _suppressThemeChange;

    // Update banner
    private LinkLabel _updateLabel = null!;
    private readonly ToastPanel _toast = null!;
    private UpdateInfo? _latestUpdate;
    private System.Windows.Forms.Timer? _updateCheckTimer;

    // System tray
    private NotifyIcon? _trayIcon;
    private bool _forceClose;

    // Toast window mode
    private GlobalHotkeyService? _hotkeyService;
    private System.Windows.Forms.Timer? _toastAnimTimer;
    private int _toastTargetTop;
    private bool _toastVisible;
    private bool _toastAnimating;
    private long _toastShownTicks;

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
        this._refreshCoordinator = new SessionRefreshCoordinator(Program.SessionStateDir, Program.PidRegistryFile, this._activeTracker);
        this._activeTracker.EventsJournal.LoadCache();
        this._activeTracker.EventsJournal.StatusChanged += this.OnEventsStatusChanged;
        this._activeTracker.EventsJournal.StartWatching();

        this._sessionsPanel = new Panel { Dock = DockStyle.Fill };

        this._sessionsVisuals = new ExistingSessionsVisuals(this._sessionsPanel, this._activeTracker);
        this._toast = ToastPanel.AttachTo(this._sessionsPanel);
        this.WireSessionsEvents();
        this.SetupUpdateBanner();
        this.SetupTrayIcon();

        this.Controls.Add(this._sessionsPanel);
        this.Controls.Add(this._updateLabel);

        SetDoubleBuffered(this);

        this.SetupTimersAndEvents(initialTab);
        this.SetupToastMode();
    }

    private void InitializeFormProperties()
    {
        this.Text = "Copilot Booster";
        this.Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f);
        this.Size = new Size(1000, 550);
        this.MinimumSize = new Size(550, 400);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.TopMost = Program._settings.AlwaysOnTop;
        this.DoubleBuffered = true;

        if (Application.IsDarkModeEnabled)
        {
            this.BackColor = Color.FromArgb(0x1E, 0x1E, 0x1E);
        }

        if (Program.AppIcon != null)
        {
            this.Icon = Program.AppIcon;
        }
    }

    // Flicker prevention — set DoubleBuffered on controls via reflection
    private static void SetDoubleBuffered(Control control)
    {
        var prop = typeof(Control).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        prop?.SetValue(control, true);

        foreach (Control child in control.Controls)
        {
            SetDoubleBuffered(child);
        }
    }

    private void SetupTrayIcon()
    {
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show", null, (s, e) => this.RestoreFromTray());
        trayMenu.Items.Add("Settings", null, (s, e) => this.ShowSettingsDialog());
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
                trayIconImage = Program.AppIcon;
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to load tray icon: {Error}", ex.Message);
        }

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

    private void ShowSettingsDialog()
    {
        this.RestoreFromTray();
        this.BuildAndShowSettingsDialog();
    }

    /// <summary>
    /// Restores the window from the system tray. Uses toast positioning when toast mode is active.
    /// </summary>
    private void RestoreFromTray()
    {
        if (Program._settings.ToastMode && (!this.Visible || this.WindowState == FormWindowState.Minimized))
        {
            this.ShowToastAtCursor();
            return;
        }

        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.Activate();
    }

    /// <summary>
    /// Initializes the global hotkey for toast mode if enabled in settings.
    /// </summary>
    private void SetupToastMode()
    {
        if (!Program._settings.ToastMode)
        {
            return;
        }

        this._hotkeyService = new GlobalHotkeyService();
        if (!this._hotkeyService.Register())
        {
            Program.Logger.LogWarning("Failed to register toast mode hotkey (Win+Alt+X)");
            this._hotkeyService = null;
            return;
        }

        this._hotkeyService.HotkeyPressed += this.OnToastHotkeyPressed;

        this._toastAnimTimer = new System.Windows.Forms.Timer { Interval = 15 };
        this._toastAnimTimer.Tick += this.OnToastAnimationTick;

        this.Deactivate += this.OnToastDeactivate;
        this._toastVisible = false;
    }

    private void OnToastHotkeyPressed()
    {
        if (this.InvokeRequired)
        {
            this.BeginInvoke(this.OnToastHotkeyPressed);
            return;
        }

        if (this._toastVisible)
        {
            this.HideToast();
        }
        else
        {
            this.ShowToast();
        }
    }

    private void ShowToast() => this.ShowToast(GetToastScreen());

    private void ShowToastAtCursor() => this.ShowToast(Screen.FromPoint(Cursor.Position));

    /// <summary>
    /// Calculates the toast target position and animation start position for the given work area.
    /// </summary>
    internal static (Point Target, Point AnimStart, bool FromBottom) CalculateToastPosition(
        Rectangle workArea, Size windowSize, string position)
    {
        int targetLeft = position switch
        {
            "bottom-left" or "top-left" => workArea.Left,
            "bottom-right" or "top-right" => workArea.Right - windowSize.Width,
            _ => workArea.Left + (workArea.Width - windowSize.Width) / 2
        };

        bool fromBottom = position.StartsWith("bottom", StringComparison.OrdinalIgnoreCase);
        int targetTop = fromBottom
            ? workArea.Bottom - windowSize.Height
            : workArea.Top;

        int startTop = fromBottom ? workArea.Bottom : workArea.Top - windowSize.Height;

        return (new Point(targetLeft, targetTop), new Point(targetLeft, startTop), fromBottom);
    }

    private void ShowToast(Screen screen)
    {
        var workArea = screen.WorkingArea;
        var pos = Program._settings.ToastPosition;
        var (target, animStart, _) = CalculateToastPosition(workArea, this.Size, pos);

        // Hide first to prevent flash when restoring from minimized
        bool wasMinimized = this.WindowState == FormWindowState.Minimized;
        if (wasMinimized)
        {
            this.Visible = false;
        }

        this.StartPosition = FormStartPosition.Manual;
        this.WindowState = FormWindowState.Normal;

        if (Program._settings.ToastAnimate)
        {
            this.Location = animStart;
            this._toastTargetTop = target.Y;

            this.Show();
            this.Activate();
            this._toastAnimating = true;
            this._toastAnimTimer?.Start();
        }
        else
        {
            this.Location = target;
            this.Show();
            this.Activate();
        }

        this._toastVisible = true;
        this._toastShownTicks = Environment.TickCount64;
    }

    private void HideToast()
    {
        if (this._toastAnimating)
        {
            this._toastAnimTimer?.Stop();
            this._toastAnimating = false;
        }

        if (Program._settings.ToastAnimate)
        {
            var screen = GetToastScreen();
            var workArea = screen.WorkingArea;
            bool fromBottom = Program._settings.ToastPosition.StartsWith("bottom", StringComparison.OrdinalIgnoreCase);
            this._toastTargetTop = fromBottom ? workArea.Bottom : workArea.Top - this.Height;
            this._toastAnimating = true;
            this._toastAnimTimer?.Start();
        }
        else
        {
            this.Hide();
            this.WindowState = FormWindowState.Minimized;
        }

        this._toastVisible = false;
    }

    private void OnToastAnimationTick(object? sender, EventArgs e)
    {
        int step = Math.Max(1, Math.Abs(this._toastTargetTop - this.Top) / 4);
        if (this.Top < this._toastTargetTop)
        {
            this.Top = Math.Min(this.Top + step, this._toastTargetTop);
        }
        else if (this.Top > this._toastTargetTop)
        {
            this.Top = Math.Max(this.Top - step, this._toastTargetTop);
        }

        if (this.Top == this._toastTargetTop)
        {
            this._toastAnimTimer?.Stop();
            this._toastAnimating = false;

            // If we animated to off-screen, hide the form
            if (!this._toastVisible)
            {
                this.Hide();
                this.WindowState = FormWindowState.Minimized;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private void OnToastDeactivate(object? sender, EventArgs e)
    {
        if (!Program._settings.ToastMode || !this._toastVisible || this._toastAnimating)
        {
            return;
        }

        // Ignore deactivation when being minimized by taskbar click
        if (this.WindowState == FormWindowState.Minimized)
        {
            this._toastVisible = false;
            return;
        }

        // Ignore deactivation within 500ms of showing (prevents rapid show/hide from taskbar clicks)
        if (Environment.TickCount64 - this._toastShownTicks < 500)
        {
            return;
        }

        // Don't hide if focus went to a window owned by our process (dialogs, context menus, etc.)
        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            GetWindowThreadProcessId(foreground, out uint pid);
            if (pid == (uint)Environment.ProcessId)
            {
                return;
            }
        }

        this.HideToast();
    }

    /// <summary>
    /// Intercepts SC_RESTORE from taskbar clicks to re-trigger toast animation
    /// instead of letting Windows restore to the last position.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_RESTORE = 0xF120;

        if (m.Msg == WM_SYSCOMMAND
            && (m.WParam.ToInt32() & 0xFFF0) == SC_RESTORE
            && Program._settings.ToastMode
            && this.WindowState == FormWindowState.Minimized)
        {
            // Let Windows handle the restore, then reposition with toast animation
            base.WndProc(ref m);
            this.BeginInvoke(this.ShowToastAtCursor);
            return;
        }

        base.WndProc(ref m);
    }

    private static Screen GetToastScreen()
    {
        var setting = Program._settings.ToastScreen;

        if (string.Equals(setting, "cursor", StringComparison.OrdinalIgnoreCase))
        {
            return Screen.FromPoint(Cursor.Position);
        }

        if (setting.StartsWith("screen-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(setting.AsSpan(7), out int idx)
            && idx >= 0 && idx < Screen.AllScreens.Length)
        {
            return Screen.AllScreens[idx];
        }

        return Screen.PrimaryScreen ?? Screen.AllScreens[0];
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

        this._hotkeyService?.Dispose();

        if (this._trayIcon != null)
        {
            this._trayIcon.Visible = false;
            this._trayIcon.Dispose();
            this._trayIcon = null;
        }

        this._activeTracker.EventsJournal.SaveCache();
        this._activeTracker.EventsJournal.Dispose();

        base.OnFormClosing(e);
    }

    private void WireSessionsEvents()
    {
        this._sessionsVisuals.OnRefreshRequested += async () =>
        {
            this._cachedSessions = (List<NamedSession>)await Task.Run(() => this._refreshCoordinator.LoadSessions()).ConfigureAwait(true);
            this.ApplySessionStates(this._cachedSessions);
            var snapshot = await Task.Run(() => this._refreshCoordinator.RefreshActiveStatus(this._cachedSessions)).ConfigureAwait(true);
            this.PopulateGridWithFilter(snapshot);
        };

        this._sessionsVisuals.OnSearchChanged += () =>
        {
            this.PopulateGridWithFilter(this._lastSnapshot);
        };

        this._sessionsVisuals.OnTabChanged += () =>
        {
            this.SuspendLayout();
            this.PopulateGridWithFilter(this._lastSnapshot);
            this.ResumeLayout(true);
        };

        this._sessionsVisuals.OnNewSessionClicked += () =>
        {
            this.ShowNewSessionDialogAsync();
        };

        this._sessionsVisuals.OnSettingsClicked += () =>
        {
            this.ShowSettingsDialog();
        };

        this._sessionsVisuals.OnSessionDoubleClicked += (sid) =>
        {
            this.SelectedSessionId = sid;
            this.LaunchSession();
        };

        this.WireContextMenuEvents();
    }

    private void BuildAndShowSettingsDialog()
    {
        var dialog = new Form
        {
            Text = "Settings",
            Size = new Size(700, 600),
            MinimumSize = new Size(500, 480),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            Font = this.Font,
            Icon = this.Icon,
            TopMost = this.TopMost
        };

        var settingsContainer = new Panel { Dock = DockStyle.Fill };
        var settingsTabs = new TabControl { Dock = DockStyle.Fill, ShowToolTips = true };
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
        var toolsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = Application.IsDarkModeEnabled ? BorderStyle.None : BorderStyle.Fixed3D };
        SettingsVisuals.ApplyThemedSelection(toolsList);
        foreach (var tool in Program._settings.AllowedTools)
        {
            toolsList.Items.Add(tool);
        }
        var toolsButtons = SettingsVisuals.CreateListButtons(toolsList, "Tool name:", "Add Tool", addBrowse: false);
        toolsTab.Controls.Add(toolsList);
        toolsTab.Controls.Add(toolsButtons);
        SettingsVisuals.ApplyTabInfo(toolsTab, "CLI tools the Copilot session is allowed to use.", "CLI tools the Copilot session is allowed to use (e.g., git, npm, dotnet).");

        // Allowed Directories
        var dirsTab = new TabPage("Allowed Directories");
        var dirsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = Application.IsDarkModeEnabled ? BorderStyle.None : BorderStyle.Fixed3D };
        SettingsVisuals.ApplyThemedSelection(dirsList);
        foreach (var dir in Program._settings.AllowedDirs)
        {
            dirsList.Items.Add(dir);
        }
        var dirsButtons = SettingsVisuals.CreateListButtons(dirsList, "Directory path:", "Add Directory", addBrowse: true);
        dirsTab.Controls.Add(dirsList);
        dirsTab.Controls.Add(dirsButtons);
        SettingsVisuals.ApplyTabInfo(dirsTab, "Directories the Copilot session is allowed to access.", "Directories the Copilot session is allowed to access for file operations.");

        // Allowed URLs (global — stored in ~/.copilot/config.json)
        var urlsTab = new TabPage("Allowed URLs");
        var urlsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = Application.IsDarkModeEnabled ? BorderStyle.None : BorderStyle.Fixed3D };
        SettingsVisuals.ApplyThemedSelection(urlsList);
        foreach (var url in CopilotConfigService.LoadAllowedUrls())
        {
            urlsList.Items.Add(url);
        }
        var urlsButtons = SettingsVisuals.CreateListButtons(urlsList, "URL or domain pattern:", "Add URL", addBrowse: false);
        urlsTab.Controls.Add(urlsList);
        urlsTab.Controls.Add(urlsButtons);
        SettingsVisuals.ApplyTabInfo(urlsTab, "URLs allowed for web access (global Copilot CLI setting).", "URL patterns the Copilot CLI is allowed to access (e.g., https://github.com, https://*.example.com).");

        // IDEs
        var idesTab = new TabPage("IDEs");
        var idesList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = !Application.IsDarkModeEnabled
        };
        idesList.Columns.Add("Description", 150);
        idesList.Columns.Add("Path", 300);
        idesList.Columns.Add("File Pattern", 120);
        foreach (var ide in Program._settings.Ides)
        {
            var item = new ListViewItem(ide.Description);
            item.SubItems.Add(ide.Path);
            item.SubItems.Add(ide.FilePattern);
            idesList.Items.Add(item);
        }
        SettingsVisuals.ApplyThemedSelection(idesList);
        var ideButtons = SettingsVisuals.CreateIdeButtons(idesList);
        idesTab.Controls.Add(idesList);
        idesTab.Controls.Add(ideButtons);
        SettingsVisuals.ApplyTabInfo(idesTab, "IDEs available in the session context menu.", "IDEs available in the context menu.");

        settingsTabs.TabPages.Add(toolsTab);
        settingsTabs.TabPages.Add(dirsTab);
        settingsTabs.TabPages.Add(urlsTab);
        settingsTabs.TabPages.Add(idesTab);

        // IDE Search Ignored Dirs tab
        var ideSearchTab = new TabPage("IDE Search");
        var ignoredDirsList = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };
        foreach (var dir in Program._settings.IdeSearchIgnoredDirs)
        {
            ignoredDirsList.Items.Add(dir);
        }
        var ignoredDirsButtons = SettingsVisuals.CreateListButtons(ignoredDirsList, "Directory name to ignore:", "Add Ignored Directory", false);
        ideSearchTab.Controls.Add(ignoredDirsList);
        ideSearchTab.Controls.Add(ignoredDirsButtons);
        SettingsVisuals.ApplyTabInfo(ideSearchTab, "Directory names excluded from IDE file pattern search.", "Directory names to skip when searching for project files (e.g., node_modules, bin, obj).");
        settingsTabs.TabPages.Add(ideSearchTab);

        // Session Tabs
        var sessionTabsTab = new TabPage("Session Tabs");
        var sessionTabsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = Application.IsDarkModeEnabled ? BorderStyle.None : BorderStyle.Fixed3D };
        SettingsVisuals.ApplyThemedSelection(sessionTabsList);
        foreach (var tab in Program._settings.SessionTabs)
        {
            sessionTabsList.Items.Add(tab);
        }

        var sessionTabsButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 35, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4) };
        var btnAddTab = new Button { Text = "Add", Width = 70 };
        btnAddTab.Click += (s, e) =>
        {
            if (sessionTabsList.Items.Count >= Program._settings.MaxSessionTabs)
            {
                MessageBox.Show($"Maximum of {Program._settings.MaxSessionTabs} tabs allowed.", "Limit Reached", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            // Check for duplicate
            foreach (var existing in sessionTabsList.Items.Cast<string>())
            {
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("A tab with that name already exists.", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            sessionTabsList.Items.Add(name);
        };

        var btnRenameTab = new Button { Text = "Rename", Width = 70 };
        btnRenameTab.Click += (s, e) =>
        {
            if (sessionTabsList.SelectedIndex < 0)
            {
                return;
            }

            var oldName = sessionTabsList.SelectedItem!.ToString()!;
            var newName = SettingsVisuals.PromptInput("Rename Tab", "New tab name (max 20 chars):", oldName);
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName.Trim(), oldName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            newName = newName.Trim();
            if (newName.Length > 20)
            {
                newName = newName[..20];
            }

            // Check for duplicate
            foreach (var existing in sessionTabsList.Items.Cast<string>())
            {
                if (string.Equals(existing, newName, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("A tab with that name already exists.", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            sessionTabsList.Items[sessionTabsList.SelectedIndex] = newName;
            SessionArchiveService.RenameTab(Program.SessionStateFile, oldName, newName);

            // Keep DefaultTab in sync when the default tab is renamed
            if (string.Equals(Program._settings.DefaultTab, oldName, StringComparison.OrdinalIgnoreCase))
            {
                Program._settings.DefaultTab = newName;
            }
        };

        var btnRemoveTab = new Button { Text = "Remove", Width = 70, Enabled = false };
        btnRemoveTab.Click += (s, e) =>
        {
            if (sessionTabsList.SelectedIndex < 0 || sessionTabsList.Items.Count <= 1)
            {
                return;
            }

            var tabName = sessionTabsList.SelectedItem!.ToString()!;
            var hasSession = this._cachedSessions.Any(x => string.Equals(x.Tab, tabName, StringComparison.OrdinalIgnoreCase));
            if (hasSession)
            {
                MessageBox.Show("Cannot remove a tab that still has sessions. Move all sessions first.", "Tab Not Empty", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            sessionTabsList.Items.RemoveAt(sessionTabsList.SelectedIndex);
            btnRemoveTab.Enabled = sessionTabsList.Items.Count > 1 && sessionTabsList.SelectedIndex >= 0;
        };

        sessionTabsList.SelectedIndexChanged += (s, e) =>
        {
            btnRemoveTab.Enabled = sessionTabsList.Items.Count > 1 && sessionTabsList.SelectedIndex >= 0;
        };

        var btnMoveUp = new Button { Text = "▲ Up", Width = 70 };
        btnMoveUp.Click += (s, e) =>
        {
            var idx = sessionTabsList.SelectedIndex;
            if (idx <= 0)
            {
                return;
            }

            var item = sessionTabsList.Items[idx];
            sessionTabsList.Items.RemoveAt(idx);
            sessionTabsList.Items.Insert(idx - 1, item);
            sessionTabsList.SelectedIndex = idx - 1;
        };

        var btnMoveDown = new Button { Text = "▼ Down", Width = 70 };
        btnMoveDown.Click += (s, e) =>
        {
            var idx = sessionTabsList.SelectedIndex;
            if (idx < 0 || idx >= sessionTabsList.Items.Count - 1)
            {
                return;
            }

            var item = sessionTabsList.Items[idx];
            sessionTabsList.Items.RemoveAt(idx);
            sessionTabsList.Items.Insert(idx + 1, item);
            sessionTabsList.SelectedIndex = idx + 1;
        };

        sessionTabsButtons.Controls.AddRange([btnAddTab, btnRenameTab, btnRemoveTab, btnMoveUp, btnMoveDown]);
        sessionTabsTab.Controls.Add(sessionTabsList);
        sessionTabsTab.Controls.Add(sessionTabsButtons);
        SettingsVisuals.ApplyTabInfo(sessionTabsTab, "Organize sessions into tabs. At least one tab must remain.", "Tabs for grouping sessions. Max 20 characters per name.");
        settingsTabs.TabPages.Add(sessionTabsTab);

        // Default Work Dir
        var workDirPanel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 4) };
        var workDirLabel = new Label { Text = "Default Work Dir:", AutoSize = true, Location = new Point(8, 12) };
        var workDirBox = new TextBox
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
            using var fbd = new FolderBrowserDialog { SelectedPath = workDirBox.Text };
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                workDirBox.Text = fbd.SelectedPath;
            }
        };
        workDirPanel.Controls.AddRange([workDirLabel, SettingsVisuals.WrapWithBorder(workDirBox), workDirBrowse]);

        // Workspaces Dir
        var wsDirPanel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 4) };
        var wsDirLabel = new Label { Text = "Workspaces Dir:", AutoSize = true, Location = new Point(8, 12) };
        var wsDirBox = new TextBox
        {
            Text = Program._settings.WorkspacesDir,
            PlaceholderText = GitService.GetDefaultWorkspacesDir(),
            Location = new Point(130, 9),
            Width = 400,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        var wsDirBrowse = new Button
        {
            Text = "...",
            Width = 30,
            Location = new Point(535, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        wsDirBrowse.Click += (s, e) =>
        {
            using var fbd = new FolderBrowserDialog
            {
                SelectedPath = string.IsNullOrWhiteSpace(wsDirBox.Text) ? GitService.GetDefaultWorkspacesDir() : wsDirBox.Text
            };
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                wsDirBox.Text = fbd.SelectedPath;
            }
        };
        wsDirPanel.Controls.AddRange([wsDirLabel, SettingsVisuals.WrapWithBorder(wsDirBox), wsDirBrowse]);

        // Notifications
        var notifyPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        var notifyOnBellCheck = new CheckBox
        {
            Text = "Notify when session is ready (\U0001F514)",
            Checked = Program._settings.NotifyOnBell,
            AutoSize = true,
            Location = new Point(8, 5)
        };
        notifyPanel.Controls.Add(notifyOnBellCheck);

        // Auto-hide on focus
        var autoHidePanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        var autoHideOnFocusCheck = new CheckBox
        {
            Text = "Auto-hide other session windows on focus",
            Checked = Program._settings.AutoHideOnFocus,
            AutoSize = true,
            Location = new Point(8, 5)
        };
        autoHidePanel.Controls.Add(autoHideOnFocusCheck);

        // Always on top
        var alwaysOnTopPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        var alwaysOnTopCheck = new CheckBox
        {
            Text = "Always on top",
            Checked = Program._settings.AlwaysOnTop,
            AutoSize = true,
            Location = new Point(8, 5)
        };
        alwaysOnTopPanel.Controls.Add(alwaysOnTopCheck);

        // Update Edge tab on rename
        var edgeRenamePanel = new Panel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8, 4, 8, 4) };
        var edgeRenameCheck = new CheckBox
        {
            Text = "Update Edge tab on session rename",
            Checked = Program._settings.UpdateEdgeTabOnRename,
            AutoSize = true,
            Location = new Point(8, 5)
        };
        var edgeRenameInfo = new Label
        {
            Text = "When enabled, renaming a session will navigate to the Edge anchor tab to update its title.",
            AutoSize = true,
            Location = new Point(28, 25),
            ForeColor = SystemColors.GrayText,
            Font = new Font(this.Font.FontFamily, this.Font.Size - 1)
        };
        edgeRenamePanel.Controls.Add(edgeRenameCheck);
        edgeRenamePanel.Controls.Add(edgeRenameInfo);

        // Theme
        var themePanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        var themeLabel = new Label { Text = "Theme:", AutoSize = true, Location = new Point(8, 7) };
        var themeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(130, 4),
            Width = 150
        };
        themeCombo.Items.AddRange(new object[] { "System (default)", "Light", "Dark" });
        themeCombo.SelectedIndex = ThemeService.ThemeToIndex(Program._settings.Theme);
        themeCombo.SelectedIndexChanged += (s, e) =>
        {
            if (this._suppressThemeChange)
            {
                return;
            }

            var theme = ThemeService.IndexToTheme(themeCombo.SelectedIndex);
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
                themeCombo.SelectedIndex = ThemeService.ThemeToIndex(Program._settings.Theme);
                this._suppressThemeChange = false;
            }
        };
        themePanel.Controls.AddRange([themeLabel, themeCombo]);

        // Max active sessions
        var maxSessionsPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        var maxSessionsLabel = new Label { Text = "Max active sessions:", AutoSize = true, Location = new Point(8, 7) };
        var maxSessionsBox = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 1000,
            Value = Program._settings.MaxActiveSessions,
            Location = new Point(160, 4),
            Width = 70
        };
        var maxSessionsHint = new Label
        {
            Text = "(0 = unlimited)",
            AutoSize = true,
            Location = new Point(235, 7),
            ForeColor = Application.IsDarkModeEnabled ? Color.Gray : Color.DimGray
        };
        maxSessionsPanel.Controls.AddRange([maxSessionsLabel, maxSessionsBox, maxSessionsHint]);

        // Pinned order
        var pinnedOrderPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        var pinnedOrderLabel = new Label { Text = "Pinned order:", AutoSize = true, Location = new Point(8, 7) };
        var pinnedOrderCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(160, 4),
            Width = 180
        };
        pinnedOrderCombo.Items.AddRange(new object[] { "Running first (default)", "Last updated", "Alias / Name" });
        pinnedOrderCombo.SelectedIndex = string.Equals(Program._settings.PinnedOrder, "alias", StringComparison.OrdinalIgnoreCase) ? 2
            : string.Equals(Program._settings.PinnedOrder, "created", StringComparison.OrdinalIgnoreCase) ? 1
            : 0;
        pinnedOrderPanel.Controls.AddRange([pinnedOrderLabel, pinnedOrderCombo]);

        // Toast mode
        var toastModePanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        var toastModeCheck = new CheckBox
        {
            Text = "Toast mode (Win+Alt+X to show)",
            Checked = Program._settings.ToastMode,
            AutoSize = true,
            Location = new Point(8, 5)
        };
        toastModePanel.Controls.Add(toastModeCheck);

        // Toast position
        var toastPositionPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        var toastPositionLabel = new Label { Text = "Toast position:", AutoSize = true, Location = new Point(28, 7) };
        var toastPositionCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(160, 4),
            Width = 180
        };
        toastPositionCombo.Items.AddRange(new object[] { "Bottom Center", "Bottom Left", "Bottom Right", "Top Center", "Top Left", "Top Right" });
        toastPositionCombo.SelectedIndex = Program._settings.ToastPosition switch
        {
            "bottom-left" => 1,
            "bottom-right" => 2,
            "top-center" => 3,
            "top-left" => 4,
            "top-right" => 5,
            _ => 0
        };
        toastPositionPanel.Controls.AddRange([toastPositionLabel, toastPositionCombo]);

        // Toast screen
        var toastScreenPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        var toastScreenLabel = new Label { Text = "Toast screen:", AutoSize = true, Location = new Point(28, 7) };
        var toastScreenCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(160, 4),
            Width = 180
        };
        toastScreenCombo.Items.Add("Cursor");
        // Add each screen with its Windows display number
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var label = screens[i].Primary ? $"Primary: {i + 1}" : $"Secondary: {i + 1}";
            toastScreenCombo.Items.Add(label);
        }

        // Resolve selected index from setting
        if (string.Equals(Program._settings.ToastScreen, "cursor", StringComparison.OrdinalIgnoreCase))
        {
            toastScreenCombo.SelectedIndex = 0;
        }
        else if (Program._settings.ToastScreen.StartsWith("screen-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(Program._settings.ToastScreen.AsSpan(7), out int screenIdx)
            && screenIdx >= 0 && screenIdx < screens.Length)
        {
            toastScreenCombo.SelectedIndex = screenIdx + 1; // +1 because index 0 is "Cursor"
        }
        else
        {
            // "primary" — find the primary screen index
            var primaryIdx = Array.FindIndex(screens, s => s.Primary);
            toastScreenCombo.SelectedIndex = (primaryIdx >= 0 ? primaryIdx : 0) + 1;
        }
        toastScreenPanel.Controls.AddRange([toastScreenLabel, toastScreenCombo]);

        // Toast animate
        var toastAnimatePanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 4) };
        var toastAnimateCheck = new CheckBox
        {
            Text = "Slide animation",
            Checked = Program._settings.ToastAnimate,
            AutoSize = true,
            Location = new Point(28, 5)
        };
        toastAnimatePanel.Controls.Add(toastAnimateCheck);

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
            dialog.Close();
        };

        var btnSave = new Button { Text = "Save", Width = 90 };
        btnSave.Click += (s, e) =>
        {
            Program._settings.AllowedTools = toolsList.Items.Cast<string>().ToList();
            Program._settings.AllowedDirs = dirsList.Items.Cast<string>().ToList();
            CopilotConfigService.SaveAllowedUrls(urlsList.Items.Cast<string>().ToList());
            Program._settings.DefaultWorkDir = workDirBox.Text.Trim();
            Program._settings.WorkspacesDir = wsDirBox.Text.Trim();
            Program._settings.Ides = [];
            foreach (ListViewItem item in idesList.Items)
            {
                Program._settings.Ides.Add(new IdeEntry
                {
                    Description = item.Text,
                    Path = item.SubItems[1].Text,
                    FilePattern = item.SubItems.Count > 2 ? item.SubItems[2].Text : ""
                });
            }

            Program._settings.NotifyOnBell = notifyOnBellCheck.Checked;
            Program._settings.AutoHideOnFocus = autoHideOnFocusCheck.Checked;
            Program._settings.AlwaysOnTop = alwaysOnTopCheck.Checked;
            Program._settings.UpdateEdgeTabOnRename = edgeRenameCheck.Checked;
            Program._settings.IdeSearchIgnoredDirs = ignoredDirsList.Items.Cast<string>().ToList();
            Program._settings.MaxActiveSessions = (int)maxSessionsBox.Value;
            Program._settings.PinnedOrder = pinnedOrderCombo.SelectedIndex switch
            {
                1 => "created",
                2 => "alias",
                _ => "running"
            };
            Program._settings.SessionTabs = sessionTabsList.Items.Cast<string>().ToList();
            Program._settings.ToastMode = toastModeCheck.Checked;
            Program._settings.ToastPosition = toastPositionCombo.SelectedIndex switch
            {
                1 => "bottom-left",
                2 => "bottom-right",
                3 => "top-center",
                4 => "top-left",
                5 => "top-right",
                _ => "bottom-center"
            };
            Program._settings.ToastScreen = toastScreenCombo.SelectedIndex == 0
                ? "cursor"
                : $"screen-{toastScreenCombo.SelectedIndex - 1}";
            Program._settings.ToastAnimate = toastAnimateCheck.Checked;
            this.TopMost = Program._settings.AlwaysOnTop;
            Program._settings.Save();
            this._sessionsVisuals.BuildSessionTabs();
            this._sessionsVisuals.BuildGridContextMenu();
            this.ApplySessionStates(this._cachedSessions);
            this.PopulateGridWithFilter(this._lastSnapshot);
            dialog.Close();
            this._toast.Show("✅ Settings saved successfully");
        };

        var btnAbout = new Button { Text = "About", Width = 90 };
        btnAbout.Click += (s, e) =>
        {
            AboutDialog.Show(dialog, this._latestUpdate);
        };

        settingsBottomPanel.Controls.Add(btnCancel);
        settingsBottomPanel.Controls.Add(btnSave);
        settingsBottomPanel.Controls.Add(btnAbout);

        settingsContainer.Controls.Add(settingsTabs);
        settingsContainer.Controls.Add(toastAnimatePanel);
        settingsContainer.Controls.Add(toastScreenPanel);
        settingsContainer.Controls.Add(toastPositionPanel);
        settingsContainer.Controls.Add(toastModePanel);
        settingsContainer.Controls.Add(pinnedOrderPanel);
        settingsContainer.Controls.Add(maxSessionsPanel);
        settingsContainer.Controls.Add(themePanel);
        settingsContainer.Controls.Add(autoHidePanel);
        settingsContainer.Controls.Add(alwaysOnTopPanel);
        settingsContainer.Controls.Add(edgeRenamePanel);
        settingsContainer.Controls.Add(notifyPanel);
        settingsContainer.Controls.Add(workDirPanel);
        settingsContainer.Controls.Add(wsDirPanel);
        settingsContainer.Controls.Add(settingsBottomPanel);

        dialog.Controls.Add(settingsContainer);
        dialog.ShowDialog(this);
    }

    private void SetupUpdateBanner()
    {
        var linkColor = Application.IsDarkModeEnabled ? Color.FromArgb(100, 180, 255) : Color.FromArgb(0, 102, 204);
        this._updateLabel = new LinkLabel
        {
            Dock = DockStyle.Bottom,
            TextAlign = ContentAlignment.MiddleCenter,
            Height = 28,
            Visible = false,
            Padding = new Padding(0, 4, 0, 4),
            LinkColor = linkColor,
            ActiveLinkColor = linkColor,
            VisitedLinkColor = linkColor
        };
        this._updateLabel.LinkClicked += this.OnUpdateLabelClickedAsync;
    }

    private void SetupTimersAndEvents(int initialTab)
    {

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

        this._activeStatusTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        this._activeStatusTimer.Tick += (s, e) => this.RefreshActiveStatusAsync();
        this._activeStatusTimer.Start();

        this._spinnerTimer = new System.Windows.Forms.Timer { Interval = 100 };
        this._spinnerTimer.Tick += (s, e) => this._sessionsVisuals.GridVisuals.AdvanceSpinnerFrame();
        this._spinnerTimer.Start();

        this.Shown += async (s, e) =>
        {
            // On first launch with toast mode, slide up instead of just hiding
            if (Program._settings.ToastMode && !this._toastVisible)
            {
                this.ShowToast();
            }

            await this.LoadInitialDataAsync().ConfigureAwait(true);
            _ = this.CheckForUpdateInBackgroundAsync();
        };

        // Periodic update check (1h)
        this._updateCheckTimer = new System.Windows.Forms.Timer { Interval = 3600000 };
        this._updateCheckTimer.Tick += (s, e) => _ = this.CheckForUpdateInBackgroundAsync();
        this._updateCheckTimer.Start();

        this.FormClosed += (s, e) =>
        {
            this._activeStatusTimer?.Stop();
            this._spinnerTimer?.Stop();
            this._updateCheckTimer?.Stop();
        };
    }

    private async Task CheckForUpdateInBackgroundAsync()
    {
        var update = await UpdateService.CheckForUpdateAsync().ConfigureAwait(false);
        if (update?.InstallerUrl != null)
        {
            this._latestUpdate = update;
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
            // Focus existing Copilot CLI window if already running
            if (!this._activeTracker.TryFocusCopilotCli(this.SelectedSessionId))
            {
                this._interactionManager.LaunchSession(this.SelectedSessionId);
            }
        }
    }

    /// <summary>
    /// Switches the main tab control to the specified tab and brings the form to the foreground.
    /// </summary>
    /// <param name="tabIndex">The zero-based index of the tab to activate.</param>
    public async void SwitchToTabAsync(int tabIndex)
    {
        if (tabIndex == 0)
        {
            this.ShowNewSessionDialogAsync();
        }

        if (tabIndex == 1)
        {
            this._cachedSessions = (List<NamedSession>)await Task.Run(() => this._refreshCoordinator.LoadSessions()).ConfigureAwait(true);
            var snapshot = await Task.Run(() => this._refreshCoordinator.RefreshActiveStatus(this._cachedSessions)).ConfigureAwait(true);
            this.PopulateGridWithFilter(snapshot);
        }

        if (tabIndex == 2)
        {
            this.BuildAndShowSettingsDialog();
        }

        if (Program._settings.ToastMode && (!this.Visible || this.WindowState == FormWindowState.Minimized))
        {
            this.ShowToastAtCursor();
            return;
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

    private void WriteSessionMetadata()
    {
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        foreach (var s in this._cachedSessions)
        {
            var displayName = !string.IsNullOrEmpty(s.Alias) ? s.Alias : s.Summary;
            EdgeWorkspaceService.WriteSessionMetadata(s.Id, displayName, version);
        }
    }

    private readonly Dictionary<string, string> _signalStatuses = new(StringComparer.OrdinalIgnoreCase);

    private void CheckEdgeTabChanges()
    {
        foreach (var ws in this._activeTracker.GetTrackedEdgeWorkspaces())
        {
            if (!ws.IsOpen)
            {
                this._signalStatuses.Remove(ws.WorkspaceId);
                continue;
            }

            // If currently processing a save, check if done
            if (this._signalStatuses.TryGetValue(ws.WorkspaceId, out var status) && status == "processing")
            {
                continue;
            }

            // Check for save signal from session.html button click
            var saveDetected = ws.DetectSaveSignal();
            Program.Logger.LogInformation("[SaveSignal] {Sid}: DetectSaveSignal={Detected}", ws.WorkspaceId, saveDetected);
            if (saveDetected)
            {
                this._signalStatuses[ws.WorkspaceId] = "processing";
                Program.Logger.LogInformation("[SaveSignal] {Sid}: Save signal detected, saving...", ws.WorkspaceId);

                var wsId = ws.WorkspaceId;
                var urls = ws.GetTabUrls();
                Program.Logger.LogInformation("[SaveSignal] {Sid}: Got {Count} URLs", wsId, urls.Count);
                if (urls.Count > 0)
                {
                    EdgeTabPersistenceService.SaveTabs(wsId, urls);
                    var titleHash = ws.GetTabNameHash();
                    Program.Logger.LogInformation("[SaveSignal] {Sid}: New title hash={Hash}", wsId, titleHash);
                    if (titleHash != null)
                    {
                        EdgeTabPersistenceService.SaveTabTitleHash(wsId, titleHash);
                    }

                    this.BeginInvoke(() => this._toast.Show($"✅ Edge state saved — {urls.Count} tab(s) stored"));
                }

                ws.HasUnsavedChanges = false;
                this._signalStatuses.Remove(wsId);
                continue;
            }

            // Lightweight change detection — just reads tab names, no navigation
            ws.CheckForTabChanges();
            if (ws.HasUnsavedChanges)
            {
                this._signalStatuses[ws.WorkspaceId] = "unsaved";
            }
            else
            {
                this._signalStatuses.Remove(ws.WorkspaceId);
            }
        }

        // Write signal file for session.html to poll
        if (this._signalStatuses.Count > 0)
        {
            Program.Logger.LogInformation("[Signals] Writing statuses: [{Statuses}]",
                string.Join(", ", this._signalStatuses.Select(kv => $"{kv.Key}={kv.Value}")));
        }

        EdgeWorkspaceService.WriteSessionSignals(this._signalStatuses);
    }

    private bool _refreshInProgress;

    /// <summary>
    /// Applies tab/pin states from the state file to loaded sessions.
    /// </summary>
    private void ApplySessionStates(List<NamedSession> sessions)
    {
        var states = SessionArchiveService.Load(Program.SessionStateFile);
        ApplySessionStates(sessions, states, Program._settings.DefaultTab);
    }

    /// <summary>
    /// Core logic for applying tab/pin states — extracted for testability.
    /// </summary>
    internal static void ApplySessionStates(List<NamedSession> sessions, Dictionary<string, SessionArchiveService.SessionState> states, string defaultTab)
    {
        foreach (var session in sessions)
        {
            if (states.TryGetValue(session.Id, out var state))
            {
                session.Tab = !string.IsNullOrEmpty(state.Tab) ? state.Tab : defaultTab;
                session.IsPinned = state.IsPinned;
            }
            else
            {
                session.Tab = defaultTab;
            }
        }
    }

    /// <summary>
    /// Returns sessions filtered by the currently selected tab,
    /// with pinned sessions sorted to the top.
    /// </summary>
    private List<NamedSession> GetFilteredSessions(ActiveStatusSnapshot? snapshot = null)
    {
        snapshot ??= this._lastSnapshot;
        var selectedTab = this._sessionsVisuals.SelectedTabName;
        var filtered = this._cachedSessions.Where(s => string.Equals(s.Tab, selectedTab, StringComparison.OrdinalIgnoreCase)).ToList();

        SortSessions(filtered, snapshot, Program._settings.PinnedOrder);

        return filtered;
    }

    /// <summary>
    /// Sorts sessions: pinned first, then running, then by date.
    /// Extracted for testability.
    /// </summary>
    internal static void SortSessions(List<NamedSession> sessions, ActiveStatusSnapshot? snapshot, string pinnedOrder)
    {
        sessions.Sort((a, b) =>
        {
            if (a.IsPinned != b.IsPinned)
            {
                return a.IsPinned ? -1 : 1;
            }

            if (a.IsPinned && b.IsPinned)
            {
                if (string.Equals(pinnedOrder, "alias", StringComparison.OrdinalIgnoreCase))
                {
                    var nameA = !string.IsNullOrEmpty(a.Alias) ? a.Alias : a.Summary;
                    var nameB = !string.IsNullOrEmpty(b.Alias) ? b.Alias : b.Summary;
                    return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
                }

                if (string.Equals(pinnedOrder, "created", StringComparison.OrdinalIgnoreCase))
                {
                    return b.LastModified.CompareTo(a.LastModified);
                }

                // Default ("running"): running pinned first, then by date
                if (snapshot != null)
                {
                    bool aRunning = snapshot.ActiveTextBySessionId.ContainsKey(a.Id);
                    bool bRunning = snapshot.ActiveTextBySessionId.ContainsKey(b.Id);
                    if (aRunning != bRunning)
                    {
                        return aRunning ? -1 : 1;
                    }
                }

                return b.LastModified.CompareTo(a.LastModified);
            }

            // Among non-pinned: running sessions first
            if (snapshot != null)
            {
                bool aRunning = snapshot.ActiveTextBySessionId.ContainsKey(a.Id);
                bool bRunning = snapshot.ActiveTextBySessionId.ContainsKey(b.Id);
                if (aRunning != bRunning)
                {
                    return aRunning ? -1 : 1;
                }
            }

            return b.LastModified.CompareTo(a.LastModified);
        });
    }

    /// <summary>
    /// Populates the grid with the current tab's filtered sessions and updates tab counts.
    /// </summary>
    private void PopulateGridWithFilter(ActiveStatusSnapshot snapshot)
    {
        this._lastSnapshot = snapshot;
        var filtered = this.GetFilteredSessions(snapshot);
        this._sessionsVisuals.GridVisuals.Populate(filtered, snapshot, this._sessionsVisuals.SearchBox.Text);
        this.UpdateTabCounts();
    }

    private void UpdateTabCounts()
    {
        var searchQuery = this._sessionsVisuals.SearchBox.Text;
        var isSearching = !string.IsNullOrWhiteSpace(searchQuery);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tabName in Program._settings.SessionTabs)
        {
            var tabSessions = this._cachedSessions.Where(s => string.Equals(s.Tab, tabName, StringComparison.OrdinalIgnoreCase)).ToList();
            counts[tabName] = isSearching
                ? SessionService.SearchSessions(tabSessions, searchQuery!).Count
                : tabSessions.Count;
        }

        this._sessionsVisuals.UpdateTabCounts(counts);
    }

    private async void RefreshActiveStatusAsync()
    {
        if (this._refreshInProgress)
        {
            return;
        }

        this._refreshInProgress = true;
        try
        {
            var sessions = (List<NamedSession>)await Task.Run(() => this._refreshCoordinator.LoadSessions()).ConfigureAwait(true);
            this._cachedSessions = sessions;
            this.ApplySessionStates(this._cachedSessions);
            this.WriteSessionMetadata();
            var snapshot = await Task.Run(() => this._refreshCoordinator.RefreshActiveStatus(this._cachedSessions)).ConfigureAwait(true);

            // Edge scan uses UI Automation (COM/STA) — run on a dedicated STA thread
            bool edgeChanged = await Task.Factory.StartNew(
                () => this._activeTracker.ScanAndTrackEdgeWorkspaces(),
                CancellationToken.None,
                TaskCreationOptions.None,
                StaTaskScheduler.Instance).ConfigureAwait(true);
            if (edgeChanged)
            {
                // Re-build snapshot to include newly discovered Edge workspaces
                snapshot = await Task.Run(() => this._refreshCoordinator.RefreshActiveStatus(this._cachedSessions)).ConfigureAwait(true);
            }

            // Check Edge tab changes on STA thread
            await Task.Factory.StartNew(
                () => this.CheckEdgeTabChanges(),
                CancellationToken.None,
                TaskCreationOptions.None,
                StaTaskScheduler.Instance).ConfigureAwait(true);

            this.PopulateGridWithFilter(snapshot);

            // Bell notification: detect transitions and fire toast
            this._bellService?.CheckAndNotify(snapshot);
        }
        finally
        {
            this._refreshInProgress = false;
        }
    }

    /// <summary>
    /// Called from the FileSystemWatcher thread when events.jsonl changes.
    /// Marshals to UI thread and updates just the affected session's row.
    /// </summary>
    private void OnEventsStatusChanged(string sessionId, EventsJournalService.SessionStatus status)
    {
        string statusIcon;
        switch (status)
        {
            case EventsJournalService.SessionStatus.Working:
                this._activeTracker.MarkSessionWorking(sessionId);
                statusIcon = "working";
                break;
            case EventsJournalService.SessionStatus.Idle:
                statusIcon = this._activeTracker.IsStartupSuppressed(sessionId) ? "" : "bell";
                break;
            case EventsJournalService.SessionStatus.IdleSilent:
                statusIcon = "";
                break;
            default:
                return;
        }

        if (this.IsHandleCreated)
        {
            this.BeginInvoke(() =>
            {
                this._sessionsVisuals.GridVisuals.UpdateSessionStatus(sessionId, statusIcon);

                if (statusIcon == "bell" && this._bellService != null)
                {
                    var session = this._cachedSessions?
                        .FirstOrDefault(s => string.Equals(s.Id, sessionId, StringComparison.OrdinalIgnoreCase));
                    var sessionName = !string.IsNullOrEmpty(session?.Alias) ? session.Alias : session?.Summary ?? "Copilot CLI";
                    this._bellService.NotifySingle(sessionId, sessionName);
                }
            });
        }
    }

    private async Task LoadInitialDataAsync()
    {
        var sessions = (List<NamedSession>)await Task.Run(() => this._refreshCoordinator.LoadSessions()).ConfigureAwait(true);
        this._cachedSessions = sessions;
        this.ApplySessionStates(this._cachedSessions);

        // Prime events.jsonl cache for all sessions (initial disk read)
        await Task.Run(() => this._activeTracker.EventsJournal.PrimeCache(
            sessions.Select(s => s.Id).ToList())).ConfigureAwait(true);

        var snapshot = await Task.Run(() => this._refreshCoordinator.RefreshActiveStatus(this._cachedSessions)).ConfigureAwait(true);

        // Seed startup sessions — suppress bell for working sessions only
        // Bell sessions should remain visible so user sees them after app restart
        var workingIds = snapshot.StatusIconBySessionId
            .Where(kvp => kvp.Value is "working")
            .Select(kvp => kvp.Key);
        this._activeTracker.InitStartedSessions(workingIds);
        this._bellService?.SeedStartupSessions(
            snapshot.StatusIconBySessionId
                .Where(kvp => kvp.Value == "bell")
                .Select(kvp => kvp.Key));

        // Re-run refresh with started sessions seeded (bells now suppressed)
        snapshot = await Task.Run(() => this._refreshCoordinator.RefreshActiveStatus(this._cachedSessions)).ConfigureAwait(true);

        // Now enable watcher events — startup seeding is complete
        this._activeTracker.EventsJournal.SuppressEvents = false;

        // Sort active sessions to the top on initial load, pinned first
        var activeIds = new HashSet<string>(snapshot.ActiveTextBySessionId.Keys, StringComparer.OrdinalIgnoreCase);
        this._cachedSessions.Sort((a, b) =>
        {
            if (a.IsPinned != b.IsPinned)
            {
                return a.IsPinned ? -1 : 1;
            }

            bool aActive = activeIds.Contains(a.Id);
            bool bActive = activeIds.Contains(b.Id);
            if (aActive != bActive)
            {
                return aActive ? -1 : 1;
            }

            return b.LastModified.CompareTo(a.LastModified);
        });

        this.PopulateGridWithFilter(snapshot);
        this._sessionsVisuals.LoadingOverlay.Visible = false;
    }

    private async Task RefreshGridAsync()
    {
        this._cachedSessions = (List<NamedSession>)await Task.Run(() => this._refreshCoordinator.LoadSessions()).ConfigureAwait(true);
        this.ApplySessionStates(this._cachedSessions);
        var snapshot = await Task.Run(() => this._refreshCoordinator.RefreshActiveStatus(this._cachedSessions)).ConfigureAwait(true);
        this.PopulateGridWithFilter(snapshot);
    }

    private async void ShowNewSessionDialogAsync()
    {
        var dialog = new Form
        {
            Text = "New Session — Select Directory",
            Size = new Size(650, 450),
            MinimumSize = new Size(450, 300),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            Font = this.Font,
            Icon = this.Icon,
            TopMost = this.TopMost
        };

        var dialogPanel = new Panel { Dock = DockStyle.Fill };

        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 6, 8, 6)
        };

        var btnCancel = new Button { Text = "Cancel", Width = 90 };
        btnCancel.Click += (s, e) => dialog.Close();

        var btnAddDir = new Button { Text = "Add Directory", Width = 110 };
        bottomPanel.Controls.Add(btnCancel);
        bottomPanel.Controls.Add(btnAddDir);

        dialog.Controls.Add(dialogPanel);
        dialog.Controls.Add(bottomPanel);

        var dialogVisuals = new NewSessionVisuals(dialogPanel);

        // Wire Add Directory button
        btnAddDir.Click += (s, e) => dialogVisuals.TriggerAddDirectoryAsync();

        // Wire events identically to the old tab-based visuals
        dialogVisuals.OnNewSession += async (selectedCwd) =>
        {
            var sessionName = NewSessionNameVisuals.ShowNamePrompt();
            if (sessionName == null)
            {
                return;
            }

            var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(selectedCwd, sessionName, CopilotSessionCreatorService.FindTemplateSessionDir()).ConfigureAwait(true);
            if (newSessionId != null)
            {
                if (!string.IsNullOrWhiteSpace(sessionName))
                {
                    SessionAliasService.SetAlias(Program.SessionAliasFile, newSessionId, sessionName);
                }

                this._interactionManager.LaunchSession(newSessionId);
                dialog.Close();
                await this.RefreshGridAsync().ConfigureAwait(true);
            }
            else
            {
                MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        dialogVisuals.OnNewSessionWorkspace += async (selectedCwd) =>
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
                        if (!string.IsNullOrWhiteSpace(wsResult.Value.SessionName))
                        {
                            SessionAliasService.SetAlias(Program.SessionAliasFile, newSessionId, wsResult.Value.SessionName);
                        }

                        this._interactionManager.LaunchSession(newSessionId);
                        dialog.Close();
                        await this.RefreshGridAsync().ConfigureAwait(true);
                    }
                    else
                    {
                        MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        };

        dialogVisuals.OnOpenExplorer += (selectedCwd) =>
        {
            SessionInteractionManager.OpenExplorer(selectedCwd);
        };

        dialogVisuals.OnOpenTerminal += (selectedCwd) =>
        {
            SessionInteractionManager.OpenTerminalSimple(selectedCwd);
        };

        dialogVisuals.OnAddDirectory += async () =>
        {
            using var fbd = new FolderBrowserDialog { SelectedPath = Program._settings.DefaultWorkDir };
            if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(fbd.SelectedPath))
            {
                PinnedDirectoryService.Add(fbd.SelectedPath);
                var pinnedDirs = PinnedDirectoryService.GetAll();
                var data = await Task.Run(() => this._sessionDataService.LoadAll(Program.SessionStateDir, Program.PidRegistryFile, pinnedDirs)).ConfigureAwait(true);
                dialogVisuals.Populate(data);
                dialogVisuals.LoadingOverlay.Visible = false;
            }
        };

        dialogVisuals.OnRemoveDirectory += async (selectedCwd) =>
        {
            PinnedDirectoryService.Remove(selectedCwd);
            var pinnedDirs = PinnedDirectoryService.GetAll();
            var data = await Task.Run(() => this._sessionDataService.LoadAll(Program.SessionStateDir, Program.PidRegistryFile, pinnedDirs)).ConfigureAwait(true);
            dialogVisuals.Populate(data);
            dialogVisuals.LoadingOverlay.Visible = false;
        };

        dialogVisuals.OnDoubleClicked += async (selectedCwd) =>
        {
            var sessionName = NewSessionNameVisuals.ShowNamePrompt();
            if (sessionName == null)
            {
                return;
            }

            var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(selectedCwd, sessionName, CopilotSessionCreatorService.FindTemplateSessionDir()).ConfigureAwait(true);
            if (newSessionId != null)
            {
                if (!string.IsNullOrWhiteSpace(sessionName))
                {
                    SessionAliasService.SetAlias(Program.SessionAliasFile, newSessionId, sessionName);
                }

                this._interactionManager.LaunchSession(newSessionId);
                dialog.Close();
                await this.RefreshGridAsync().ConfigureAwait(true);
            }
            else
            {
                MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        dialogVisuals.GetCwdMenuInfo = (path, cwdGitStatus) =>
        {
            bool isGit = cwdGitStatus.TryGetValue(path, out bool g) && g;
            int sessionCount = 0;
            if (dialogVisuals.CwdListView.SelectedItems.Count > 0
                && int.TryParse(dialogVisuals.CwdListView.SelectedItems[0].SubItems[1].Text, out int count))
            {
                sessionCount = count;
            }

            var pinnedDirs = PinnedDirectoryService.GetAll();
            bool isPinned = pinnedDirs.Exists(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase));

            return (isGit, isPinned, sessionCount);
        };

        // Load data
        var allPinnedDirs = PinnedDirectoryService.GetAll();
        var sessionData = await Task.Run(() => this._sessionDataService.LoadAll(Program.SessionStateDir, Program.PidRegistryFile, allPinnedDirs)).ConfigureAwait(true);
        dialogVisuals.Populate(sessionData);
        dialogVisuals.LoadingOverlay.Visible = false;

        dialog.ShowDialog(this);
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

    /// <summary>
    /// Lists user files in a session folder, excluding reserved Copilot CLI files and directories.
    /// Returns (relativePath, fullPath) tuples.
    /// </summary>
    internal static List<(string Name, string FullPath)> GetSessionFiles(string sessionStateDir, string sessionId)
    {
        var files = new List<(string Name, string FullPath)>();
        var sessionDir = Path.Combine(sessionStateDir, sessionId);
        if (!Directory.Exists(sessionDir))
        {
            return files;
        }

        // Reserved Copilot CLI files and folders to exclude
        var reservedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "events.jsonl", "workspace.yaml", "session.db"
        };
        var reservedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rewind-snapshots", "checkpoints"
        };

        foreach (var file in Directory.EnumerateFiles(sessionDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sessionDir, file);
            var fileName = Path.GetFileName(file);

            // Skip files in reserved directories
            var firstSegment = relativePath.Split(Path.DirectorySeparatorChar)[0];
            if (reservedDirs.Contains(firstSegment))
            {
                continue;
            }

            // Skip reserved root-level files
            if (!relativePath.Contains(Path.DirectorySeparatorChar) && reservedFiles.Contains(fileName))
            {
                continue;
            }

            files.Add((relativePath, file));
        }

        return files;
    }
}
