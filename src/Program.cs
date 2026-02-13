using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using CopilotApp.Forms;
using CopilotApp.Models;
using CopilotApp.Services;

[assembly: InternalsVisibleTo("CopilotApp.Tests")]

/// <summary>
/// Entry point for the Copilot launcher application.
/// </summary>
internal class Program
{
    private const string UpdaterMutexName = "Global\\CopilotJumpListUpdater";
    private const string UpdateLockName = "Global\\CopilotJumpListUpdateLock";

    private static readonly string s_copilotDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot");
    /// <summary>
    /// Directory path for persisted session state.
    /// </summary>
    internal static readonly string SessionStateDir = Path.Combine(s_copilotDir, "session-state");
    internal static readonly string PidRegistryFile = Path.Combine(s_copilotDir, "active-pids.json");
    private static readonly string s_signalFile = Path.Combine(s_copilotDir, "ui-signal.txt");
    private static readonly string s_lastUpdateFile = Path.Combine(s_copilotDir, "jumplist-lastupdate.txt");
    private static readonly string s_logFile = Path.Combine(s_copilotDir, "launcher.log");
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
    /// Parses command-line arguments into a structured <see cref="ParsedArgs"/> result.
    /// </summary>
    /// <param name="args">The command-line arguments to parse.</param>
    /// <returns>A <see cref="ParsedArgs"/> instance containing the parsed values.</returns>
    internal static ParsedArgs ParseArguments(string[] args)
    {
        string? resumeSessionId = null;
        bool openExisting = false;
        bool showSettings = false;
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

        return new ParsedArgs(resumeSessionId, openExisting, showSettings, openIdeSessionId, workDir);
    }

    [STAThread]
    [ExcludeFromCodeCoverage]
    private static void Main(string[] args)
    {
        LogService.Log("Launcher started", s_logFile);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Load settings (creates defaults on first run)
        _settings = LauncherSettings.Load();

        // Parse arguments
        var parsed = ParseArguments(args);
        string? resumeSessionId = parsed.ResumeSessionId;
        bool openExisting = parsed.OpenExisting;
        bool showSettings = parsed.ShowSettings;
        string? openIdeSessionId = parsed.OpenIdeSessionId;
        string? workDir = parsed.WorkDir;

        LogService.Log($"Args: resume={resumeSessionId ?? "null"}, openExisting={openExisting}, settings={showSettings}", s_logFile);

        // Settings / Existing Sessions / Open IDE share a single MainForm window
        if (showSettings || openExisting || openIdeSessionId != null)
        {
            if (openIdeSessionId != null)
            {
                IdePickerForm.OpenIdeForSession(openIdeSessionId);
                return;
            }

            int desiredTab = showSettings ? 2 : 1;

            // Check if another CopilotApp MainForm is already open
            var existing = Process.GetProcessesByName("CopilotApp")
                .Where(p => p.Id != Environment.ProcessId && p.MainWindowTitle == "Copilot App")
                .FirstOrDefault();

            if (existing != null)
            {
                // Signal the existing instance to switch tab and bring to front
                try
                {
                    File.WriteAllText(s_signalFile, desiredTab.ToString());
                }
                catch { }
                LogService.Log($"Signaled existing MainForm (PID {existing.Id}) to switch to tab {desiredTab}", s_logFile);
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
                            s_mainForm.SwitchToTab(tab);
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

        // For new sessions (no explicit workDir, not resuming), show MainForm
        if (workDir == null && resumeSessionId == null)
        {
            var mainForm = new MainForm(initialTab: 0);
            if (mainForm.ShowDialog() == DialogResult.OK && mainForm.NewSessionCwd != null)
            {
                workDir = mainForm.NewSessionCwd;
            }
            else
            {
                return;
            }
        }

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

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{CopilotExePath}\" {settingsArgs}\"",
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
                LogService.Log($"Mapped PID {myPid} to session {sessionId}, cmd PID {cmdPid}", s_logFile);
            }

            LogService.Log("Updating jump list...", s_logFile);
            JumpListService.TryUpdateJumpListWithLock(UpdateLockName, s_lastUpdateFile, s_launcherExePath, CopilotExePath, PidRegistryFile, SessionStateDir, s_logFile, s_hiddenForm);
            LogService.Log("Jump list updated", s_logFile);

            // Watch for copilot exit
            var exitWatcher = new Thread(() =>
            {
                s_copilotProcess?.WaitForExit();
                LogService.Log("copilot exited", s_logFile);

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

