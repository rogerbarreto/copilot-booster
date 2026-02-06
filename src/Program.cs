using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Taskbar;
using Microsoft.WindowsAPICodePack.Shell;

#region Settings Model

class LauncherSettings
{
    static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "launcher-settings.json");

    [JsonPropertyName("allowedTools")]
    public List<string> AllowedTools { get; set; } = new();

    [JsonPropertyName("allowedDirs")]
    public List<string> AllowedDirs { get; set; } = new();

    [JsonPropertyName("defaultWorkDir")]
    public string DefaultWorkDir { get; set; } = "";

    [JsonPropertyName("ides")]
    public List<IdeEntry> Ides { get; set; } = new();

    public static LauncherSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<LauncherSettings>(json) ?? CreateDefault();
            }
        }
        catch { }

        var settings = CreateDefault();
        settings.Save();
        return settings;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFile)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, options));
        }
        catch { }
    }

    static LauncherSettings CreateDefault()
    {
        return new LauncherSettings
        {
            AllowedTools = new List<string>(),
            AllowedDirs = new List<string>(),
            DefaultWorkDir = ""
        };
    }

    public string BuildCopilotArgs(string[] extraArgs)
    {
        var parts = new List<string>();
        foreach (var tool in AllowedTools)
            parts.Add($"\"--allow-tool={tool}\"");
        foreach (var dir in AllowedDirs)
            parts.Add($"\"--add-dir={dir}\"");
        foreach (var arg in extraArgs)
            parts.Add(arg);
        return string.Join(" ", parts);
    }
}

class IdeEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    public override string ToString() => string.IsNullOrEmpty(Description) ? Path : $"{Description}  —  {Path}";
}

#endregion

#region MainForm

class MainForm : Form
{
    readonly TabControl _mainTabs;
    readonly TabPage _sessionsTab;
    readonly TabPage _settingsTab;

    // Sessions tab controls
    readonly ListView _sessionListView;
    string? _selectedSessionId;

    // Settings tab controls
    readonly ListBox _toolsList;
    readonly ListBox _dirsList;
    readonly ListView _idesList;
    readonly TextBox _workDirBox;

    public string? SelectedSessionId => _selectedSessionId;

    public MainForm(int initialTab = 0)
    {
        Text = "Copilot App";
        Size = new Size(700, 550);
        MinimumSize = new Size(550, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;

        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null) Icon = icon;
        }
        catch { }

        _mainTabs = new TabControl { Dock = DockStyle.Fill };

        // ===== Sessions Tab =====
        _sessionsTab = new TabPage("Existing Sessions");

        _sessionListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Tile,
            FullRowSelect = true,
            MultiSelect = false,
            TileSize = new Size(560, 65)
        };
        _sessionListView.Columns.Add("Session");
        _sessionListView.Columns.Add("CWD");
        _sessionListView.Columns.Add("Date");

        _sessionListView.DoubleClick += (s, e) =>
        {
            if (_sessionListView.SelectedItems.Count > 0)
            {
                _selectedSessionId = _sessionListView.SelectedItems[0].Tag as string;
                DialogResult = DialogResult.OK;
                Close();
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
            if (_sessionListView.SelectedItems.Count > 0)
            {
                _selectedSessionId = _sessionListView.SelectedItems[0].Tag as string;
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        var btnRefresh = new Button { Text = "Refresh", Width = 80 };
        btnRefresh.Click += (s, e) => RefreshSessionList();

        sessionButtonPanel.Controls.Add(btnOpenSession);

        if (Program._settings.Ides.Count > 0)
        {
            var btnIde = new Button { Text = "Open in IDE", Width = 100 };
            btnIde.Click += (s, e) =>
            {
                if (_sessionListView.SelectedItems.Count > 0)
                {
                    var sid = _sessionListView.SelectedItems[0].Tag as string;
                    if (sid != null)
                        Program.OpenIdeForSession(sid);
                }
            };
            sessionButtonPanel.Controls.Add(btnIde);
        }

        sessionButtonPanel.Controls.Add(btnRefresh);

        _sessionsTab.Controls.Add(_sessionListView);
        _sessionsTab.Controls.Add(sessionButtonPanel);

        RefreshSessionList();

        // ===== Settings Tab =====
        _settingsTab = new TabPage("Settings");

        var settingsContainer = new Panel { Dock = DockStyle.Fill };

        var settingsTabs = new TabControl { Dock = DockStyle.Fill };

        // --- Allowed Tools tab ---
        var toolsTab = new TabPage("Allowed Tools");
        _toolsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var tool in Program._settings.AllowedTools)
            _toolsList.Items.Add(tool);

        var toolsButtons = CreateListButtons(_toolsList, "Tool name:", "Add Tool", addBrowse: false);
        toolsTab.Controls.Add(_toolsList);
        toolsTab.Controls.Add(toolsButtons);

        // --- Allowed Directories tab ---
        var dirsTab = new TabPage("Allowed Directories");
        _dirsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var dir in Program._settings.AllowedDirs)
            _dirsList.Items.Add(dir);

        var dirsButtons = CreateListButtons(_dirsList, "Directory path:", "Add Directory", addBrowse: true);
        dirsTab.Controls.Add(_dirsList);
        dirsTab.Controls.Add(dirsButtons);

        // --- IDEs tab ---
        var idesTab = new TabPage("IDEs");
        _idesList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true
        };
        _idesList.Columns.Add("Description", 200);
        _idesList.Columns.Add("Path", 400);
        foreach (var ide in Program._settings.Ides)
        {
            var item = new ListViewItem(ide.Description);
            item.SubItems.Add(ide.Path);
            _idesList.Items.Add(item);
        }

        var ideButtons = CreateIdeButtons();
        idesTab.Controls.Add(_idesList);
        idesTab.Controls.Add(ideButtons);

        settingsTabs.TabPages.Add(toolsTab);
        settingsTabs.TabPages.Add(dirsTab);
        settingsTabs.TabPages.Add(idesTab);

        // --- Default Work Dir ---
        var workDirPanel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 4) };
        var workDirLabel = new Label { Text = "Default Work Dir:", AutoSize = true, Location = new Point(8, 12) };
        _workDirBox = new TextBox
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
            using var fbd = new FolderBrowserDialog { SelectedPath = _workDirBox.Text };
            if (fbd.ShowDialog() == DialogResult.OK)
                _workDirBox.Text = fbd.SelectedPath;
        };
        workDirPanel.Controls.AddRange(new Control[] { workDirLabel, _workDirBox, workDirBrowse });

        // --- Bottom buttons ---
        var settingsBottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 6, 8, 6)
        };

        var btnCancel = new Button { Text = "Cancel", Width = 90 };
        btnCancel.Click += (s, e) => ReloadSettingsUI();

        var btnSave = new Button { Text = "Save", Width = 90 };
        btnSave.Click += (s, e) =>
        {
            Program._settings.AllowedTools = _toolsList.Items.Cast<string>().ToList();
            Program._settings.AllowedDirs = _dirsList.Items.Cast<string>().ToList();
            Program._settings.DefaultWorkDir = _workDirBox.Text.Trim();
            Program._settings.Ides = new List<IdeEntry>();
            foreach (ListViewItem item in _idesList.Items)
                Program._settings.Ides.Add(new IdeEntry { Description = item.Text, Path = item.SubItems[1].Text });
            Program._settings.Save();
            MessageBox.Show("Settings saved.", "Copilot App", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        settingsBottomPanel.Controls.Add(btnCancel);
        settingsBottomPanel.Controls.Add(btnSave);

        settingsContainer.Controls.Add(settingsTabs);
        settingsContainer.Controls.Add(workDirPanel);
        settingsContainer.Controls.Add(settingsBottomPanel);

        _settingsTab.Controls.Add(settingsContainer);

        // ===== Add tabs to main control =====
        _mainTabs.TabPages.Add(_sessionsTab);
        _mainTabs.TabPages.Add(_settingsTab);
        Controls.Add(_mainTabs);

        if (initialTab >= 0 && initialTab < _mainTabs.TabPages.Count)
            _mainTabs.SelectedIndex = initialTab;
    }

    public void SwitchToTab(int tabIndex)
    {
        if (tabIndex >= 0 && tabIndex < _mainTabs.TabPages.Count)
            _mainTabs.SelectedIndex = tabIndex;

        if (tabIndex == 0)
            RefreshSessionList();

        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    void RefreshSessionList()
    {
        _sessionListView.Items.Clear();
        var sessions = LoadNamedSessions();
        foreach (var session in sessions)
        {
            var item = new ListViewItem(session.Summary) { Tag = session.Id };
            item.SubItems.Add(session.Cwd);
            item.SubItems.Add(session.LastModified.ToString("yyyy-MM-dd HH:mm"));
            _sessionListView.Items.Add(item);
        }
    }

    void ReloadSettingsUI()
    {
        var fresh = LauncherSettings.Load();

        _toolsList.Items.Clear();
        foreach (var tool in fresh.AllowedTools)
            _toolsList.Items.Add(tool);

        _dirsList.Items.Clear();
        foreach (var dir in fresh.AllowedDirs)
            _dirsList.Items.Add(dir);

        _idesList.Items.Clear();
        foreach (var ide in fresh.Ides)
        {
            var item = new ListViewItem(ide.Description);
            item.SubItems.Add(ide.Path);
            _idesList.Items.Add(item);
        }

        _workDirBox.Text = fresh.DefaultWorkDir;
    }

    internal static List<NamedSession> LoadNamedSessions()
    {
        var results = new List<NamedSession>();
        if (!Directory.Exists(Program.SessionStateDir)) return results;

        var sessions = Directory.GetDirectories(Program.SessionStateDir)
            .OrderByDescending(d => Directory.GetLastWriteTime(d))
            .Select(d =>
            {
                var wsFile = Path.Combine(d, "workspace.yaml");
                if (!File.Exists(wsFile)) return null;

                try
                {
                    var lines = File.ReadAllLines(wsFile);
                    string? id = null, cwd = null, summary = null;
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("id:")) id = line[3..].Trim();
                        else if (line.StartsWith("cwd:")) cwd = line[4..].Trim();
                        else if (line.StartsWith("summary:")) summary = line[8..].Trim();
                    }

                    if (id == null || string.IsNullOrWhiteSpace(summary)) return null;

                    var folder = Path.GetFileName(cwd?.TrimEnd('\\') ?? "");
                    return new NamedSession
                    {
                        Id = id,
                        Cwd = cwd ?? "",
                        Summary = $"[{folder}] {summary}",
                        LastModified = Directory.GetLastWriteTime(d)
                    };
                }
                catch { return null; }
            })
            .Where(s => s != null)
            .Take(50)
            .ToList();

        foreach (var s in sessions)
            if (s != null) results.Add(s);

        return results;
    }

    Panel CreateListButtons(ListBox listBox, string promptText, string addTitle, bool addBrowse)
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
            if (listBox.SelectedIndex < 0) return;
            var current = listBox.SelectedItem?.ToString() ?? "";

            if (addBrowse)
            {
                using var fbd = new FolderBrowserDialog { SelectedPath = current };
                if (fbd.ShowDialog() == DialogResult.OK)
                    listBox.Items[listBox.SelectedIndex] = fbd.SelectedPath;
            }
            else
            {
                var value = PromptInput("Edit", promptText, current);
                if (value != null)
                    listBox.Items[listBox.SelectedIndex] = value;
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
                    listBox.SelectedIndex = Math.Min(idx, listBox.Items.Count - 1);
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

        panel.Controls.AddRange(new Control[] { btnAdd, btnEdit, btnRemove, btnUp, btnDown });
        return panel;
    }

    Panel CreateIdeButtons()
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
                _idesList.Items.Add(item);
                item.Selected = true;
                _idesList.Focus();
            }
        };

        var btnEdit = new Button { Text = "Edit", Width = 88 };
        btnEdit.Click += (s, e) =>
        {
            if (_idesList.SelectedItems.Count == 0) return;
            var sel = _idesList.SelectedItems[0];
            var result = PromptIdeEntry("Edit IDE", sel.SubItems[1].Text, sel.Text);
            if (result != null)
            {
                sel.Text = result.Value.desc;
                sel.SubItems[1].Text = result.Value.path;
            }
            _idesList.Focus();
        };

        var btnRemove = new Button { Text = "Remove", Width = 88 };
        btnRemove.Click += (s, e) =>
        {
            if (_idesList.SelectedItems.Count > 0)
            {
                int idx = _idesList.SelectedIndices[0];
                _idesList.Items.RemoveAt(idx);
                if (_idesList.Items.Count > 0)
                {
                    int newIdx = Math.Min(idx, _idesList.Items.Count - 1);
                    _idesList.Items[newIdx].Selected = true;
                }
                _idesList.Focus();
            }
        };

        panel.Controls.AddRange(new Control[] { btnAdd, btnEdit, btnRemove });
        return panel;
    }

    static (string path, string desc)? PromptIdeEntry(string title, string defaultPath, string defaultDesc)
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
                txtPath.Text = ofd.FileName;
        };

        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(310, 118), Width = 75 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(392, 118), Width = 75 };

        form.Controls.AddRange(new Control[] { lblDesc, txtDesc, lblPath, txtPath, btnBrowse, btnOk, btnCancel });
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(txtPath.Text))
            return (txtPath.Text, txtDesc.Text);
        return null;
    }

    static string? PromptInput(string title, string label, string defaultValue)
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

        form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? txt.Text : null;
    }
}

class NamedSession
{
    public string Id { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime LastModified { get; set; }
}

#endregion

class Program
{
    const string AppId = "GitHub.CopilotCLI.Permissive";
    const string UpdaterMutexName = "Global\\CopilotJumpListUpdater";
    const string UpdateLockName = "Global\\CopilotJumpListUpdateLock";

    static readonly string CopilotDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot");
    internal static readonly string SessionStateDir = Path.Combine(CopilotDir, "session-state");
    static readonly string PidRegistryFile = Path.Combine(CopilotDir, "active-pids.json");
    static readonly string LastUpdateFile = Path.Combine(CopilotDir, "jumplist-lastupdate.txt");
    static readonly string LogFile = Path.Combine(CopilotDir, "launcher.log");
    static readonly string LauncherExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
    internal static readonly string CopilotExePath = FindCopilotExe();

    internal static LauncherSettings _settings = null!;
    static Form? _hiddenForm;
    static Process? _copilotProcess;
    static MainForm? _mainForm;

    [DllImport("shell32.dll", SetLastError = true)]
    static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    static string FindCopilotExe()
    {
        // Search common install locations
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WinGet\Packages\GitHub.Copilot.Prerelease_Microsoft.Winget.Source_8wekyb3d8bbwe\copilot.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WinGet\Packages\GitHub.Copilot_Microsoft.Winget.Source_8wekyb3d8bbwe\copilot.exe"),
        };

        foreach (var path in candidates)
            if (File.Exists(path)) return path;

        // Fallback: try to find copilot in PATH
        try
        {
            var psi = new ProcessStartInfo("where", "copilot")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit();
            if (!string.IsNullOrEmpty(output) && File.Exists(output.Split('\n')[0].Trim()))
                return output.Split('\n')[0].Trim();
        }
        catch { }

        return "copilot.exe";
    }

    static void Log(string message)
    {
        try
        {
            if (!Directory.Exists(CopilotDir))
                Directory.CreateDirectory(CopilotDir);
            File.AppendAllText(LogFile, $"[{DateTime.Now:o}] {message}\n");
        }
        catch { }
    }

    [STAThread]
    static void Main(string[] args)
    {
        Log("Launcher started");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        SetCurrentProcessExplicitAppUserModelID(AppId);

        // Load settings (creates defaults on first run)
        _settings = LauncherSettings.Load();

        // Parse arguments
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

        // If settings mode, show MainForm on Settings tab and exit
        if (showSettings)
        {
            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                _mainForm.SwitchToTab(1);
            }
            else
            {
                _mainForm = new MainForm(initialTab: 1);
                Application.Run(_mainForm);
            }
            return;
        }

        // If open-ide mode, show IDE picker for the given session
        if (openIdeSessionId != null)
        {
            OpenIdeForSession(openIdeSessionId);
            return;
        }

        // Resolve default work directory for fallback
        var defaultWorkDir = !string.IsNullOrEmpty(_settings.DefaultWorkDir) ? _settings.DefaultWorkDir
            : Environment.GetEnvironmentVariable("COPILOT_WORK_DIR")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // If open-existing mode, show session picker
        if (openExisting)
        {
            resumeSessionId = ShowSessionPicker();
            if (resumeSessionId == null) return;
        }

        // For new sessions (no explicit workDir, not resuming), show CWD picker
        if (workDir == null && resumeSessionId == null)
        {
            workDir = ShowCwdPicker(defaultWorkDir);
            if (workDir == null) return;
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

        Log($"WorkDir: {workDir}, Resume: {resumeSessionId ?? "none"}");

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
            if (icon != null) _hiddenForm.Icon = icon;
        }
        catch { }

        _hiddenForm.Load += (s, e) =>
        {
            _hiddenForm.WindowState = FormWindowState.Minimized;
            _hiddenForm.Visible = false;
            _hiddenForm.ShowInTaskbar = true;
            StartCopilotSession(workDir, resumeSessionId);
        };

        Application.Run(_hiddenForm);
    }

    static void StartCopilotSession(string workDir, string? resumeSessionId)
    {
        var myPid = Environment.ProcessId;
        RegisterPid(myPid);
        Log($"Registered PID: {myPid}");

        // Try to become the jump list updater (single instance)
        bool isUpdater = false;
        Mutex? updaterMutex = null;
        try
        {
            updaterMutex = new Mutex(true, UpdaterMutexName, out isUpdater);
            Log($"Is updater: {isUpdater}");
        }
        catch (Exception ex) { Log($"Mutex error: {ex.Message}"); }

        var cts = new CancellationTokenSource();
        if (isUpdater)
        {
            var updaterThread = new Thread(() => UpdaterLoop(cts.Token)) { IsBackground = true };
            updaterThread.Start();
        }

        Log("Starting copilot...");

        // Snapshot existing sessions before launch
        var existingSessions = new HashSet<string>(
            Directory.Exists(SessionStateDir)
                ? Directory.GetDirectories(SessionStateDir).Select(d => Path.GetFileName(d) ?? "")
                : Array.Empty<string>());

        // Launch copilot directly with allowed tools/dirs from settings
        var copilotArgs = new List<string>();
        if (resumeSessionId != null)
            copilotArgs.Add($"--resume {resumeSessionId}");
        var settingsArgs = _settings.BuildCopilotArgs(copilotArgs.ToArray());

        var psi = new ProcessStartInfo
        {
            FileName = CopilotExePath,
            Arguments = settingsArgs,
            WorkingDirectory = workDir,
            UseShellExecute = true
        };

        _copilotProcess = Process.Start(psi);
        Log($"Started copilot with PID: {_copilotProcess?.Id}");

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
                UpdatePidSessionId(myPid, sessionId);
                Log($"Mapped PID {myPid} to session {sessionId}");
            }

            Log("Updating jump list...");
            TryUpdateJumpListWithLock();
            Log("Jump list updated");

            // Watch for copilot exit
            var exitWatcher = new Thread(() =>
            {
                _copilotProcess?.WaitForExit();
                Log("copilot exited");

                UnregisterPid(myPid);
                TryUpdateJumpListWithLock();

                cts.Cancel();
                updaterMutex?.ReleaseMutex();
                updaterMutex?.Dispose();

                _hiddenForm?.Invoke(() => Application.Exit());
            }) { IsBackground = true };
            exitWatcher.Start();
        };
        timer.Start();
    }

    #region Jump List

    static void TryUpdateJumpListWithLock()
    {
        try
        {
            using var updateLock = new Mutex(false, UpdateLockName);
            if (updateLock.WaitOne(TimeSpan.FromSeconds(5)))
            {
                try
                {
                    File.WriteAllText(LastUpdateFile, DateTime.UtcNow.ToString("o"));
                    UpdateJumpList();
                }
                finally
                {
                    updateLock.ReleaseMutex();
                }
            }
        }
        catch (Exception ex) { Log($"TryUpdateJumpListWithLock error: {ex.Message}"); }
    }

    static bool ShouldBackgroundUpdate(TimeSpan minInterval)
    {
        try
        {
            if (!File.Exists(LastUpdateFile)) return true;
            var lastUpdate = DateTime.Parse(File.ReadAllText(LastUpdateFile).Trim());
            return DateTime.UtcNow - lastUpdate > minInterval;
        }
        catch { return true; }
    }

    static void UpdaterLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (ShouldBackgroundUpdate(TimeSpan.FromMinutes(1)))
            {
                _hiddenForm?.Invoke(() => TryUpdateJumpListWithLock());
            }

            for (int i = 0; i < 300 && !ct.IsCancellationRequested; i++)
                Thread.Sleep(1000);
        }
    }

    static void UpdateJumpList()
    {
        try
        {
            var activeSessions = GetActiveSessions();

            if (_hiddenForm == null || !_hiddenForm.IsHandleCreated)
            {
                Log("Hidden form not ready");
                return;
            }

            var jumpList = JumpList.CreateJumpListForIndividualWindow(AppId, _hiddenForm.Handle);
            jumpList.KnownCategoryToDisplay = JumpListKnownCategoryType.Neither;
            jumpList.ClearAllUserTasks();

            var newSessionTask = new JumpListLink(LauncherExePath, "New Copilot Session")
            {
                IconReference = new IconReference(LauncherExePath, 0)
            };

            var openExistingTask = new JumpListLink(LauncherExePath, "Existing Sessions")
            {
                Arguments = "--open-existing",
                IconReference = new IconReference(LauncherExePath, 0)
            };

            var settingsTask = new JumpListLink(LauncherExePath, "Settings")
            {
                Arguments = "--settings",
                IconReference = new IconReference(LauncherExePath, 0)
            };

            jumpList.AddUserTasks(newSessionTask, new JumpListSeparator(), openExistingTask, new JumpListSeparator(), settingsTask);

            var category = new JumpListCustomCategory("Active Sessions");
            foreach (var session in activeSessions)
            {
                var link = new JumpListLink(LauncherExePath, session.Summary)
                {
                    Arguments = $"--resume {session.Id}",
                    IconReference = new IconReference(LauncherExePath, 0),
                    WorkingDirectory = session.Cwd
                };
                category.AddJumpListItems(link);
            }
            jumpList.AddCustomCategories(category);

            jumpList.Refresh();
            Log($"Jump list updated: {activeSessions.Count} sessions");
        }
        catch (Exception ex)
        {
            Log($"UpdateJumpList error: {ex.Message}");
        }
    }

    #endregion

    #region PID Registry

    static void RegisterPid(int pid)
    {
        try
        {
            if (!Directory.Exists(CopilotDir))
                Directory.CreateDirectory(CopilotDir);

            Dictionary<string, object> registry = new();
            if (File.Exists(PidRegistryFile))
            {
                try { registry = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(PidRegistryFile)) ?? new(); }
                catch { }
            }
            registry[pid.ToString()] = new { started = DateTime.Now.ToString("o"), sessionId = (string?)null };
            File.WriteAllText(PidRegistryFile, JsonSerializer.Serialize(registry));
        }
        catch { }
    }

    static void UpdatePidSessionId(int pid, string sessionId)
    {
        try
        {
            if (!File.Exists(PidRegistryFile)) return;
            var json = File.ReadAllText(PidRegistryFile);
            var registry = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();

            registry[pid.ToString()] = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(new { started = DateTime.Now.ToString("o"), sessionId }));

            File.WriteAllText(PidRegistryFile, JsonSerializer.Serialize(registry));
        }
        catch { }
    }

    static void UnregisterPid(int pid)
    {
        try
        {
            if (!File.Exists(PidRegistryFile)) return;
            var registry = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(PidRegistryFile)) ?? new();
            registry.Remove(pid.ToString());
            File.WriteAllText(PidRegistryFile, JsonSerializer.Serialize(registry));
        }
        catch { }
    }

    #endregion

    #region Session Discovery

    static List<SessionInfo> GetActiveSessions()
    {
        var sessions = new List<SessionInfo>();
        if (!File.Exists(PidRegistryFile)) return sessions;

        Dictionary<string, JsonElement>? pidRegistry;
        try
        {
            pidRegistry = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(PidRegistryFile));
        }
        catch { return sessions; }

        if (pidRegistry == null) return sessions;

        var toRemove = new List<string>();

        foreach (var (pidStr, entry) in pidRegistry)
        {
            if (!int.TryParse(pidStr, out int pid)) continue;

            try
            {
                var proc = Process.GetProcessById(pid);
                if (proc.ProcessName != "CopilotApp")
                {
                    toRemove.Add(pidStr);
                    continue;
                }

                string? sessionId = null;
                if (entry.TryGetProperty("sessionId", out var sidProp) && sidProp.ValueKind == JsonValueKind.String)
                    sessionId = sidProp.GetString();

                if (sessionId == null) continue;

                var workspaceFile = Path.Combine(SessionStateDir, sessionId, "workspace.yaml");
                if (!File.Exists(workspaceFile)) continue;

                var session = ParseWorkspace(workspaceFile, pid);
                if (session != null)
                    sessions.Add(session);
            }
            catch { toRemove.Add(pidStr); }
        }

        if (toRemove.Count > 0)
        {
            foreach (var pid in toRemove)
                pidRegistry.Remove(pid);
            try { File.WriteAllText(PidRegistryFile, JsonSerializer.Serialize(pidRegistry)); } catch { }
        }

        return sessions;
    }

    static SessionInfo? ParseWorkspace(string path, int pid)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            string? id = null, cwd = null, summary = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("id:")) id = line[3..].Trim();
                else if (line.StartsWith("cwd:")) cwd = line[4..].Trim();
                else if (line.StartsWith("summary:")) summary = line[8..].Trim();
            }

            if (id == null) return null;

            var folder = Path.GetFileName(cwd?.TrimEnd('\\') ?? "Unknown");
            return new SessionInfo
            {
                Id = id,
                Cwd = cwd ?? "Unknown",
                Summary = string.IsNullOrEmpty(summary) ? $"[{folder}]" : $"[{folder}] {summary}",
                Pid = pid
            };
        }
        catch { return null; }
    }

    #endregion

    #region Open IDE

    internal static void OpenIdeForSession(string sessionId)
    {
        if (_settings.Ides.Count == 0)
        {
            MessageBox.Show("No IDEs configured.\nGo to Settings → IDEs tab to add one.",
                "Open in IDE", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Get session CWD
        var workspaceFile = Path.Combine(SessionStateDir, sessionId, "workspace.yaml");
        if (!File.Exists(workspaceFile))
        {
            MessageBox.Show("Session not found.", "Open in IDE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string? cwd = null;
        foreach (var line in File.ReadAllLines(workspaceFile))
        {
            if (line.StartsWith("cwd:")) { cwd = line[4..].Trim(); break; }
        }

        if (string.IsNullOrEmpty(cwd))
        {
            MessageBox.Show("Session has no working directory.", "Open in IDE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var repoRoot = FindGitRoot(cwd);
        bool hasRepo = repoRoot != null && !string.Equals(repoRoot, cwd, StringComparison.OrdinalIgnoreCase);

        // Build a compact picker: one row per IDE, each with CWD and Repo buttons
        var form = new Form
        {
            Text = "Open in IDE",
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12)
        };

        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null) form.Icon = icon;
        }
        catch { }

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = hasRepo ? 3 : 2,
            Padding = new Padding(0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };

        // Header
        var lblIde = new Label { Text = "IDE", Font = new Font(SystemFonts.DefaultFont.FontFamily, 9, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 4, 8, 4) };
        layout.Controls.Add(lblIde, 0, 0);
        var lblCwd = new Label { Text = $"CWD: {cwd}", Font = new Font(SystemFonts.DefaultFont.FontFamily, 8), AutoSize = true, Padding = new Padding(0, 4, 4, 4), ForeColor = Color.Gray };
        layout.Controls.Add(lblCwd, 1, 0);
        if (hasRepo)
        {
            var lblRepo = new Label { Text = $"Repo: {repoRoot}", Font = new Font(SystemFonts.DefaultFont.FontFamily, 8), AutoSize = true, Padding = new Padding(0, 4, 0, 4), ForeColor = Color.Gray };
            layout.Controls.Add(lblRepo, 2, 0);
        }

        int row = 1;
        foreach (var ide in _settings.Ides)
        {
            var ideName = new Label
            {
                Text = ide.Description,
                AutoSize = true,
                Padding = new Padding(0, 6, 8, 2),
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 9.5f)
            };
            layout.Controls.Add(ideName, 0, row);

            var btnCwd = new Button { Text = "Open CWD", Width = 100, Height = 28 };
            var capturedIde = ide;
            btnCwd.Click += (s, e) =>
            {
                LaunchIde(capturedIde.Path, cwd);
                form.Close();
            };
            layout.Controls.Add(btnCwd, 1, row);

            if (hasRepo)
            {
                var btnRepo = new Button { Text = "Open Repo", Width = 100, Height = 28 };
                btnRepo.Click += (s, e) =>
                {
                    LaunchIde(capturedIde.Path, repoRoot!);
                    form.Close();
                };
                layout.Controls.Add(btnRepo, 2, row);
            }

            row++;
        }

        form.Controls.Add(layout);
        form.CancelButton = null;
        form.KeyPreview = true;
        form.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) form.Close(); };
        form.ShowDialog();
    }

    static void LaunchIde(string idePath, string folderPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = idePath,
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch IDE: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static string? FindGitRoot(string startPath)
    {
        var dir = startPath;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir) break;
            dir = parent;
        }
        return null;
    }

    #endregion

    #region CWD Picker

    static string? ShowCwdPicker(string defaultWorkDir)
    {
        // Scan all session workspace.yaml files to collect CWDs and their frequency
        var cwdCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(SessionStateDir))
        {
            foreach (var dir in Directory.GetDirectories(SessionStateDir))
            {
                var wsFile = Path.Combine(dir, "workspace.yaml");
                if (!File.Exists(wsFile)) continue;
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

        var form = new Form
        {
            Text = "New Session — Select Working Directory",
            Size = new Size(600, 420),
            MinimumSize = new Size(450, 300),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.Sizable
        };

        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null) form.Icon = icon;
        }
        catch { }

        var listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true
        };
        listView.Columns.Add("Directory", 420);
        listView.Columns.Add("Sessions", 80, HorizontalAlignment.Center);

        foreach (var cwd in sortedCwds)
        {
            var item = new ListViewItem(cwd) { Tag = cwd };
            item.SubItems.Add(cwdCounts[cwd].ToString());
            listView.Items.Add(item);
        }

        if (listView.Items.Count > 0)
            listView.Items[0].Selected = true;

        string? selectedPath = null;

        listView.DoubleClick += (s, e) =>
        {
            if (listView.SelectedItems.Count > 0)
            {
                selectedPath = listView.SelectedItems[0].Tag as string;
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5)
        };

        var btnCancel = new Button { Text = "Cancel", Width = 80 };
        btnCancel.Click += (s, e) => { form.DialogResult = DialogResult.Cancel; form.Close(); };

        var btnOpen = new Button { Text = "Start", Width = 80 };
        btnOpen.Click += (s, e) =>
        {
            if (listView.SelectedItems.Count > 0)
            {
                selectedPath = listView.SelectedItems[0].Tag as string;
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        var btnBrowse = new Button { Text = "Browse...", Width = 90 };
        btnBrowse.Click += (s, e) =>
        {
            using var fbd = new FolderBrowserDialog { SelectedPath = defaultWorkDir };
            if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(fbd.SelectedPath))
            {
                selectedPath = fbd.SelectedPath;
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        // RightToLeft flow: Cancel first (rightmost), then Start, then Browse
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOpen);
        buttonPanel.Controls.Add(btnBrowse);

        form.Controls.Add(listView);
        form.Controls.Add(buttonPanel);
        form.AcceptButton = btnOpen;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? selectedPath : null;
    }

    #endregion

    #region Session Picker

    static string? ShowSessionPicker()
    {
        var sessions = MainForm.LoadNamedSessions();
        if (sessions.Count == 0)
        {
            MessageBox.Show("No named sessions found.", "Existing Sessions", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var form = new MainForm(initialTab: 0);
        return form.ShowDialog() == DialogResult.OK ? form.SelectedSessionId : null;
    }

    #endregion
}

class SessionInfo
{
    public string Id { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string Summary { get; set; } = "";
    public int Pid { get; set; }
}

