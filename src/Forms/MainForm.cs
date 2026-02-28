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
    private System.Windows.Forms.Timer? _backgroundPollTimer;
    private System.Windows.Forms.Timer? _visualRefreshTimer;
    private System.Windows.Forms.Timer? _spinnerTimer;
    private BellNotificationService? _bellService;

    // New Session support
    private readonly SessionDataService _sessionDataService = new();

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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var tabs = this._sessionsVisuals.SessionTabs;
        var grid = this._sessionsVisuals.SessionGrid;
        int realTabCount = Program._settings.SessionTabs.Count;

        switch (keyData)
        {
            case Keys.Tab | Keys.Shift:
                // Next tab (wrap around, skip "+" tab)
                if (realTabCount > 1)
                {
                    tabs.SelectedIndex = (tabs.SelectedIndex + 1) % realTabCount;
                }

                return true;

            case Keys.Tab | Keys.Shift | Keys.Control:
                // Previous tab (wrap around, skip "+" tab)
                if (realTabCount > 1)
                {
                    tabs.SelectedIndex = (tabs.SelectedIndex - 1 + realTabCount) % realTabCount;
                }

                return true;

            case Keys.Tab:
                // Cycle focus: Search → New Session → Settings → Grid
                var search = this._sessionsVisuals.SearchBox;
                var newBtn = this._sessionsVisuals.NewSessionButton;
                var setBtn = this._sessionsVisuals.SettingsButton;

                if (search.Focused)
                {
                    newBtn.Focus();
                }
                else if (newBtn.Focused)
                {
                    setBtn.Focus();
                }
                else if (setBtn.Focused)
                {
                    grid.Focus();
                    if (grid.CurrentRow == null && grid.Rows.Count > 0)
                    {
                        grid.CurrentCell = grid.Rows[0].Cells[1];
                    }
                }
                else
                {
                    search.Focus();
                    search.SelectAll();
                }

                return true;

            case Keys.Enter when grid.Focused && grid.CurrentRow != null:
                // Show context menu at the selected row
                var cellRect = grid.GetCellDisplayRectangle(1, grid.CurrentRow.Index, false);
                grid.ContextMenuStrip?.Show(grid, cellRect.Left, cellRect.Bottom);
                return true;

            case Keys.Enter | Keys.Shift when grid.Focused && grid.CurrentRow != null:
                // Launch session (same as double-click)
                var sid = grid.CurrentRow.Tag as string;
                if (sid != null)
                {
                    this.SelectedSessionId = sid;
                    this.LaunchSession();
                }

                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

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

        // Re-apply session states from disk before populating so that any tab
        // changes made while hidden are reflected immediately.
        this.ApplySessionStates(this._cachedSessions);
        this.PopulateGridWithFilter(this._lastSnapshot);
        this._visualRefreshTimer?.Start();
    }

    private void HideToast()
    {
        if (this._toastAnimating)
        {
            this._toastAnimTimer?.Stop();
            this._toastAnimating = false;
        }

        this._visualRefreshTimer?.Stop();

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
        this._sessionsVisuals.OnSearchChanged += () =>
        {
            this.PopulateGridWithFilter(this._lastSnapshot);
        };

        this._sessionsVisuals.OnSortChanged += () =>
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
        using var settingsForm = new SettingsForm(this._cachedSessions, this._latestUpdate);
        settingsForm.Font = this.Font;
        settingsForm.Icon = this.Icon;
        if (settingsForm.ShowDialog(this) == DialogResult.OK)
        {
            this.TopMost = Program._settings.AlwaysOnTop;
            this._sessionsVisuals.BuildSessionTabs();
            this._sessionsVisuals.BuildGridContextMenu();
            this.ApplySessionStates(this._cachedSessions);
            this.PopulateGridWithFilter(this._lastSnapshot);
            this._toast.Show("✅ Settings saved successfully");
        }
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

        this._backgroundPollTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        this._backgroundPollTimer.Tick += (s, e) => this.RefreshBackgroundAsync();

        this._visualRefreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        this._visualRefreshTimer.Tick += (s, e) => this.RefreshVisualsAsync();

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

            // Start background timers only after initial data is loaded to avoid
            // populating the grid before session states (Active/Archived/Done) are applied.
            this._backgroundPollTimer.Start();
            this._visualRefreshTimer.Start();

            this.CheckForMissingAllowedDirs();
            _ = this.CheckForUpdateInBackgroundAsync();
        };

        // Periodic update check (1h)
        this._updateCheckTimer = new System.Windows.Forms.Timer { Interval = 3600000 };
        this._updateCheckTimer.Tick += (s, e) => _ = this.CheckForUpdateInBackgroundAsync();
        this._updateCheckTimer.Start();

        this.FormClosed += (s, e) =>
        {
            this._backgroundPollTimer?.Stop();
            this._visualRefreshTimer?.Stop();
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

        SortSessions(filtered, snapshot, Program._settings.PinnedOrder,
            this._sessionsVisuals.SortColumn, this._sessionsVisuals.SortDirection);

        return filtered;
    }

    /// <summary>
    /// Sorts sessions: pinned first (using pinnedOrder), then by column sort.
    /// Extracted for testability.
    /// </summary>
    internal static void SortSessions(List<NamedSession> sessions, ActiveStatusSnapshot? snapshot,
        string pinnedOrder, string sortColumn = "RunningApps", SortOrder sortDirection = SortOrder.Descending)
    {
        sessions.Sort((a, b) =>
        {
            // Pinned always first
            if (a.IsPinned != b.IsPinned)
            {
                return a.IsPinned ? -1 : 1;
            }

            // Among pinned: use PinnedOrder setting
            if (a.IsPinned && b.IsPinned)
            {
                return ComparePinned(a, b, snapshot, pinnedOrder);
            }

            // Among non-pinned: use column sort
            int result = CompareByColumn(a, b, snapshot, sortColumn);
            if (result != 0)
            {
                return sortDirection == SortOrder.Ascending ? result : -result;
            }

            // Tie-break: use PinnedOrder setting
            return CompareTiebreak(a, b, snapshot, pinnedOrder);
        });
    }

    private static int ComparePinned(NamedSession a, NamedSession b, ActiveStatusSnapshot? snapshot, string pinnedOrder)
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

    private static int CompareByColumn(NamedSession a, NamedSession b, ActiveStatusSnapshot? snapshot, string column)
    {
        return column switch
        {
            "Session" => string.Compare(
                !string.IsNullOrEmpty(a.Alias) ? a.Alias : a.Summary,
                !string.IsNullOrEmpty(b.Alias) ? b.Alias : b.Summary,
                StringComparison.OrdinalIgnoreCase),
            "CWD" => string.Compare(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase),
            "Date" => a.LastModified.CompareTo(b.LastModified),
            "RunningApps" => CompareRunning(a, b, snapshot),
            _ => 0
        };
    }

    private static int CompareRunning(NamedSession a, NamedSession b, ActiveStatusSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            return 0;
        }

        bool aRunning = snapshot.ActiveTextBySessionId.ContainsKey(a.Id);
        bool bRunning = snapshot.ActiveTextBySessionId.ContainsKey(b.Id);
        return aRunning.CompareTo(bRunning);
    }

    private static int CompareTiebreak(NamedSession a, NamedSession b, ActiveStatusSnapshot? snapshot, string pinnedOrder)
    {
        if (string.Equals(pinnedOrder, "alias", StringComparison.OrdinalIgnoreCase))
        {
            var nameA = !string.IsNullOrEmpty(a.Alias) ? a.Alias : a.Summary;
            var nameB = !string.IsNullOrEmpty(b.Alias) ? b.Alias : b.Summary;
            return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
        }

        // Default tiebreak: by date descending (newest first)
        return b.LastModified.CompareTo(a.LastModified);
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

    /// <summary>
    /// Core background refresh: refreshes session data, Edge tracking, and notifications.
    /// Returns the latest snapshot. Does not touch the grid.
    /// </summary>
    private async Task RefreshBackgroundCoreAsync()
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

            this._lastSnapshot = snapshot;

            // Bell notification: detect transitions and fire toast
            this._bellService?.CheckAndNotify(snapshot);
        }
        finally
        {
            this._refreshInProgress = false;
        }
    }

    /// <summary>
    /// Background polling callback: refreshes data, Edge tracking, and notifications.
    /// Runs even when the toast is hidden.
    /// </summary>
    private async void RefreshBackgroundAsync() => await this.RefreshBackgroundCoreAsync().ConfigureAwait(true);

    /// <summary>
    /// Visual polling: repopulates the grid from the latest cached snapshot.
    /// Stopped when the toast is hidden, restarted when shown.
    /// </summary>
    private void RefreshVisualsAsync()
    {
        this.PopulateGridWithFilter(this._lastSnapshot);
    }

    /// <summary>
    /// Full refresh: background data + visual grid. Used by user-triggered actions (context menu, etc.).
    /// </summary>
    private async void RefreshActiveStatusAsync()
    {
        await this.RefreshBackgroundCoreAsync().ConfigureAwait(true);
        this.PopulateGridWithFilter(this._lastSnapshot);
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

    private void CheckForMissingAllowedDirs()
    {
        var missing = Program._settings.AllowedDirs.Where(d => !Directory.Exists(d)).ToList();
        if (missing.Count > 0)
        {
            var names = string.Join(", ", missing.Select(d => Path.GetFileName(d.TrimEnd('\\')) ?? d));
            this._toast.ShowWarning($"⚠️ {missing.Count} allowed dir(s) not found: {names} — check Settings");
        }
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
            FormBorderStyle = FormBorderStyle.Sizable,
            Font = this.Font,
            Icon = this.Icon,
            TopMost = this.TopMost
        };
        SettingsVisuals.AlignWithParent(dialog);

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
            var promptResult = NewSessionNameVisuals.ShowNamePrompt(selectedCwd);
            if (promptResult == null)
            {
                return;
            }

            // Handle branch/PR/issue checkout before creating the session
            if (promptResult.Action != BranchAction.None)
            {
                var gitRoot = SessionService.FindGitRoot(selectedCwd);
                if (gitRoot != null)
                {
                    (bool success, string error) checkoutResult = promptResult.Action switch
                    {
                        BranchAction.ExistingBranch when !string.IsNullOrEmpty(promptResult.BranchName) =>
                            GitService.CheckoutBranch(gitRoot, promptResult.BranchName),
                        BranchAction.NewBranch when !string.IsNullOrEmpty(promptResult.BranchName) && !string.IsNullOrEmpty(promptResult.BaseBranch) =>
                            GitService.CheckoutNewBranch(gitRoot, promptResult.BranchName, promptResult.BaseBranch),
                        BranchAction.FromPr when promptResult.PrNumber.HasValue && !string.IsNullOrEmpty(promptResult.Remote) && promptResult.Platform.HasValue =>
                            GitService.FetchAndCheckoutPr(gitRoot, promptResult.Remote, promptResult.Platform.Value, promptResult.PrNumber.Value, promptResult.HeadBranch ?? $"pr-{promptResult.PrNumber.Value}"),
                        BranchAction.FromIssue when !string.IsNullOrEmpty(promptResult.BranchName) && !string.IsNullOrEmpty(promptResult.BaseBranch) =>
                            GitService.CheckoutNewBranch(gitRoot, promptResult.BranchName, promptResult.BaseBranch),
                        _ => (true, "")
                    };

                    if (!checkoutResult.success)
                    {
                        MessageBox.Show($"Failed to switch branch:\n{checkoutResult.error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
            }

            var sessionName = promptResult.SessionName;
            var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(selectedCwd, sessionName, CopilotSessionCreatorService.FindTemplateSessionDir()).ConfigureAwait(true);
            if (newSessionId != null)
            {
                if (!string.IsNullOrWhiteSpace(sessionName))
                {
                    SessionAliasService.SetAlias(Program.SessionAliasFile, newSessionId, sessionName);
                }

                // Auto-add Edge tab for PR/Issue URL
                if (!string.IsNullOrEmpty(promptResult.GitHubUrl))
                {
                    var existingTabs = EdgeTabPersistenceService.LoadTabs(newSessionId);
                    if (!existingTabs.Contains(promptResult.GitHubUrl))
                    {
                        existingTabs.Add(promptResult.GitHubUrl);
                        EdgeTabPersistenceService.SaveTabs(newSessionId, existingTabs);
                    }
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

                        // Auto-add Edge tab for PR/Issue URL
                        if (!string.IsNullOrEmpty(wsResult.Value.GitHubUrl))
                        {
                            var existingTabs = EdgeTabPersistenceService.LoadTabs(newSessionId);
                            if (!existingTabs.Contains(wsResult.Value.GitHubUrl))
                            {
                                existingTabs.Add(wsResult.Value.GitHubUrl);
                                EdgeTabPersistenceService.SaveTabs(newSessionId, existingTabs);
                            }
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
