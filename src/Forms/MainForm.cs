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
    private List<NamedSession> _cachedSessions = [];
    private ActiveStatusSnapshot _lastSnapshot = new([], [], []);
    private readonly ActiveStatusTracker _activeTracker = new();
    private readonly EventsJournalService _eventsJournal;
    private readonly SessionRefreshCoordinator _refreshCoordinator;
    private readonly SessionInteractionManager _interactionManager;
    private System.Windows.Forms.Timer? _spinnerTimer;
    private System.Windows.Forms.Timer? _refreshDebounceTimer;
    private System.Windows.Forms.Timer? _fullRefreshTimer;
    private WindowEventHookService? _windowHookService;
    private ProcessExitTracker? _processExitTracker;
    private readonly HashSet<string> _dirtyTrackingSessionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyDataSessionIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirtyFullRefresh;
    private BellNotificationService? _bellService;
    private readonly WorkspaceYamlWatcherService? _workspaceWatcher;
    private readonly SessionContextWatcherService? _contextWatcher;
    private readonly CopilotLogWatcherService? _logWatcher;
    private readonly Dictionary<string, long> _lastSavedBySession = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _saveInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly GitHubApiService _githubApi;
    private readonly GitHubPollingService _githubPoller;
    private readonly IConfirmDialog _confirmDialog;
    private readonly IMessageBox _messageBox;

    // Window pin mode state
    private bool _pinMode;
    private IntPtr _pinHwnd;
    private string _pinTitle = "";

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
    private bool _toastAnimating;
    private long _toastShownTicks;

    /// <summary>
    /// Determines whether the toast is actually visible on screen by checking window state
    /// rather than tracking a flag, since external actions (Win+D, taskbar click) can change
    /// visibility outside our control. A window whose area is less than 10% of its restore
    /// bounds is considered minimized/invisible (e.g., Win+D shrinks to 160x28).
    /// </summary>
    private bool IsToastVisible
    {
        get
        {
            if (!this.Visible || this.WindowState == FormWindowState.Minimized)
            {
                return false;
            }

            var restore = this.RestoreBounds.Size;
            if (restore.Width <= 0 || restore.Height <= 0)
            {
                return this.Visible;
            }

            var currentArea = (long)this.Size.Width * this.Size.Height;
            var restoreArea = (long)restore.Width * restore.Height;
            return currentArea > restoreArea / 10;
        }
    }

    /// <summary>
    /// Gets the identifier of the currently selected session.
    /// </summary>
    public string? SelectedSessionId { get; private set; }

    internal AiDetectionService AiDetectionService { get; }

    internal ICopilotProbe CopilotProbe { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainForm"/> class.
    /// </summary>
    /// <param name="initialTab">The zero-based index of the tab to display on startup.</param>
    public MainForm(int initialTab = 0)
    {
        this.InitializeFormProperties();

        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        EdgeWorkspaceService.StampSessionHtmlVersion(version);

        this._interactionManager = new SessionInteractionManager(Program.SessionStateDir, Program.TerminalCacheFile);
        this._refreshCoordinator = new SessionRefreshCoordinator(Program.SessionStateDir, Program.PidRegistryFile, this._activeTracker);
        this._eventsJournal = this._activeTracker.EventsJournal;
        this._githubApi = new GitHubApiService(() => Program._settings.GitHubToken);
        this._githubPoller = new GitHubPollingService(this._githubApi,
            () => this._cachedSessions.Select(s => s.Id).ToList());
        this._confirmDialog = new MessageBoxConfirmDialog(this);
        this._messageBox = new MessageBoxAdapter(this);
        this.CopilotProbe = new CopilotProbe();
        this.AiDetectionService = new AiDetectionService(
            this._githubApi,
            new ProcessRunner(),
            this.GetSessionCwdForAiDetection,
            msg =>
            {
                if (this.IsHandleCreated && this.InvokeRequired)
                {
                    this.BeginInvoke(() => this._toast.Show(msg));
                }
                else
                {
                    this._toast.Show(msg);
                }
            },
            this._githubPoller,
            settingsGetter: () => Program._settings.AiDetection,
            copilotProbe: this.CopilotProbe);
        this._activeTracker.EventsJournal.LoadCache();
        this._activeTracker.EventsJournal.StatusChanged += this.OnEventsStatusChanged;
        this._activeTracker.EventsJournal.LatestCwdChanged += this.OnLatestCwdChanged;
        this._activeTracker.EventsJournal.StartWatching();

        this._workspaceWatcher = new WorkspaceYamlWatcherService();
        this._workspaceWatcher.WorkspaceChanged += sid =>
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(() =>
                {
                    this.WriteSessionMetadata(sid);
                    this.RequestRefresh(sessionId: sid, dataChanged: true);
                });
            }
        };
        this._workspaceWatcher.WorkspaceDeleted += sid =>
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(() => this.RequestRefresh(sessionId: sid, dataChanged: true));
            }
        };
        this._workspaceWatcher.StartWatching();

        this._contextWatcher = new SessionContextWatcherService();
        this._contextWatcher.PrimeCache();
        this._contextWatcher.CountsChanged += sid =>
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(() => this.RequestRefresh(sessionId: sid, trackingChanged: true));
            }
        };
        this._contextWatcher.StartWatching();

        this._logWatcher = new CopilotLogWatcherService();
        this._logWatcher.ExternalSessionDiscovered += (sid, copilotPid) =>
        {
            if (this.IsHandleCreated)
            {
                Program.Logger.LogInformation("External session discovered via log watcher: {SessionId}", sid);
                this.BeginInvoke(() =>
                {
                    this._activeTracker.HandleExternalSessionDiscovered(sid, copilotPid);
                    this.RequestRefresh(sessionId: sid, dataChanged: true);
                });
            }
        };
        this._logWatcher.StartWatching();

        // T2: Subscribe to internal Copilot PID registration
        PidRegistryService.CopilotPidRegisteredStatic += (sid, copilotPid) =>
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(() =>
                {
                    this._activeTracker.HandleInternalCopilotPidRegistered(sid, copilotPid);
                    this.RequestRefresh(sessionId: sid, trackingChanged: true);
                });
            }
        };

        this._sessionsPanel = new Panel { Dock = DockStyle.Fill };

        this._sessionsVisuals = new ExistingSessionsVisuals(this._sessionsPanel, this._activeTracker)
        {
            AiDetectionService = this.AiDetectionService
        };
        this._sessionsVisuals.GridVisuals.AiDetectionService = this.AiDetectionService;
        this._sessionsVisuals.GridVisuals.ConfirmDialog = this._confirmDialog;
        this._sessionsVisuals.GridVisuals.MessageBox = this._messageBox;
        this._toast = ToastPanel.AttachTo(this._sessionsPanel);
        this.WireGitHubPollingEvents();
        this.WireAiDetectionEvents();
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

    private static async Task<(bool updateOk, string? updateErr)> UpdateAndDropEffectiveRefAsync(string gitRoot, string sourceRef)
    {
        var (updateOk, updateErr, _) = await WorkspaceCreationService.UpdateSourceBranchAsync(
            gitRoot, sourceRef, CancellationToken.None).ConfigureAwait(true);
        return (updateOk, updateErr);
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
            Program.Logger.LogWarning("Failed to register spotlight hotkey (Win+Alt+X)");
            this._hotkeyService = null;
            return;
        }

        this._hotkeyService.HotkeyPressed += this.OnToastHotkeyPressed;
        this._hotkeyService.WindowPinHotkeyPressed += this.OnWindowPinHotkeyPressed;

        this._toastAnimTimer = new System.Windows.Forms.Timer { Interval = 15 };
        this._toastAnimTimer.Tick += this.OnToastAnimationTick;

        this.Deactivate += this.OnToastDeactivate;
        this.ShowToast();
    }

    private void TeardownToastMode()
    {
        if (this._hotkeyService != null)
        {
            this._hotkeyService.HotkeyPressed -= this.OnToastHotkeyPressed;
            this._hotkeyService.Dispose();
            this._hotkeyService = null;
        }

        if (this._toastAnimTimer != null)
        {
            this._toastAnimTimer.Stop();
            this._toastAnimTimer.Tick -= this.OnToastAnimationTick;
            this._toastAnimTimer.Dispose();
            this._toastAnimTimer = null;
        }

        this.Deactivate -= this.OnToastDeactivate;

        // Restore normal window state if it was hidden
        if (this.WindowState == FormWindowState.Minimized)
        {
            this.WindowState = FormWindowState.Normal;
            this.Show();
        }
    }

    /// <summary>
    /// Applies spotlight settings at runtime without requiring a restart.
    /// </summary>
    internal void ApplySpotlightSettings()
    {
        if (Program._settings.ToastMode && this._hotkeyService == null)
        {
            this.SetupToastMode();
        }
        else if (!Program._settings.ToastMode && this._hotkeyService != null)
        {
            this.TeardownToastMode();
        }
    }

    private void OnToastHotkeyPressed()
    {
        if (this.InvokeRequired)
        {
            this.BeginInvoke(this.OnToastHotkeyPressed);
            return;
        }

        if (this.IsToastVisible)
        {
            // When not always-on-top, the window may be behind others — bring it forward instead of hiding
            if (!Program._settings.AlwaysOnTop && GetForegroundWindow() != this.Handle)
            {
                this.Activate();
            }
            else
            {
                this.HideToast();
            }
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
                grid.ContextMenuStrip?.ShowOnCurrentScreen(grid, new Point(cellRect.Left, cellRect.Bottom));
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
            _ => workArea.Left + ((workArea.Width - windowSize.Width) / 2)
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

        // When minimized (e.g., after Win+D), the form Size is the taskbar thumbnail
        // (160x28). Use RestoreBounds to get the real size for position calculation.
        bool wasMinimized = this.WindowState == FormWindowState.Minimized;
        var formSize = wasMinimized ? this.RestoreBounds.Size : this.Size;
        var (target, animStart, _) = CalculateToastPosition(workArea, formSize, pos);

        // Only hide if the window was visually present (not already shrunk by Win+D)
        if (wasMinimized && this.RestoreBounds.Size.Width <= this.Size.Width)
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

        this._toastShownTicks = Environment.TickCount64;

        // Re-applysession states from disk before populating so that any tab
        // changes made while hidden are reflected immediately.
        this.ApplySessionStates(this._cachedSessions);
        this.PopulateGridWithFilter(this._lastSnapshot);
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
            var screen = GetToastScreen();
            if (this.Top >= screen.WorkingArea.Bottom || this.Bottom <= screen.WorkingArea.Top)
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    private const uint GA_ROOT = 2;

    private void OnWindowPinHotkeyPressed()
    {
        if (this.InvokeRequired)
        {
            this.BeginInvoke(this.OnWindowPinHotkeyPressed);
            return;
        }

        if (!GetCursorPos(out var cursorPos))
        {
            return;
        }

        var hwnd = WindowFromPoint(cursorPos);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var rootHwnd = GetAncestor(hwnd, GA_ROOT);
        if (rootHwnd != IntPtr.Zero)
        {
            hwnd = rootHwnd;
        }

        if (hwnd == this.Handle)
        {
            return;
        }

        var title = WindowFocusService.GetWindowTitle(hwnd);
        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        var existingSession = this._activeTracker.ResolveSessionForHwnd(hwnd);
        var menu = new ContextMenuStrip();

        if (existingSession != null)
        {
            var session = this._cachedSessions.FirstOrDefault(s => s.Id == existingSession);
            var sessionName = session != null
                ? (!string.IsNullOrEmpty(session.Alias) ? session.Alias : session.Summary)
                : existingSession[..Math.Min(8, existingSession.Length)];

            menu.Items.Add(new ToolStripMenuItem($"📌 Pinned to: {sessionName}") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());

            var detachItem = new ToolStripMenuItem("Detach from Session");
            detachItem.Click += (s, e) =>
            {
                this._activeTracker.DetachWindow(existingSession, hwnd);
                this.RequestRefresh(sessionId: existingSession, trackingChanged: true);
                this._toast.Show("✅ Window detached from session");
            };
            menu.Items.Add(detachItem);
        }
        else
        {
            var displayTitle = title.Length > 60 ? title[..57] + "..." : title;
            menu.Items.Add(new ToolStripMenuItem($"🪟 {displayTitle}") { Enabled = false, ForeColor = Color.Gray });
            menu.Items.Add(new ToolStripSeparator());

            var capturedHwnd = hwnd;
            var capturedTitle = title;
            var pinItem = new ToolStripMenuItem("Pin to Session...");
            pinItem.Click += (s, e) =>
            {
                this.EnterPinMode(capturedHwnd, capturedTitle);
            };
            menu.Items.Add(pinItem);
        }

        menu.Items.Add(new ToolStripSeparator());
        var cancelItem = new ToolStripMenuItem("Cancel");
        cancelItem.Click += (s, e) => menu.Close();
        menu.Items.Add(cancelItem);

        menu.Show(cursorPos);
    }

    private void EnterPinMode(IntPtr hwnd, string title)
    {
        this._pinMode = true;
        this._pinHwnd = hwnd;
        this._pinTitle = title;

        this.ShowToast();
        this.Activate();

        this._sessionsVisuals.GridVisuals.PinMode = true;
        this._sessionsVisuals.SessionGrid.Cursor = Cursors.Cross;
        this._toast.Show($"🎯 Click a session to pin: {title}");
    }

    private void ExitPinMode()
    {
        this._pinMode = false;
        this._pinHwnd = IntPtr.Zero;
        this._pinTitle = "";
        this._sessionsVisuals.GridVisuals.PinMode = false;
        this._sessionsVisuals.SessionGrid.Cursor = Cursors.Default;
    }

    private void OnToastDeactivate(object? sender, EventArgs e)
    {
        if (!Program._settings.ToastMode || !Program._settings.SpotlightAutoHide || !this.IsToastVisible || this._toastAnimating)
        {
            return;
        }

        // Ignore deactivation when being minimized by taskbar click or Win+D
        if (this.WindowState == FormWindowState.Minimized)
        {
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
            _ = GetWindowThreadProcessId(foreground, out uint pid);
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

        // Stop the window hook BEFORE base.OnFormClosing destroys the form handle.
        // This prevents WinEvent callbacks from firing during handle destruction,
        // which would call RequestRefresh → timer.Start() → Win32Exception.
        this._windowHookService?.Stop();
        this._refreshDebounceTimer?.Stop();
        this._fullRefreshTimer?.Stop();
        this._spinnerTimer?.Stop();
        this._updateCheckTimer?.Stop();

        this._activeTracker.EventsJournal.SaveCache();
        this._activeTracker.EventsJournal.Dispose();
        this._workspaceWatcher?.Dispose();
        this._contextWatcher?.Dispose();
        this._logWatcher?.Dispose();
        this._sessionsVisuals.GridVisuals.Dispose();
        this.AiDetectionService.Dispose();
        this._githubPoller.Dispose();
        this._activeTracker.SaveWindowHandleCache();

        base.OnFormClosing(e);
    }

    private void WireGitHubPollingEvents()
    {
        this._githubPoller.ItemUpdated += sid =>
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(() => this.RequestRefresh(sessionId: sid, trackingChanged: true));
            }
        };

        this._githubPoller.NewActivityDetected += (sid, type, number, title) =>
        {
            var prefix = type == "pr" ? "PR" : "Issue";
            var session = this._cachedSessions.FirstOrDefault(s => s.Id == sid);
            var sessionName = session != null
                ? (!string.IsNullOrEmpty(session.Alias) ? session.Alias : session.Summary)
                : sid[..Math.Min(8, sid.Length)];

            var message = $"🔔 {prefix} #{number} has new activity\n{title}";

            if (this.IsHandleCreated)
            {
                this.BeginInvoke(() => this._toast.Show(message));
            }

            this._trayIcon?.ShowBalloonTip(5000, $"GitHub: {prefix} #{number}", title, ToolTipIcon.Info);
        };
    }

    private void WireAiDetectionEvents()
    {
        this.AiDetectionService.DetectionStateChanged += (sid, oldState, newState) =>
        {
            if (oldState == DetectionStatus.Running
                && newState != DetectionStatus.Running
                && this.IsHandleCreated)
            {
                this.BeginInvoke(() => this.RequestRefresh(sessionId: sid, trackingChanged: true));
            }
        };
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

        // Pin mode: intercept grid clicks to pin a window to the clicked session
        this._sessionsVisuals.SessionGrid.CellMouseClick += (s, e) =>
        {
            if (!this._pinMode || e.RowIndex < 0 || e.Button != MouseButtons.Left)
            {
                return;
            }

            var sessionId = this._sessionsVisuals.SessionGrid.Rows[e.RowIndex].Tag as string;
            if (sessionId == null)
            {
                return;
            }

            var session = this._cachedSessions.FirstOrDefault(x => x.Id == sessionId);
            var sessionName = session != null
                ? (!string.IsNullOrEmpty(session.Alias) ? session.Alias : session.Summary)
                : sessionId[..Math.Min(8, sessionId.Length)];

            var label = this._pinTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Window";
            var result = MessageBox.Show(
                $"Pin \"{label}\" to session \"{sessionName}\"?",
                "Pin Window to Session",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                var proc = new ActiveProcess(label, 0, null) { Hwnd = this._pinHwnd, HwndEverCaptured = true };
                this._activeTracker.TrackProcess(sessionId, proc);
                this.RequestRefresh(sessionId: sessionId, trackingChanged: true);
                this._toast.Show($"📌 {label} pinned to session");
                this.ExitPinMode();
            }
        };

        // Esc cancels pin mode
        this._sessionsVisuals.SessionGrid.KeyDown += (s, e) =>
        {
            if (this._pinMode && e.KeyCode == Keys.Escape)
            {
                this.ExitPinMode();
                this._toast.Show("Pin cancelled");
            }
        };
    }

    private void BuildAndShowSettingsDialog()
    {
        using var settingsForm = new SettingsForm(this._cachedSessions, this._latestUpdate, this.CopilotProbe);
        settingsForm.Font = this.Font;
        settingsForm.Icon = this.Icon;
        if (settingsForm.ShowDialog(this) == DialogResult.OK)
        {
            this.TopMost = Program._settings.AlwaysOnTop;
            this._sessionsVisuals.BuildSessionTabs();
            this._sessionsVisuals.BuildGridContextMenu();
            this.ApplySessionStates(this._cachedSessions);
            this.PopulateGridWithFilter(this._lastSnapshot);
            this.ApplySpotlightSettings();
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

        this._refreshDebounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
        this._refreshDebounceTimer.Tick += this.OnDebouncedRefreshAsync;

        this._fullRefreshTimer = new System.Windows.Forms.Timer { Interval = 45000 };
        this._fullRefreshTimer.Tick += (s, e) => this.RequestRefresh(fullRefresh: true);

        this._spinnerTimer = new System.Windows.Forms.Timer { Interval = 100 };
        this._spinnerTimer.Tick += (s, e) => this._sessionsVisuals.GridVisuals.AdvanceSpinnerFrame();
        this._spinnerTimer.Start();

        this._windowHookService = new WindowEventHookService();
        this._windowHookService.WindowCreated += hwnd =>
        {
            var sessionId = this._activeTracker.OnWindowCreated(hwnd);
            if (sessionId != null)
            {
                this.RequestRefresh(sessionId: sessionId, trackingChanged: true);
            }

            // Also check the window title for Terminal/Copilot CLI patterns.
            // Windows may be created with their final title already set, so
            // EVENT_OBJECT_NAMECHANGE never fires for them.
            var title = WindowFocusService.GetWindowTitle(hwnd);
            if (!string.IsNullOrEmpty(title))
            {
                var affected = this._activeTracker.OnWindowTitleChanged(hwnd, title, this.BuildSessionSummaryMap());
                foreach (var id in affected)
                {
                    this.RequestRefresh(sessionId: id, trackingChanged: true);
                }
            }
        };
        this._windowHookService.WindowDestroyed += hwnd =>
        {
            this._activeTracker.HandleWindowDestroyed(hwnd);
            var affected = this._activeTracker.OnWindowDestroyed(hwnd);
            foreach (var id in affected)
            {
                this.RequestRefresh(sessionId: id, trackingChanged: true);
            }
        };
        this._windowHookService.WindowTitleChanged += (hwnd, title) =>
        {
            // Detect Edge save signal: title contains "::Save"
            if (title.Contains("::Save", StringComparison.OrdinalIgnoreCase))
            {
                this.HandleEdgeSaveSignalAsync(hwnd, title);
            }

            var paneAffected = this._activeTracker.HandleWindowNameChanged(hwnd);
            foreach (var id in paneAffected)
            {
                this.RequestRefresh(sessionId: id, trackingChanged: true);
            }

            var affected = this._activeTracker.OnWindowTitleChanged(hwnd, title, this.BuildSessionSummaryMap());
            foreach (var id in affected)
            {
                this.RequestRefresh(sessionId: id, trackingChanged: true);
            }
        };
        this._windowHookService.ForegroundChanged += hwnd =>
        {
            // When a window gains focus, try to capture its HWND for tracked processes (IDEs).
            var sessionId = this._activeTracker.OnWindowCreated(hwnd);
            if (sessionId != null)
            {
                this.RequestRefresh(sessionId: sessionId, trackingChanged: true);
            }

            // Check title for Terminal/Copilot CLI patterns.
            var title = WindowFocusService.GetWindowTitle(hwnd);
            if (!string.IsNullOrEmpty(title))
            {
                var affected = this._activeTracker.OnWindowTitleChanged(hwnd, title, this.BuildSessionSummaryMap());
                foreach (var id in affected)
                {
                    this.RequestRefresh(sessionId: id, trackingChanged: true);
                }
            }

            // Track active session: select the session row when its window gains focus
            if (Program._settings.TrackActiveSession && this.IsHandleCreated)
            {
                var focusedSession = sessionId ?? this._activeTracker.ResolveSessionForHwnd(hwnd);
                if (focusedSession != null)
                {
                    this.BeginInvoke(() =>
                    {
                        this._sessionsVisuals.SelectSessionById(focusedSession, this._cachedSessions);
                    });
                }
            }
        };

        this._processExitTracker = new ProcessExitTracker();
        this._processExitTracker.ProcessExited += pid =>
        {
            this.BeginInvoke(() =>
            {
                var affected = this._activeTracker.OnProcessExited(pid);
                foreach (var id in affected)
                {
                    this.RequestRefresh(sessionId: id, trackingChanged: true);
                }
            });
        };

        // Tab switches inside a single wt window don't change the foreground
        // hwnd, so the foreground hook above can't drive the booster grid's
        // active-session highlight when the user manually flips tabs in their
        // terminal. ActiveStatusTracker fires ActiveSessionHintChanged whenever
        // OnWindowTitleChanged title-matches a Copilot CLI tab (the strongest
        // possible signal of which session is now in front), so subscribe and
        // mirror the SelectSessionById call from the foreground handler.
        this._activeTracker.ActiveSessionHintChanged += sessionId =>
        {
            if (Program._settings.TrackActiveSession && this.IsHandleCreated)
            {
                this.BeginInvoke(() =>
                {
                    this._sessionsVisuals.SelectSessionById(sessionId, this._cachedSessions);
                });
            }
        };

        this.Shown += async (s, e) =>
        {
            // On first launch with toast mode, slide up instead of just hiding
            if (Program._settings.ToastMode && !this.IsToastVisible)
            {
                this.ShowToast();
            }

            await this.LoadInitialDataAsync().ConfigureAwait(true);

            // Check for welcome popup (star request) — async, non-blocking
            _ = this.CheckWelcomePopupAsync();

            // Start event-driven refresh after initial data is loaded
            this._windowHookService?.Start();
            this._fullRefreshTimer?.Start();

            // Start GitHub polling
            this._githubPoller.Start();

            this.CheckForMissingAllowedDirs();
            this.CheckForMissingSessionCwds();
            _ = this.CheckForUpdateInBackgroundAsync();
        };

        // Periodic update check (1h)
        this._updateCheckTimer = new System.Windows.Forms.Timer { Interval = 3600000 };
        this._updateCheckTimer.Tick += (s, e) => _ = this.CheckForUpdateInBackgroundAsync();
        this._updateCheckTimer.Start();

        this.FormClosed += (s, e) =>
        {
            this._windowHookService?.Dispose();
            this._processExitTracker?.Dispose();

        };
    }

    private string? GetSessionCwdForAiDetection(string sessionId)
    {
        var session = this._cachedSessions.FirstOrDefault(s => s.Id == sessionId);
        if (!string.IsNullOrWhiteSpace(session?.Cwd))
        {
            return session.Cwd;
        }

        return this._interactionManager.GetSessionCwd(sessionId);
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
                var cwd = this._interactionManager.GetSessionCwd(this.SelectedSessionId);
                if (this.ValidateCwdOrPrompt(this.SelectedSessionId, cwd) == null)
                {
                    return;
                }

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
        foreach (var s in this._cachedSessions)
        {
            var metadataPath = Path.Combine(SessionStateService.GetSessionDir(s.Id), "metadata.js");
            if (File.Exists(metadataPath))
            {
                continue;
            }

            var displayName = !string.IsNullOrEmpty(s.Alias) ? s.Alias : s.Summary;
            EdgeWorkspaceService.WriteSessionMetadata(s.Id, displayName);
        }
    }

    /// <summary>
    /// Writes metadata.js for a single session (e.g., when its workspace.yaml changes).
    /// </summary>
    private void WriteSessionMetadata(string sessionId)
    {
        var session = this._cachedSessions?.FirstOrDefault(s =>
            string.Equals(s.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        if (session == null)
        {
            return;
        }

        var displayName = !string.IsNullOrEmpty(session.Alias) ? session.Alias : session.Summary;
        EdgeWorkspaceService.WriteSessionMetadata(session.Id, displayName);
    }

    private bool _refreshInProgress;

    /// <summary>
    /// Applies tab/pin states from the state file to loaded sessions.
    /// </summary>
    private void ApplySessionStates(List<NamedSession> sessions)
    {
        var states = SessionArchiveService.Load(Program.SessionStateFile);
        var tabCountBefore = Program._settings.SessionTabs.Count;
        ApplySessionStates(sessions, states, Program._settings.DefaultTab, Program._settings);

        // If missing tabs were auto-recovered, rebuild the tab UI
        if (Program._settings.SessionTabs.Count > tabCountBefore)
        {
            this._sessionsVisuals.BuildSessionTabs();
        }
    }

    /// <summary>
    /// Core logic for applying tab/pin states — extracted for testability.
    /// </summary>
    internal static void ApplySessionStates(List<NamedSession> sessions, Dictionary<string, SessionArchiveService.SessionState> states, string defaultTab, LauncherSettings settings, bool persistChanges = true)
    {
        var tabSet = new HashSet<string>(settings.SessionTabs, StringComparer.OrdinalIgnoreCase);
        bool tabsChanged = false;

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

            // Auto-recover tabs referenced by sessions but missing from settings
            if (!tabSet.Contains(session.Tab))
            {
                settings.SessionTabs.Add(session.Tab);
                tabSet.Add(session.Tab);
                tabsChanged = true;
            }
        }

        if (tabsChanged && persistChanges)
        {
            Program.Logger.LogInformation("Auto-recovered missing session tabs from session state data");
            settings.Save();
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
    /// Core background refresh: refreshes session data and notifications.
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
            EventsJournalService.ApplyLiveCwdOverlay(sessions, this._eventsJournal);
            this._cachedSessions = sessions;
            this.ApplySessionStates(this._cachedSessions);
            this.WriteSessionMetadata();
            var snapshot = await Task.Run(() => this._refreshCoordinator.RefreshActiveStatus(this._cachedSessions)).ConfigureAwait(true);

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
    /// Full refresh: background data + visual grid. Used by user-triggered actions (context menu, etc.).
    /// </summary>
    private async void RefreshActiveStatusAsync()
    {
        await this.RefreshBackgroundCoreAsync().ConfigureAwait(true);
        this.PopulateGridWithFilter(this._lastSnapshot);
    }

    /// <summary>
    /// Handles the Edge save signal detected via WindowTitleChanged event.
    /// Extracts the session ID from the title, reads tab URLs via UI Automation,
    /// saves them, and writes the lastSaved timestamp to session-signals.js.
    /// </summary>
    private async void HandleEdgeSaveSignalAsync(IntPtr hwnd, string title)
    {
        var sessionId = EdgeWorkspaceService.ExtractSessionId(title);
        if (sessionId == null)
        {
            return;
        }

        // Debounce: skip if a save is already in progress for this session
        if (!this._saveInProgress.Add(sessionId))
        {
            Program.Logger.LogDebug("[SaveSignal] Save already in progress for {SessionId}, skipping duplicate", sessionId);
            return;
        }

        Program.Logger.LogInformation("[SaveSignal] Detected ::Save for session {SessionId}", sessionId);

        try
        {
            // GetTabUrls requires STA thread for UI Automation
            var urls = await Task.Factory.StartNew(() =>
            {
                if (!this._activeTracker.TryGetEdge(sessionId, out var ws))
                {
                    Program.Logger.LogWarning("[SaveSignal] TryGetEdge returned false for {SessionId} — edge not tracked (tracked count: {Count})", sessionId, this._activeTracker.GetTrackedEdgeWorkspaces().Count());
                    return [];
                }

                Program.Logger.LogInformation("[SaveSignal] Found Edge workspace for {SessionId}, HWND={Hwnd}, IsOpen={IsOpen}", sessionId, ws.CachedHwnd, ws.IsOpen);
                return ws.GetTabUrls();
            }, CancellationToken.None, TaskCreationOptions.None, StaTaskScheduler.Instance).ConfigureAwait(true);

            if (urls.Count > 0)
            {
                EdgeTabPersistenceService.SaveTabs(sessionId, urls);
                this._contextWatcher?.UpdateTabCount(sessionId, urls.Count);
                Program.Logger.LogInformation("[SaveSignal] Saved {Count} tabs for session {SessionId}", urls.Count, sessionId);
                this._toast.Show($"✅ Edge state saved — {urls.Count} tab(s) stored");
            }
            else if (EdgeTabPersistenceService.HasSavedTabs(sessionId))
            {
                // No tabs found now but there were previously saved tabs — clear them
                EdgeTabPersistenceService.SaveTabs(sessionId, []);
                this._contextWatcher?.UpdateTabCount(sessionId, 0);
                Program.Logger.LogInformation("[SaveSignal] Cleared previously saved tabs for session {SessionId}", sessionId);
                this._toast.Show("✅ Edge state saved — previous tabs cleared");
            }
            else
            {
                this._toast.Show("No tabs to save — only the session anchor tab was found");
            }

            // Always write lastSaved timestamp so session.html resets the button
            this._lastSavedBySession[sessionId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            EdgeWorkspaceService.WriteSessionSignals(this._lastSavedBySession);
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("[SaveSignal] Failed to save tabs for {SessionId}: {Error}", sessionId, ex.Message);
        }
        finally
        {
            this._saveInProgress.Remove(sessionId);
        }
    }

    private void RequestRefresh(string? sessionId = null, bool trackingChanged = false, bool dataChanged = false, bool fullRefresh = false)
    {
        if (this.IsDisposed || !this.IsHandleCreated)
        {
            return;
        }

        if (sessionId != null)
        {
            if (trackingChanged)
            {
                this._dirtyTrackingSessionIds.Add(sessionId);
            }

            if (dataChanged)
            {
                this._dirtyDataSessionIds.Add(sessionId);
            }
        }

        if (fullRefresh)
        {
            this._dirtyFullRefresh = true;
        }

        this._refreshDebounceTimer!.Stop();
        this._refreshDebounceTimer.Start();
    }

    private async void OnDebouncedRefreshAsync(object? sender, EventArgs e)
    {
        this._refreshDebounceTimer!.Stop();

        if (this._dirtyFullRefresh)
        {
            this._dirtyFullRefresh = false;
            this._dirtyTrackingSessionIds.Clear();
            this._dirtyDataSessionIds.Clear();
            await this.RefreshBackgroundCoreAsync().ConfigureAwait(true);
            this.PopulateGridWithFilter(this._lastSnapshot);
            return;
        }

        // For data changes: reload affected sessions
        if (this._dirtyDataSessionIds.Count > 0)
        {
            var sessions = (List<NamedSession>)await Task.Run(() => this._refreshCoordinator.LoadSessions()).ConfigureAwait(true);
            EventsJournalService.ApplyLiveCwdOverlay(sessions, this._eventsJournal);
            this._cachedSessions = sessions;
            this.ApplySessionStates(this._cachedSessions);
        }

        // For tracking changes: build incremental snapshot
        if (this._dirtyTrackingSessionIds.Count > 0 || this._dirtyDataSessionIds.Count > 0)
        {
            var snapshot = this._activeTracker.IncrementalRefresh(this._cachedSessions);
            this._lastSnapshot = snapshot;

            if (this._dirtyTrackingSessionIds.Count > 0 && this._dirtyDataSessionIds.Count == 0)
            {
                this._sessionsVisuals.GridVisuals.UpdateGridIncremental(snapshot);
            }
            else
            {
                this.PopulateGridWithFilter(snapshot);
            }
        }

        this._dirtyTrackingSessionIds.Clear();
        this._dirtyDataSessionIds.Clear();
    }

    private Dictionary<string, string> BuildSessionSummaryMap()
    {
        return ActiveStatusTracker.BuildSessionSummaryMap(this._cachedSessions);
    }

    private void OnLatestCwdChanged(string sessionId, string cwd)
    {
        if (!this.IsHandleCreated)
        {
            return;
        }

        this.BeginInvoke(() =>
        {
            var session = this._cachedSessions.FirstOrDefault(s => string.Equals(s.Id, sessionId, StringComparison.OrdinalIgnoreCase));
            if (session == null)
            {
                return;
            }

            session.Cwd = cwd;
            session.Folder = Path.GetFileName(cwd.TrimEnd('\\'));
            this.RequestRefresh(sessionId: sessionId, dataChanged: true);
        });
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

    private async Task CheckWelcomePopupAsync()
    {
        if (Program._settings.WelcomePopupDismissed)
        {
            return;
        }

        try
        {
            var starred = await Task.Run(() => this._githubApi.IsRepoStarredAsync("rogerbarreto", "copilot-booster")).ConfigureAwait(true);
            if (starred)
            {
                return;
            }

            var dismissed = WelcomePopupVisuals.Show(this._githubApi);
            if (dismissed)
            {
                Program._settings.WelcomePopupDismissed = true;
                Program._settings.Save();
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Welcome popup check failed: {Error}", ex.Message);
        }
    }

    private async Task LoadInitialDataAsync()
    {
        var sessions = (List<NamedSession>)await Task.Run(() => this._refreshCoordinator.LoadSessions()).ConfigureAwait(true);
        this._cachedSessions = sessions;
        this.ApplySessionStates(this._cachedSessions);

        // Startup rescan: bind pre-existing copilot.exe processes to their hosts
        // This must run BEFORE the first RefreshActiveStatus so active icons light up
        await Task.Run(() => this._activeTracker.RescanExistingSessions()).ConfigureAwait(true);

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

        // Edge scan uses UI Automation (COM/STA) — run once at startup on a dedicated STA thread
        bool edgeChanged = await Task.Factory.StartNew(
            () => this._activeTracker.ScanAndTrackEdgeWorkspaces(),
            CancellationToken.None,
            TaskCreationOptions.None,
            StaTaskScheduler.Instance).ConfigureAwait(true);
        if (edgeChanged)
        {
            snapshot = await Task.Run(() => this._refreshCoordinator.RefreshActiveStatus(this._cachedSessions)).ConfigureAwait(true);
        }

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

    private void CheckForMissingSessionCwds()
    {
        if (this._cachedSessions == null)
        {
            return;
        }

        var missing = this._cachedSessions
            .Where(s => !string.IsNullOrEmpty(s.Cwd) && !Directory.Exists(s.Cwd))
            .Select(s => s.Alias ?? s.Id)
            .ToList();

        if (missing.Count > 0)
        {
            var names = string.Join(", ", missing.Take(3));
            var suffix = missing.Count > 3 ? $" (+{missing.Count - 3} more)" : "";
            this._toast.ShowWarning($"⚠️ {missing.Count} session(s) have missing CWD: {names}{suffix} — edit session to fix");
        }
    }

    /// <summary>
    /// Validates the CWD for a session. If it doesn't exist, prompts the user to select a new folder.
    /// Returns the valid CWD, or null if the user cancels.
    /// </summary>
    private string? ValidateCwdOrPrompt(string sessionId, string? cwd)
    {
        if (!string.IsNullOrEmpty(cwd) && Directory.Exists(cwd))
        {
            return cwd;
        }

        var displayCwd = cwd ?? "(not set)";
        var result = MessageBox.Show(
            $"The working directory for this session no longer exists:\n\n{displayCwd}\n\nWould you like to select a new directory?",
            "Working Directory Not Found",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (result != DialogResult.OK)
        {
            return null;
        }

        var defaultDir = Program._settings.DefaultWorkDir;
        var initialDir = !string.IsNullOrEmpty(defaultDir) && Directory.Exists(defaultDir) ? defaultDir : null;

        using var fbd = new FolderBrowserDialog();
        if (initialDir != null)
        {
            fbd.InitialDirectory = initialDir;
        }

        if (fbd.ShowDialog() != DialogResult.OK)
        {
            return null;
        }

        var newCwd = fbd.SelectedPath;

        // Update the session's workspace.yaml with the new CWD
        var sessionDir = Path.Combine(Program.SessionStateDir, sessionId);
        SessionService.UpdateSessionCwd(sessionDir, newCwd);

        // Update cached session
        var session = this._cachedSessions?.Find(x => x.Id == sessionId);
        session?.Cwd = newCwd;

        return newCwd;
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
            var promptResult = NewSessionNameVisuals.ShowNamePrompt(selectedCwd, this._githubApi); if (promptResult == null)
            {
                return;
            }

            // Update from upstream before creating the session
            if (promptResult.UpdateSourceFirst)
            {
                var gitRoot = SessionService.FindGitRoot(selectedCwd);
                if (gitRoot != null)
                {
                    (bool updateOk, string? updateErr) = promptResult.Action switch
                    {
                        BranchAction.None =>
                            await WorkspaceCreationService.PullCurrentBranchAsync(gitRoot, CancellationToken.None).ConfigureAwait(true),
                        BranchAction.ExistingBranch when !string.IsNullOrEmpty(promptResult.BranchName) =>
                            await UpdateAndDropEffectiveRefAsync(gitRoot, promptResult.BranchName).ConfigureAwait(true),
                        BranchAction.NewBranch when !string.IsNullOrEmpty(promptResult.BaseBranch) =>
                            await UpdateAndDropEffectiveRefAsync(gitRoot, promptResult.BaseBranch).ConfigureAwait(true),
                        _ => (true, null)
                    };

                    if (!updateOk)
                    {
                        var proceed = MessageBox.Show(this,
                            $"Couldn't update from upstream:\n{updateErr}\n\nCreate session anyway with current state?",
                            "Update Failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (proceed == DialogResult.No)
                        {
                            return;
                        }
                    }
                }
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
                var wsResult = WorkspaceCreatorVisuals.ShowWorkspaceCreator(gitRoot, this._githubApi);
                if (wsResult != null)
                {
                    var sid = await CopilotSessionCreatorService.CreateSessionAsync(wsResult.Value.WorktreePath, wsResult.Value.SessionName, CopilotSessionCreatorService.FindTemplateSessionDir()).ConfigureAwait(true);
                    if (sid != null)
                    {
                        if (!string.IsNullOrWhiteSpace(wsResult.Value.SessionName))
                        {
                            SessionAliasService.SetAlias(Program.SessionAliasFile, sid, wsResult.Value.SessionName);
                        }

                        if (wsResult.Value.GitHubLink is { } link)
                        {
                            try
                            {
                                GitHubTrackingService.AddItem(sid, link.Owner, link.Repo, link.Item);
                            }
                            catch (Exception ex)
                            {
                                Program.Logger.LogWarning("[WorkspaceCreator] Failed to auto-link {Type} #{Number}: {Error}", link.Item.Type, link.Item.Number, ex.Message);
                                this._toast.ShowWarning($"⚠️ Session created. Couldn't auto-link {link.Item.Type} #{link.Item.Number} — {ex.Message}");
                            }

                            try { this._githubPoller?.PollSessionNow(sid); }
                            catch (Exception ex) { Program.Logger.LogWarning("[WorkspaceCreator] PollSessionNow failed: {Error}", ex.Message); }

                            try { this.AiDetectionService.Reset(sid); }
                            catch (Exception ex) { Program.Logger.LogWarning("[WorkspaceCreator] AiDetectionService.Reset failed: {Error}", ex.Message); }

                            var url = GitHubLinkService.GetItemUrl(link.Owner, link.Repo, link.Item);
                            var existingTabs = EdgeTabPersistenceService.LoadTabs(sid);
                            if (!existingTabs.Contains(url))
                            {
                                existingTabs.Add(url);
                                EdgeTabPersistenceService.SaveTabs(sid, existingTabs);
                            }
                        }

                        this._interactionManager.LaunchSession(sid);
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
            using var fbd = new FolderBrowserDialog { InitialDirectory = SettingsVisuals.GetBrowseInitialDirectory(Program._settings.DefaultWorkDir) };
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
            "events.jsonl", "workspace.yaml", "workspace-deleted.yaml", "session.db", "vscode.metadata.json"
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

            // Skip .lock files (e.g. inuse.48696.lock created by Copilot CLI)
            if (fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            files.Add((relativePath, file));
        }

        return files;
    }
}
