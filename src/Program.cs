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

class Program
{
    const string UpdaterMutexName = "Global\\CopilotJumpListUpdater";
    const string UpdateLockName = "Global\\CopilotJumpListUpdateLock";

    static readonly string CopilotDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot");
    internal static readonly string SessionStateDir = Path.Combine(CopilotDir, "session-state");
    static readonly string PidRegistryFile = Path.Combine(CopilotDir, "active-pids.json");
    static readonly string SignalFile = Path.Combine(CopilotDir, "ui-signal.txt");
    static readonly string LastUpdateFile = Path.Combine(CopilotDir, "jumplist-lastupdate.txt");
    static readonly string LogFile = Path.Combine(CopilotDir, "launcher.log");
    static readonly string LauncherExePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
    internal static readonly string CopilotExePath = CopilotLocator.FindCopilotExe();

    internal static LauncherSettings _settings = null!;
    static Form? _hiddenForm;
    static Process? _copilotProcess;
    static MainForm? _mainForm;

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
    static void Main(string[] args)
    {
        LogService.Log("Launcher started", LogFile);

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

        // Settings / Existing Sessions / Open IDE share a single MainForm window
        if (showSettings || openExisting || openIdeSessionId != null)
        {
            if (openIdeSessionId != null)
            {
                IdePickerForm.OpenIdeForSession(openIdeSessionId);
                return;
            }

            int desiredTab = showSettings ? 1 : 0;

            // Check if another CopilotApp MainForm is already open
            var existing = Process.GetProcessesByName("CopilotApp")
                .Where(p => p.Id != Environment.ProcessId && p.MainWindowTitle == "Copilot App")
                .FirstOrDefault();

            if (existing != null)
            {
                // Signal the existing instance to switch tab and bring to front
                try { File.WriteAllText(SignalFile, desiredTab.ToString()); } catch { }
                LogService.Log($"Signaled existing MainForm (PID {existing.Id}) to switch to tab {desiredTab}", LogFile);
                return;
            }

            _mainForm = new MainForm(initialTab: desiredTab);

            // Poll for signal file from other instances
            var signalTimer = new System.Windows.Forms.Timer { Interval = 300 };
            signalTimer.Tick += (s, e) =>
            {
                try
                {
                    if (File.Exists(SignalFile))
                    {
                        var content = File.ReadAllText(SignalFile).Trim();
                        File.Delete(SignalFile);
                        if (int.TryParse(content, out int tab))
                        {
                            _mainForm.SwitchToTab(tab);
                        }
                    }
                }
                catch { }
            };
            signalTimer.Start();

            try { if (File.Exists(SignalFile)) { File.Delete(SignalFile); } } catch { }

            Application.Run(_mainForm);
            signalTimer.Stop();
            return;
        }

        // Resolve default work directory for fallback
        var defaultWorkDir = !string.IsNullOrEmpty(_settings.DefaultWorkDir) ? _settings.DefaultWorkDir
            : Environment.GetEnvironmentVariable("COPILOT_WORK_DIR")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // For new sessions (no explicit workDir, not resuming), show CWD picker
        if (workDir == null && resumeSessionId == null)
        {
            workDir = CwdPickerForm.ShowCwdPicker(defaultWorkDir);
            if (workDir == null)
            {
                return;
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
                    if (line.StartsWith("cwd:")) { workDir = line[4..].Trim(); break; }
                }
            }
        }

        workDir ??= defaultWorkDir;

        LogService.Log($"WorkDir: {workDir}, Resume: {resumeSessionId ?? "none"}", LogFile);

        // Create form - visible in taskbar for jump list but no visible window
        _hiddenForm = new Form
        {
            Text = "Copilot App",
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
                _hiddenForm.Icon = icon;
            }
        }
        catch { }

        _hiddenForm.Load += (s, e) =>
        {
            _hiddenForm.WindowState = FormWindowState.Minimized;
            _hiddenForm.ShowInTaskbar = true;
            StartCopilotSession(workDir, resumeSessionId);
        };

        Application.Run(_hiddenForm);
    }

    [ExcludeFromCodeCoverage]
    static void StartCopilotSession(string workDir, string? resumeSessionId)
    {
        var myPid = Environment.ProcessId;
        PidRegistryService.RegisterPid(myPid, CopilotDir, PidRegistryFile);
        LogService.Log($"Registered PID: {myPid}", LogFile);

        // Try to become the jump list updater (single instance)
        bool isUpdater = false;
        Mutex? updaterMutex = null;
        try
        {
            updaterMutex = new Mutex(true, UpdaterMutexName, out isUpdater);
            LogService.Log($"Is updater: {isUpdater}", LogFile);
        }
        catch (Exception ex) { LogService.Log($"Mutex error: {ex.Message}", LogFile); }

        var cts = new CancellationTokenSource();
        if (isUpdater)
        {
            var updaterThread = new Thread(() => JumpListService.UpdaterLoop(cts.Token, UpdateLockName, LastUpdateFile, LauncherExePath, CopilotExePath, PidRegistryFile, SessionStateDir, LogFile, _hiddenForm)) { IsBackground = true };
            updaterThread.Start();
        }

        LogService.Log("Starting copilot...", LogFile);

        // Snapshot existing sessions before launch
        var existingSessions = new HashSet<string>(
            Directory.Exists(SessionStateDir)
                ? Directory.GetDirectories(SessionStateDir).Select(d => Path.GetFileName(d) ?? "")
                : Array.Empty<string>());

        // Launch copilot directly with allowed tools/dirs from settings
        var copilotArgs = new List<string>();
        if (resumeSessionId != null)
        {
            copilotArgs.Add($"--resume {resumeSessionId}");
        }

        var settingsArgs = _settings.BuildCopilotArgs(copilotArgs.ToArray());

        var psi = new ProcessStartInfo
        {
            FileName = CopilotExePath,
            Arguments = settingsArgs,
            WorkingDirectory = workDir,
            UseShellExecute = true
        };

        _copilotProcess = Process.Start(psi);
        LogService.Log($"Started copilot with PID: {_copilotProcess?.Id}", LogFile);

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
                PidRegistryService.UpdatePidSessionId(myPid, sessionId, PidRegistryFile);
                LogService.Log($"Mapped PID {myPid} to session {sessionId}", LogFile);
            }

            LogService.Log("Updating jump list...", LogFile);
            JumpListService.TryUpdateJumpListWithLock(UpdateLockName, LastUpdateFile, LauncherExePath, CopilotExePath, PidRegistryFile, SessionStateDir, LogFile, _hiddenForm);
            LogService.Log("Jump list updated", LogFile);

            // Watch for copilot exit
            var exitWatcher = new Thread(() =>
            {
                _copilotProcess?.WaitForExit();
                LogService.Log("copilot exited", LogFile);

                PidRegistryService.UnregisterPid(myPid, PidRegistryFile);
                JumpListService.TryUpdateJumpListWithLock(UpdateLockName, LastUpdateFile, LauncherExePath, CopilotExePath, PidRegistryFile, SessionStateDir, LogFile, _hiddenForm);

                cts.Cancel();
                updaterMutex?.ReleaseMutex();
                updaterMutex?.Dispose();

                _hiddenForm?.Invoke(() => Application.Exit());
            })
            { IsBackground = true };
            exitWatcher.Start();
        };
        timer.Start();
    }
}

