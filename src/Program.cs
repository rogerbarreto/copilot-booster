using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CopilotBooster.Forms;
using CopilotBooster.Models;
using CopilotBooster.Services;

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
                        LogService.Log($"Migrated {fileName} from ~/.copilot/ to %APPDATA%\\CopilotBooster\\", s_logFile);
                    }
                    catch (Exception ex)
                    {
                        // Log individual file migration failure but continue with other files
                        LogService.Log($"Failed to migrate {fileName}: {ex.Message}", s_logFile);
                    }
                }
            }

            if (migrationOccurred)
            {
                LogService.Log("File migration completed successfully", s_logFile);
            }
        }
        catch (Exception ex)
        {
            // Wrap in try/catch so migration failure does not prevent startup
            LogService.Log($"Migration error: {ex.Message}", s_logFile);
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

        // Perform one-time migration of files from old location
        MigrateFromCopilotDir();

        LogService.Log("Launcher started", s_logFile);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Load settings (creates defaults on first run)
        _settings = LauncherSettings.Load();
        ThemeService.ApplyTheme(_settings.Theme);

        // Parse arguments
        var parsed = ParseArguments(args);
        string? resumeSessionId = parsed.ResumeSessionId;
        bool openExisting = parsed.OpenExisting;
        bool showSettings = parsed.ShowSettings;
        bool newSession = parsed.NewSession;
        string? openIdeSessionId = parsed.OpenIdeSessionId;
        string? workDir = parsed.WorkDir;

        LogService.Log($"Args: resume={resumeSessionId ?? "null"}, openExisting={openExisting}, settings={showSettings}, newSession={newSession}", s_logFile);

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
                catch { }
                LogService.Log($"Signaled existing MainForm to switch to tab {desiredTab}", s_logFile);
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
                catch { }
            };
            signalTimer.Start();

            try
            {
                if (File.Exists(s_signalFile))
                {
                    File.Delete(s_signalFile);
                }
            }
            catch { }

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
                    LogService.Log($"Focused terminal (cmd PID {existing.CopilotPid}) for session {resumeSessionId}", s_logFile);
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

        LogService.Log($"WorkDir: {workDir}, Resume: {resumeSessionId ?? "none"}", s_logFile);

        // Create form - visible in taskbar for jump list but no visible window
        s_hiddenForm = new Form
        {
            Text = "Copilot Session",
            ShowInTaskbar = true,
            WindowState = FormWindowState.Minimized,
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
            MinimizeBox = true,
            Size = new System.Drawing.Size(0, 0),
            Opacity = 0,
            ShowIcon = false
        };

        // Set window icon
        try
        {
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null)
            {
                s_hiddenForm.Icon = icon;
            }
        }
        catch { }

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
        LogService.Log($"Registered PID: {myPid}", s_logFile);

        // Try to become the jump list updater (single instance)
        bool isUpdater = false;
        Mutex? updaterMutex = null;
        try
        {
            updaterMutex = new Mutex(true, UpdaterMutexName, out isUpdater);
            LogService.Log($"Is updater: {isUpdater}", s_logFile);
        }
        catch (Exception ex)
        {
            LogService.Log($"Mutex error: {ex.Message}", s_logFile);
        }

        var cts = new CancellationTokenSource();
        if (isUpdater)
        {
            var updaterThread = new Thread(() => JumpListService.UpdaterLoop(UpdateLockName, s_lastUpdateFile, s_launcherExePath, CopilotExePath, PidRegistryFile, SessionStateDir, s_logFile, s_hiddenForm, cts.Token)) { IsBackground = true };
            updaterThread.Start();
        }

        LogService.Log("Starting copilot...", s_logFile);

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
        LogService.Log($"Started copilot with PID: {s_copilotProcess?.Id}", s_logFile);

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
                LogService.Log($"Mapped PID {myPid} to session {sessionId}, cmd PID {cmdPid}", s_logFile);

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
                    catch { }
                }
            }

            LogService.Log("Updating jump list...", s_logFile);
            JumpListService.TryUpdateJumpListWithLock(UpdateLockName, s_lastUpdateFile, s_launcherExePath, CopilotExePath, PidRegistryFile, SessionStateDir, s_logFile, s_hiddenForm);
            LogService.Log("Jump list updated", s_logFile);

            // Watch for copilot exit
            var exitWatcher = new Thread(() =>
            {
                s_copilotProcess?.WaitForExit();
                LogService.Log("copilot exited", s_logFile);

                if (sessionId != null)
                {
                    TerminalCacheService.RemoveTerminal(TerminalCacheFile, sessionId);
                }

                PidRegistryService.UnregisterPid(myPid, PidRegistryFile);
                JumpListService.TryUpdateJumpListWithLock(UpdateLockName, s_lastUpdateFile, s_launcherExePath, CopilotExePath, PidRegistryFile, SessionStateDir, s_logFile, s_hiddenForm);

                cts.Cancel();
                updaterMutex?.ReleaseMutex();
                updaterMutex?.Dispose();

                s_hiddenForm?.Invoke(() => Application.Exit());
            })
            { IsBackground = true };
            exitWatcher.Start();
        };
        timer.Start();
    }
}

