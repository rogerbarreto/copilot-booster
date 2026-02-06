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

[ExcludeFromCodeCoverage]
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
                LaunchSessionAndClose();
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
                LaunchSessionAndClose();
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
                        IdePickerForm.OpenIdeForSession(sid);
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

    void LaunchSessionAndClose()
    {
        if (_selectedSessionId != null)
        {
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            Process.Start(new ProcessStartInfo(exePath, $"--resume {_selectedSessionId}")
            {
                UseShellExecute = false
            });
        }
        Close();
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

    internal static List<NamedSession> LoadNamedSessions() => SessionService.LoadNamedSessions(Program.SessionStateDir);

    internal static List<NamedSession> LoadNamedSessions(string sessionStateDir) => SessionService.LoadNamedSessions(sessionStateDir);

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
