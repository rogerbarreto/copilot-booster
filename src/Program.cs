using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CopilotBooster.Forms;
using CopilotBooster.Models;
using CopilotBooster.Services;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("CopilotBooster.Tests")]

/// <summary>
/// Entry point for the Copilot launcher application.
/// </summary>
internal class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private const string AppUserModelId = "CopilotBooster";

    private const string UpdaterMutexName = "Global\\CopilotJumpListUpdater";
    private const string UpdateLockName = "Global\\CopilotJumpListUpdateLock";
    private const string MainFormMutexName = "Local\\CopilotBoosterMainForm";

    private static readonly string s_copilotDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot");
    /// <summary>
    /// Directory path for CopilotBooster application data in %APPDATA%.
    /// </summary>
    internal static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CopilotBooster");
    /// <summary>
    /// Directory path for persisted session state (owned by copilot CLI).
    /// </summary>
    internal static readonly string SessionStateDir = Path.Combine(s_copilotDir, "session-state");
    internal static readonly string PidRegistryFile = Path.Combine(AppDataDir, "active-pids.json");
    internal static readonly string TerminalCacheFile = Path.Combine(AppDataDir, "terminal-cache.json");
    internal static readonly string IdeCacheFile = Path.Combine(AppDataDir, "ide-cache.json");
    internal static readonly string SessionAliasFile = Path.Combine(AppDataDir, "session-aliases.json");
    private static readonly string s_signalFile = Path.Combine(AppDataDir, "ui-signal.txt");
    private static readonly string s_lastUpdateFile = Path.Combine(AppDataDir, "jumplist-lastupdate.txt");
    private static readonly string s_logFile = Path.Combine(AppDataDir, "launcher.log");
    private static readonly string s_launcherExePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
    /// <summary>
    /// Absolute path to the Copilot CLI executable.
    /// </summary>
    internal static readonly string CopilotExePath = CopilotLocator.FindCopilotExe();

    /// <summary>
    /// Current launcher settings loaded from disk.
    /// </summary>
    internal static LauncherSettings _settings = null!;

    /// <summary>
    /// Cached application icon extracted once at startup.
    /// </summary>
    internal static Icon? AppIcon { get; private set; }

    /// <summary>
    /// Application-wide logger. Uses <see cref="LogLevel.Debug"/> for profiling,
    /// <see cref="LogLevel.Information"/> for general operational logs.
    /// Minimum level is <see cref="LogLevel.Information"/> by default;
    /// configure via Settings → Log Level or the <c>logLevel</c> field in settings.json.
    /// </summary>
    internal static ILogger Logger { get; set; } = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private static Form? s_hiddenForm;
    private static Process? s_copilotProcess;
    private static MainForm? s_mainForm;

    /// <summary>
    /// Migrates CopilotBooster-owned files from the old ~/.copilot/ location to %APPDATA%\CopilotBooster\.
    /// This is a one-time migration that runs on startup. Failure does not prevent startup.
    /// </summary>
    private static void MigrateFromCopilotDir()
    {
        try
        {
            // List of files to migrate from old location to new location
            string[] filesToMigrate = new[]
            {
                "active-pids.json",
                "terminal-cache.json",
                "ide-cache.json",
                "ui-signal.txt",
                "jumplist-lastupdate.txt",
                "launcher.log",
                "launcher-settings.json",
                "pinned-directories.json"
            };

            bool migrationOccurred = false;

            foreach (string fileName in filesToMigrate)
            {
                string oldPath = Path.Combine(s_copilotDir, fileName);
                string newPath = Path.Combine(AppDataDir, fileName);

                // Check if file exists in old location and not in new location
                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    try
                    {
                        // Copy from old location to new location
                        File.Copy(oldPath, newPath, overwrite: false);

                        // Delete the old file after successful copy
                        File.Delete(oldPath);

                        migrationOccurred = true;
                        Logger.LogInformation("Migrated {FileName} from ~/.copilot/ to %APPDATA%\\CopilotBooster\\", fileName);
                    }
                    catch (Exception ex)
                    {
                        // Log individual file migration failure but continue with other files
                        Logger.LogWarning("Failed to migrate {FileName}: {Error}", fileName, ex.Message);
                    }
                }
            }

            if (migrationOccurred)
            {
                Logger.LogInformation("File migration completed successfully");
            }
        }
        catch (Exception ex)
        {
            // Wrap in try/catch so migration failure does not prevent startup
            Logger.LogError("Migration error: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Parses command-line arguments into a structured <see cref="ParsedArgs"/> result.
    /// </summary>
    /// <param name="args">The command-line arguments to parse.</param>
    /// <returns>A <see cref="ParsedArgs"/> instance containing the parsed values.</returns>
    internal static ParsedArgs ParseArguments(string[] args)
    {
        string? resumeSessionId = null;
        bool openExisting = false;
        bool showSettings = false;
        bool newSession = false;
        string? openIdeSessionId = null;
        string? workDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--resume" && i + 1 < args.Length)
            {
                resumeSessionId = args[i + 1];
                i++;
            }
            else if (args[i] == "--open-existing")
            {
                openExisting = true;
            }
            else if (args[i] == "--settings")
            {
                showSettings = true;
            }
            else if (args[i] == "--new-session")
            {
                newSession = true;
            }
            else if (args[i] == "--open-ide" && i + 1 < args.Length)
            {
                openIdeSessionId = args[i + 1];
                i++;
            }
            else
            {
                workDir = args[i];
            }
        }

        return new ParsedArgs(resumeSessionId, openExisting, showSettings, newSession, openIdeSessionId, workDir);
    }

    [STAThread]
    [ExcludeFromCodeCoverage]
    private static void Main(string[] args)
    {
        // Set AppUserModelID so Windows associates our JumpList with the correct taskbar button
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

        // Ensure AppDataDir exists before any file operations
        Directory.CreateDirectory(AppDataDir);

        // Parse arguments early to determine log level
        var parsed = ParseArguments(args);

        // Load settings (creates defaults on first run)
        _settings = LauncherSettings.Load();

        // Initialize logger — priority: settings > compile-time default
        LogLevel minLevel;
        if (_settings.LogLevel != null && Enum.TryParse<LogLevel>(_settings.LogLevel, ignoreCase: true, out var configuredLevel))
        {
            minLevel = configuredLevel;
        }
        else
        {
#if DEBUG
            minLevel = LogLevel.Debug;
#else
            minLevel = LogLevel.Information;
#endif
        }
        Logger = new FileLogger(s_logFile, minLevel);

        // Cache app icon once
        try
        {
            AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Failed to extract app icon: {Error}", ex.Message);
        }

        // Perform one-time migration of files from old location
        MigrateFromCopilotDir();

        Logger.LogInformation("Launcher started");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        ThemeService.ApplyTheme(_settings.Theme);

        string? resumeSessionId = parsed.ResumeSessionId;
        bool openExisting = parsed.OpenExisting;
        bool showSettings = parsed.ShowSettings;
        bool newSession = parsed.NewSession;
        string? openIdeSessionId = parsed.OpenIdeSessionId;
        string? workDir = parsed.WorkDir;

        Logger.LogInformation("Args: resume={ResumeId}, openExisting={OpenExisting}, settings={ShowSettings}, newSession={NewSession}",
            resumeSessionId ?? "null", openExisting, showSettings, newSession);

        // Settings / Existing Sessions / Open IDE share a single MainForm window
        if (showSettings || openExisting || newSession || openIdeSessionId != null || (workDir == null && resumeSessionId == null))
        {
            if (openIdeSessionId != null)
            {
                IdePickerVisuals.OpenIdeForSession(openIdeSessionId);
                return;
            }

            int desiredTab = showSettings ? 2 : newSession ? 0 : 1;

            // Use a Mutex to detect if another MainForm is already open
            using var mainFormMutex = new Mutex(true, MainFormMutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                // Signal the existing instance to switch tab and bring to front
                try
                {
                    File.WriteAllText(s_signalFile, desiredTab.ToString());
                }
                catch (Exception ex) { Logger.LogWarning("Failed to write signal file: {Error}", ex.Message); }
                Logger.LogInformation("Signaled existing MainForm to switch to tab {Tab}", desiredTab);
                return;
            }

            s_mainForm = new MainForm(initialTab: desiredTab);

            // Poll for signal file from other instances
            var signalTimer = new System.Windows.Forms.Timer { Interval = 300 };
            signalTimer.Tick += (s, e) =>
            {
                try
                {
                    if (File.Exists(s_signalFile))
                    {
                        var content = File.ReadAllText(s_signalFile).Trim();
                        File.Delete(s_signalFile);
                        if (int.TryParse(content, out int tab))
                        {
                            s_mainForm.SwitchToTabAsync(tab);
                        }
                    }
                }
                catch (Exception ex) { Logger.LogWarning("Failed to signal existing instance: {Error}", ex.Message); }
            };
            signalTimer.Start();

            try
            {
                if (File.Exists(s_signalFile))
                {
                    File.Delete(s_signalFile);
                }
            }
            catch (Exception ex) { Logger.LogWarning("Failed to delete signal file: {Error}", ex.Message); }

            Application.Run(s_mainForm);
            signalTimer.Stop();
            return;
        }

        // Resolve default work directory for fallback
        var defaultWorkDir = !string.IsNullOrEmpty(_settings.DefaultWorkDir) ? _settings.DefaultWorkDir
            : Environment.GetEnvironmentVariable("COPILOT_WORK_DIR")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);


        // If resuming a session that is already running, focus its terminal window
        if (resumeSessionId != null)
        {
            var activeSessions = SessionService.GetActiveSessions(PidRegistryFile, SessionStateDir);
            var existing = activeSessions.FirstOrDefault(s => s.Id == resumeSessionId);
            if (existing != null && existing.CopilotPid > 0)
            {
                // CopilotPid stores the cmd.exe PID that hosts the copilot process
                if (WindowFocusService.TryFocusProcessWindow(existing.CopilotPid))
                {
                    Logger.LogInformation("Focused terminal (cmd PID {CopilotPid}) for session {SessionId}", existing.CopilotPid, resumeSessionId);
                    return;
                }
            }
        }

        // When resuming, always use the session's original CWD
        if (resumeSessionId != null)
        {
            var wsFile = Path.Combine(SessionStateDir, resumeSessionId, "workspace.yaml");
            if (File.Exists(wsFile))
            {
                foreach (var line in File.ReadAllLines(wsFile))
                {
                    if (line.StartsWith("cwd:"))
                    {
                        workDir = line[4..].Trim();
                        break;
                    }
                }
            }
        }

        workDir ??= defaultWorkDir;

        Logger.LogInformation("WorkDir: {WorkDir}, Resume: {ResumeId}", workDir, resumeSessionId ?? "none");

        // Create form - visible in taskbar for jump list but no visible window
        s_hiddenForm = new Form
        {
            Text = "Copilot Session",
            ShowInTaskbar = true,
            WindowState = FormWindowState.Minimized,
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
            MinimizeBox = true,
            Size = new Size(0, 0),
            Opacity = 0,
            ShowIcon = false
        };

        if (AppIcon != null)
        {
            s_hiddenForm.Icon = AppIcon;
        }

        s_hiddenForm.Load += (s, e) =>
        {
            s_hiddenForm.WindowState = FormWindowState.Minimized;
            s_hiddenForm.ShowInTaskbar = true;
            StartCopilotSession(workDir, resumeSessionId);
        };

        Application.Run(s_hiddenForm);
    }

    [ExcludeFromCodeCoverage]
    private static void StartCopilotSession(string workDir, string? resumeSessionId)
    {
        var myPid = Environment.ProcessId;
        PidRegistryService.RegisterPid(myPid, s_copilotDir, PidRegistryFile);
        Logger.LogInformation("Registered PID: {Pid}", myPid);

        // Try to become the jump list updater (single instance)
        bool isUpdater = false;
        Mutex? updaterMutex = null;
        try
        {
            updaterMutex = new Mutex(true, UpdaterMutexName, out isUpdater);
            Logger.LogInformation("Is updater: {IsUpdater}", isUpdater);
        }
        catch (Exception ex)
        {
            Logger.LogError("Mutex error: {Error}", ex.Message);
        }

        var cts = new CancellationTokenSource();
        if (isUpdater)
        {
            var updaterThread = new Thread(() => JumpListService.UpdaterLoop(UpdateLockName, s_lastUpdateFile, s_launcherExePath, CopilotExePath, PidRegistryFile, SessionStateDir, s_hiddenForm, cts.Token)) { IsBackground = true };
            updaterThread.Start();
        }

        Logger.LogInformation("Starting copilot...");

        // Snapshot existing sessions before launch
        var existingSessions = new HashSet<string>(
            Directory.Exists(SessionStateDir)
                ? Directory.GetDirectories(SessionStateDir).Select(d => Path.GetFileName(d) ?? "")
                : []);

        // Launch copilot directly with allowed tools/dirs from settings
        var copilotArgs = new List<string>();
        if (resumeSessionId != null)
        {
            copilotArgs.Add($"--resume {resumeSessionId}");
        }

        var settingsArgs = _settings.BuildCopilotArgs(copilotArgs.ToArray());

        // Set a trackable title for the console window
        var titlePrefix = resumeSessionId != null
            ? $"Copilot CLI - {resumeSessionId}"
            : "Copilot CLI";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"title {titlePrefix} && \"{CopilotExePath}\" {settingsArgs}\"",
            WorkingDirectory = workDir,
            UseShellExecute = true
        };

        s_copilotProcess = Process.Start(psi);
        Logger.LogInformation("Started copilot with PID: {Pid}", s_copilotProcess?.Id);

        // Update jump list after session creation delay
        var timer = new System.Windows.Forms.Timer { Interval = 3000 };
        timer.Tick += (s, e) =>
        {
            timer.Stop();

            // Map this PID to its session
            string? sessionId = resumeSessionId;
            if (sessionId == null && Directory.Exists(SessionStateDir))
            {
                var newSession = Directory.GetDirectories(SessionStateDir)
                    .Select(d => Path.GetFileName(d) ?? "")
                    .FirstOrDefault(d => !string.IsNullOrEmpty(d) && !existingSessions.Contains(d));
                sessionId = newSession;
            }

            if (sessionId != null)
            {
                int cmdPid = s_copilotProcess?.Id ?? 0;
                PidRegistryService.UpdatePidSessionId(myPid, sessionId, PidRegistryFile, copilotPid: cmdPid);
                TerminalCacheService.CacheTerminal(TerminalCacheFile, sessionId, cmdPid);
                Logger.LogInformation("Mapped PID {MyPid} to session {SessionId}, cmd PID {CmdPid}", myPid, sessionId, cmdPid);

                // For new sessions, update the console window title now that we know the session ID
                if (resumeSessionId == null && s_copilotProcess != null)
                {
                    try
                    {
                        var hwnd = s_copilotProcess.MainWindowHandle;
                        if (hwnd != IntPtr.Zero)
                        {
                            SetWindowText(hwnd, $"Copilot CLI - {sessionId}");
                        }
                    }
                    catch (Exception ex) { Logger.LogDebug("Failed to set window title: {Error}", ex.Message); }
                }
            }

            Logger.LogInformation("Updating jump list...");
            JumpListService.TryUpdateJumpListWithLock(UpdateLockName, s_lastUpdateFile, s_launcherExePath, CopilotExePath, PidRegistryFile, SessionStateDir, s_hiddenForm);

            // Watch for copilot exit
            var exitWatcher = new Thread(() =>
            {
                s_copilotProcess?.WaitForExit();
                Logger.LogInformation("copilot exited");

                if (sessionId != null)
                {
                    TerminalCacheService.RemoveTerminal(TerminalCacheFile, sessionId);
                }

                PidRegistryService.UnregisterPid(myPid, PidRegistryFile);
                JumpListService.TryUpdateJumpListWithLock(UpdateLockName, s_lastUpdateFile, s_launcherExePath, CopilotExePath, PidRegistryFile, SessionStateDir, s_hiddenForm);

                cts.Cancel();
                updaterMutex?.Dispose();

                s_hiddenForm?.Invoke(() => Application.Exit());
            })
            { IsBackground = true };
            exitWatcher.Start();
        };
        timer.Start();
    }
}

