using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CopilotBooster.Models;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Settings form with a VS Code-style category tree on the left and content panels on the right.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class SettingsForm : Form
{
    private readonly IReadOnlyList<NamedSession> _cachedSessions;
    private readonly UpdateInfo? _latestUpdate;
    private readonly ICopilotProbe? _copilotProbe;
    private readonly ToolTip _validationToolTip = new();
    private CheckBox _aiEnabledCheck = null!;
    private NumericUpDown _aiTimeoutSecondsBox = null!;
    private NumericUpDown _aiConfidenceThresholdBox = null!;
    private TextBox _aiCopilotPathBox = null!;
    private Panel _aiCopilotPathBorder = null!;
    private Label _aiCopilotPathError = null!;
    private TextBox _aiModelBox = null!;
    private bool _suppressThemeChange;
    private string _initialCopilotPath = "";

    internal SettingsForm(IReadOnlyList<NamedSession> cachedSessions, UpdateInfo? latestUpdate, ICopilotProbe? copilotProbe = null)
    {
        this._cachedSessions = cachedSessions;
        this._latestUpdate = latestUpdate;
        this._copilotProbe = copilotProbe;

        this.Text = "Settings";
        this.Size = new Size(880, 620);
        this.MinimumSize = new Size(680, 480);
        this.FormBorderStyle = FormBorderStyle.Sizable;
        SettingsVisuals.AlignWithParent(this);
        this.TopMost = Program._settings.AlwaysOnTop;

        this.BuildLayout();
    }

    private void BuildLayout()
    {
        // Bottom buttons
        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 6, 8, 6)
        };

        // Main split: left tree + right content
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 240,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 1,
            Panel1MinSize = 160
        };

        // === TreeView (left panel) ===
        var tree = new TreeView
        {
            Dock = DockStyle.Fill,
            ShowLines = false,
            ShowPlusMinus = true,
            ShowRootLines = false,
            FullRowSelect = true,
            HideSelection = false,
            ItemHeight = 28,
            Font = new Font(this.Font.FontFamily, 10f),
            BorderStyle = BorderStyle.None,
            Indent = 16
        };

        var categoryNames = new[] { "General", "IDEs", "Git && GitHub", "Session Tabs", "Spotlight", "GitHub", "AI" };
        foreach (var name in categoryNames)
        {
            tree.Nodes.Add(name);
        }

        // Copilot CLI parent with child categories
        var copilotNode = new TreeNode("Copilot CLI", [
            new TreeNode("Allowed Directories"),
            new TreeNode("Allowed Tools"),
            new TreeNode("Allowed URLs")
        ]);
        tree.Nodes.Insert(1, copilotNode);
        copilotNode.Expand();

        // Content host panel on the right
        var contentHost = new Panel { Dock = DockStyle.Fill };

        // =====================================================================
        // GENERAL
        // =====================================================================
        var (generalPanel, generalBody) = this.CreateCategoryPanel("General", "Application-wide preferences and behavior.", autoScroll: true, padding: new Padding(8));

        var autoHideOnFocusCheck = new CheckBox
        {
            Text = "Auto-hide other session windows on focus",
            Checked = Program._settings.AutoHideOnFocus,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 4, 0, 4)
        };

        var notifyOnBellCheck = new CheckBox
        {
            Text = "Notify when session is ready (\U0001F514)",
            Checked = Program._settings.NotifyOnBell,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 4, 0, 4)
        };

        var pinnedOrderRow = new Panel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(4, 4, 0, 4) };
        var pinnedOrderLabel = new Label { Text = "Pinned order:", AutoSize = true, Location = new Point(4, 7) };
        var pinnedOrderCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(150, 4),
            Width = 180
        };
        pinnedOrderCombo.Items.AddRange(["Running first (default)", "Last updated", "Alias / Name"]);
        pinnedOrderCombo.SelectedIndex = string.Equals(Program._settings.PinnedOrder, "alias", StringComparison.OrdinalIgnoreCase) ? 2
            : string.Equals(Program._settings.PinnedOrder, "created", StringComparison.OrdinalIgnoreCase) ? 1
            : 0;
        pinnedOrderRow.Controls.AddRange([pinnedOrderLabel, pinnedOrderCombo]);

        var maxSessionsRow = new Panel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(4, 4, 0, 4) };
        var maxSessionsLabel = new Label { Text = "Max active sessions:", AutoSize = true, Location = new Point(4, 7) };
        var maxSessionsBox = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 1000,
            Value = Program._settings.MaxActiveSessions,
            Location = new Point(150, 4),
            Width = 70
        };
        var maxSessionsHint = new Label
        {
            Text = "(0 = unlimited)",
            AutoSize = true,
            Location = new Point(225, 7),
            ForeColor = Application.IsDarkModeEnabled ? Color.Gray : Color.DimGray
        };
        maxSessionsRow.Controls.AddRange([maxSessionsLabel, maxSessionsBox, maxSessionsHint]);

        var alwaysOnTopCheck = new CheckBox
        {
            Text = "Always on top",
            Checked = Program._settings.AlwaysOnTop,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 4, 0, 4)
        };

        var themeRow = new Panel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(4, 4, 0, 4) };
        var themeLabel = new Label { Text = "Theme:", AutoSize = true, Location = new Point(4, 7) };
        var themeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(150, 4),
            Width = 150
        };
        themeCombo.Items.AddRange(["System (default)", "Light", "Dark"]);
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
        themeRow.Controls.AddRange([themeLabel, themeCombo]);

        var dateFormatRow = new Panel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(4, 4, 0, 4) };
        var dateFormatLabel = new Label { Text = "Date format:", AutoSize = true, Location = new Point(4, 7) };
        var dateFormatCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(150, 4),
            Width = 200
        };
        dateFormatCombo.Items.AddRange(["YYYY-MM-DD HH:mm", "MM/DD hh:mm AM/PM", "DD/MM HH:mm"]);
        dateFormatCombo.SelectedIndex = Program._settings.DateFormat switch
        {
            "MM/dd hh:mm tt" => 1,
            "dd/MM HH:mm" => 2,
            _ => 0
        };
        dateFormatRow.Controls.AddRange([dateFormatLabel, dateFormatCombo]);

        // Add to general body (reverse visual order for Dock.Top stacking)
        var trackActiveInfo = new Label
        {
            Text = "When enabled, focusing a tracked window (IDE, Terminal, Edge) selects the session in the grid and switches tabs.",
            Dock = DockStyle.Top,
            Height = 20,
            Padding = new Padding(24, 0, 0, 4),
            ForeColor = SystemColors.GrayText,
            Font = new Font(this.Font.FontFamily, this.Font.Size - 1)
        };
        var trackActiveCheck = new CheckBox
        {
            Text = "Track active session from focused window",
            Checked = Program._settings.TrackActiveSession,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 4, 0, 0)
        };
        generalBody.Controls.Add(trackActiveInfo);
        generalBody.Controls.Add(trackActiveCheck);
        generalBody.Controls.Add(autoHideOnFocusCheck);
        generalBody.Controls.Add(notifyOnBellCheck);
        generalBody.Controls.Add(pinnedOrderRow);
        generalBody.Controls.Add(maxSessionsRow);
        generalBody.Controls.Add(alwaysOnTopCheck);
        generalBody.Controls.Add(dateFormatRow);
        generalBody.Controls.Add(themeRow);

        // =====================================================================
        // COPILOT CLI > ALLOWED DIRECTORIES
        // =====================================================================
        var (dirsPanel, dirsBody) = this.CreateCategoryPanel("Allowed Directories", "Directories the Copilot session is allowed to access for file operations.");

        var dirFieldsPanel = new Panel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(4) };

        var workDirLabel = new Label { Text = "Default Work Dir:", AutoSize = true, Location = new Point(4, 12) };
        var workDirBox = new TextBox
        {
            Text = Program._settings.DefaultWorkDir,
            Location = new Point(140, 9),
            Width = 380,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        var workDirBrowse = new Button
        {
            Text = "...",
            Width = 30,
            Location = new Point(525, 8),
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

        var wsDirLabel = new Label { Text = "Workspaces Dir:", AutoSize = true, Location = new Point(4, 52) };
        var wsDirBox = new TextBox
        {
            Text = Program._settings.WorkspacesDir,
            PlaceholderText = GitService.GetDefaultWorkspacesDir(),
            Location = new Point(140, 49),
            Width = 380,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        var wsDirBrowse = new Button
        {
            Text = "...",
            Width = 30,
            Location = new Point(525, 48),
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

        dirFieldsPanel.Controls.AddRange([workDirLabel, SettingsVisuals.WrapWithBorder(workDirBox), workDirBrowse,
            wsDirLabel, SettingsVisuals.WrapWithBorder(wsDirBox), wsDirBrowse]);

        var dirsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = Application.IsDarkModeEnabled ? BorderStyle.None : BorderStyle.Fixed3D };
        SettingsVisuals.ApplyThemedSelection(dirsList);
        foreach (var dir in Program._settings.AllowedDirs)
        {
            dirsList.Items.Add(Directory.Exists(dir) ? dir : SettingsVisuals.NotFoundPrefix + dir);
        }
        var dirsButtons = SettingsVisuals.CreateListButtons(dirsList, "Directory path:", "Add Directory", addBrowse: true);

        dirsBody.Controls.Add(dirsList);
        dirsBody.Controls.Add(dirsButtons);
        dirsBody.Controls.Add(dirFieldsPanel);

        // =====================================================================
        // COPILOT CLI > ALLOWED TOOLS
        // =====================================================================
        var (toolsPanel, toolsBody) = this.CreateCategoryPanel("Allowed Tools", "CLI tools the Copilot session is allowed to use (e.g., git, npm, dotnet).");

        var toolsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = Application.IsDarkModeEnabled ? BorderStyle.None : BorderStyle.Fixed3D };
        SettingsVisuals.ApplyThemedSelection(toolsList);
        foreach (var tool in Program._settings.AllowedTools)
        {
            toolsList.Items.Add(tool);
        }
        var toolsButtons = SettingsVisuals.CreateListButtons(toolsList, "Tool name:", "Add Tool", addBrowse: false);
        toolsBody.Controls.Add(toolsList);
        toolsBody.Controls.Add(toolsButtons);

        // =====================================================================
        // COPILOT CLI > ALLOWED URLs
        // =====================================================================
        var (urlsPanel, urlsBody) = this.CreateCategoryPanel("Allowed URLs", "URL patterns the Copilot CLI is allowed to access (global setting).");

        var urlsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = Application.IsDarkModeEnabled ? BorderStyle.None : BorderStyle.Fixed3D };
        SettingsVisuals.ApplyThemedSelection(urlsList);
        foreach (var url in CopilotConfigService.LoadAllowedUrls())
        {
            urlsList.Items.Add(url);
        }
        var urlsButtons = SettingsVisuals.CreateListButtons(urlsList, "URL or domain pattern:", "Add URL", addBrowse: false);
        urlsBody.Controls.Add(urlsList);
        urlsBody.Controls.Add(urlsButtons);

        // =====================================================================
        // IDEs
        // =====================================================================
        var (idesPanel, idesBody) = this.CreateCategoryPanel("IDEs", "IDEs available in the session context menu and search settings.");
        var idesSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 4
        };

        var idesListLabel = new Label
        {
            Text = "IDEs:",
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(4, 2, 0, 0),
            Font = new Font(this.Font.FontFamily, this.Font.Size, FontStyle.Bold)
        };
        var idesList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = !Application.IsDarkModeEnabled
        };
        idesList.Columns.Add("Description", 150);
        idesList.Columns.Add("Path", 250);
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
        idesSplit.Panel1.Controls.Add(idesList);
        idesSplit.Panel1.Controls.Add(ideButtons);
        idesSplit.Panel1.Controls.Add(idesListLabel);

        var ideSearchLabel = new Label
        {
            Text = "IDE Search Ignored Directories:",
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(4, 2, 0, 0),
            Font = new Font(this.Font.FontFamily, this.Font.Size, FontStyle.Bold)
        };
        var ignoredDirsList = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };
        foreach (var dir in Program._settings.IdeSearchIgnoredDirs)
        {
            ignoredDirsList.Items.Add(dir);
        }
        var ignoredDirsButtons = SettingsVisuals.CreateListButtons(ignoredDirsList, "Directory name to ignore:", "Add Ignored Directory", false);
        idesSplit.Panel2.Controls.Add(ignoredDirsList);
        idesSplit.Panel2.Controls.Add(ignoredDirsButtons);
        idesSplit.Panel2.Controls.Add(ideSearchLabel);

        idesBody.Controls.Add(idesSplit);

        // =====================================================================
        // GIT & GITHUB
        // =====================================================================
        var (gitPanel, gitBody) = this.CreateCategoryPanel("Git && GitHub", "Branch naming patterns for issue and PR sessions. Use {number} and {alias} as placeholders.", autoScroll: true, padding: new Padding(8));

        var prBranchRow = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 8, 0, 4) };
        var prBranchLabel = new Label { Text = "PR branch pattern:", AutoSize = true, Location = new Point(4, 12) };
        var prBranchBox = new TextBox
        {
            Text = Program._settings.PrBranchPattern,
            PlaceholderText = "prs/{number}-{alias}",
            Location = new Point(180, 9),
            Width = 340,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        prBranchRow.Controls.AddRange([prBranchLabel, SettingsVisuals.WrapWithBorder(prBranchBox)]);

        var issueBranchRow = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 8, 0, 4) };
        var issueBranchLabel = new Label { Text = "Issue branch pattern:", AutoSize = true, Location = new Point(4, 12) };
        var issueBranchBox = new TextBox
        {
            Text = Program._settings.IssueBranchPattern,
            PlaceholderText = "issues/{number}-{alias}",
            Location = new Point(180, 9),
            Width = 340,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        issueBranchRow.Controls.AddRange([issueBranchLabel, SettingsVisuals.WrapWithBorder(issueBranchBox)]);

        gitBody.Controls.Add(prBranchRow);
        gitBody.Controls.Add(issueBranchRow);

        // =====================================================================
        // SESSION TABS
        // =====================================================================
        var (sessionTabsPanel, sessionTabsBody) = this.CreateCategoryPanel("Session Tabs", "Organize sessions into tabs. At least one tab must remain. Max 20 characters per name.");

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

        var sessionTabsListPanel = new Panel { Dock = DockStyle.Fill };
        sessionTabsListPanel.Controls.Add(sessionTabsList);
        sessionTabsListPanel.Controls.Add(sessionTabsButtons);

        sessionTabsBody.Controls.Add(sessionTabsListPanel);

        // =====================================================================
        // TOAST
        // =====================================================================
        var (toastPanel, toastBody) = this.CreateCategoryPanel("Spotlight", "Spotlight (Win+Alt+X) pop-in and out for quick session management.", autoScroll: true, padding: new Padding(8));

        var toastAnimateCheck = new CheckBox
        {
            Text = "Slide animation",
            Checked = Program._settings.ToastAnimate,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(20, 4, 0, 4)
        };

        var toastScreenRow = new Panel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(20, 4, 0, 4) };
        var toastScreenLabel = new Label { Text = "Screen:", AutoSize = true, Location = new Point(4, 7) };
        var toastScreenCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(150, 4),
            Width = 180
        };
        toastScreenCombo.Items.Add("Cursor");
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var label = screens[i].Primary ? $"Primary: {i + 1}" : $"Secondary: {i + 1}";
            toastScreenCombo.Items.Add(label);
        }
        if (string.Equals(Program._settings.ToastScreen, "cursor", StringComparison.OrdinalIgnoreCase))
        {
            toastScreenCombo.SelectedIndex = 0;
        }
        else if (Program._settings.ToastScreen.StartsWith("screen-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(Program._settings.ToastScreen.AsSpan(7), out int screenIdx)
            && screenIdx >= 0 && screenIdx < screens.Length)
        {
            toastScreenCombo.SelectedIndex = screenIdx + 1;
        }
        else
        {
            var primaryIdx = Array.FindIndex(screens, s => s.Primary);
            toastScreenCombo.SelectedIndex = (primaryIdx >= 0 ? primaryIdx : 0) + 1;
        }
        toastScreenRow.Controls.AddRange([toastScreenLabel, toastScreenCombo]);

        var toastPositionRow = new Panel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(20, 4, 0, 4) };
        var toastPositionLabel = new Label { Text = "Position:", AutoSize = true, Location = new Point(4, 7) };
        var toastPositionCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(150, 4),
            Width = 180
        };
        toastPositionCombo.Items.AddRange(["Bottom Center", "Bottom Left", "Bottom Right", "Top Center", "Top Left", "Top Right"]);
        toastPositionCombo.SelectedIndex = Program._settings.ToastPosition switch
        {
            "bottom-left" => 1,
            "bottom-right" => 2,
            "top-center" => 3,
            "top-left" => 4,
            "top-right" => 5,
            _ => 0
        };
        toastPositionRow.Controls.AddRange([toastPositionLabel, toastPositionCombo]);

        var toastModeCheck = new CheckBox
        {
            Text = "Enable Spotlight (Win+Alt+X)",
            Checked = Program._settings.ToastMode,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 4, 0, 4)
        };

        var autoHideCheck = new CheckBox
        {
            Text = "Auto-hide on deactivate",
            Checked = Program._settings.SpotlightAutoHide,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(20, 4, 0, 4),
            Enabled = Program._settings.ToastMode
        };

        // Disable sub-settings when spotlight is off
        void updateSubSettings()
        {
            var enabled = toastModeCheck.Checked;
            autoHideCheck.Enabled = enabled;
            toastPositionRow.Enabled = enabled;
            toastPositionCombo.Enabled = enabled;
            toastScreenRow.Enabled = enabled;
            toastScreenCombo.Enabled = enabled;
            toastAnimateCheck.Enabled = enabled;
        }
        toastModeCheck.CheckedChanged += (s, e) => updateSubSettings();
        updateSubSettings();

        toastBody.Controls.Add(toastAnimateCheck);
        toastBody.Controls.Add(autoHideCheck);
        toastBody.Controls.Add(toastScreenRow);
        toastBody.Controls.Add(toastPositionRow);
        toastBody.Controls.Add(toastModeCheck);

        // =====================================================================
        // GITHUB
        // =====================================================================
        var (githubPanel, githubBody) = this.CreateCategoryPanel("GitHub", "GitHub integration settings for PR/Issue tracking.", autoScroll: true, padding: new Padding(8));

        // =====================================================================
        // AI
        // =====================================================================
        var (aiPanel, aiBody) = this.CreateCategoryPanel("AI", "AI auto-detect settings for GitHub issue and PR detection.", autoScroll: true, padding: new Padding(8));
        var aiSettings = Program._settings.AiDetection ?? new AiDetectionSettings();
        this._initialCopilotPath = aiSettings.CopilotPath ?? "";

        this._aiEnabledCheck = new CheckBox
        {
            Text = "Enable AI auto-detect",
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 4, 0, 4)
        };

        var aiTimeoutRow = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 8, 0, 4) };
        var aiTimeoutLabel = new Label { Text = "Per-detection timeout (seconds):", AutoSize = true, Location = new Point(4, 12) };
        this._aiTimeoutSecondsBox = new NumericUpDown
        {
            Minimum = 30,
            Maximum = 1800,
            Increment = 10,
            Location = new Point(220, 9),
            Width = 90,
            TextAlign = HorizontalAlignment.Right
        };
        aiTimeoutRow.Controls.AddRange([aiTimeoutLabel, this._aiTimeoutSecondsBox]);

        var aiConfidenceRow = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 8, 0, 4) };
        var aiConfidenceLabel = new Label { Text = "Auto-apply confidence threshold:", AutoSize = true, Location = new Point(4, 12) };
        this._aiConfidenceThresholdBox = new NumericUpDown
        {
            Minimum = 0.00M,
            Maximum = 1.00M,
            Increment = 0.05M,
            DecimalPlaces = 2,
            Location = new Point(220, 9),
            Width = 90,
            TextAlign = HorizontalAlignment.Right
        };
        aiConfidenceRow.Controls.AddRange([aiConfidenceLabel, this._aiConfidenceThresholdBox]);

        var aiCopilotPathRow = new Panel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(4, 8, 0, 4) };
        var aiCopilotPathLabel = new Label { Text = "Copilot CLI path:", AutoSize = true, Location = new Point(4, 12) };
        this._aiCopilotPathBox = new TextBox
        {
            PlaceholderText = "Empty = use PATH",
            Location = new Point(220, 9),
            Width = 300,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        this._aiCopilotPathBorder = SettingsVisuals.WrapWithBorder(this._aiCopilotPathBox);
        var aiCopilotPathBrowse = new Button
        {
            Text = "Browse...",
            Width = 90,
            Location = new Point(530, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        aiCopilotPathBrowse.Click += (s, e) =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true,
                FileName = this._aiCopilotPathBox.Text
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                this._aiCopilotPathBox.Text = dialog.FileName;
                this.ValidateCopilotPath(showTooltip: false);
            }
        };
        this._aiCopilotPathBox.Leave += (s, e) => this.ValidateCopilotPath(showTooltip: false);
        this._aiCopilotPathBox.TextChanged += (s, e) =>
        {
            if (this._aiCopilotPathError.Visible)
            {
                this.ValidateCopilotPath(showTooltip: false);
            }
        };
        this._aiCopilotPathError = new Label
        {
            Text = "File not found",
            AutoSize = true,
            Location = new Point(220, 42),
            ForeColor = Color.Red,
            Visible = false
        };
        aiCopilotPathRow.Controls.AddRange([aiCopilotPathLabel, this._aiCopilotPathBorder, aiCopilotPathBrowse, this._aiCopilotPathError]);

        var aiModelRow = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 8, 0, 4) };
        var aiModelLabel = new Label { Text = "Model (optional):", AutoSize = true, Location = new Point(4, 12) };
        this._aiModelBox = new TextBox
        {
            PlaceholderText = "Empty = let Copilot pick",
            Location = new Point(220, 9),
            Width = 300,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        aiModelRow.Controls.AddRange([aiModelLabel, SettingsVisuals.WrapWithBorder(this._aiModelBox)]);

        this.LoadAiDetectionFromSettings(aiSettings);
        aiBody.Controls.Add(aiModelRow);
        aiBody.Controls.Add(aiCopilotPathRow);
        aiBody.Controls.Add(aiConfidenceRow);
        aiBody.Controls.Add(aiTimeoutRow);
        aiBody.Controls.Add(this._aiEnabledCheck);

        // =====================================================================
        // PANEL MAP & TREE WIRING
        // =====================================================================
        var panelMap = new Dictionary<string, Panel>
        {
            ["General"] = generalPanel,
            ["Copilot CLI"] = dirsPanel,
            ["Allowed Directories"] = dirsPanel,
            ["Allowed Tools"] = toolsPanel,
            ["Allowed URLs"] = urlsPanel,
            ["IDEs"] = idesPanel,
            ["Git && GitHub"] = gitPanel,
            ["Session Tabs"] = sessionTabsPanel,
            ["Spotlight"] = toastPanel,
            ["GitHub"] = githubPanel,
            ["AI"] = aiPanel
        };

        foreach (var p in panelMap.Values)
        {
            contentHost.Controls.Add(p);
        }

        tree.AfterSelect += (s, e) =>
        {
            // If a parent node with children is clicked, select its first child instead
            if (e.Node != null && e.Node.Nodes.Count > 0)
            {
                tree.SelectedNode = e.Node.Nodes[0];
                return;
            }

            foreach (var p in panelMap.Values)
            {
                p.Visible = false;
            }

            if (e.Node != null && panelMap.TryGetValue(e.Node.Text, out var selected))
            {
                selected.Visible = true;
            }
        };

        tree.SelectedNode = tree.Nodes[0];

        split.Panel1.Controls.Add(tree);
        split.Panel2.Padding = new Padding(8, 0, 0, 0);
        split.Panel2.Controls.Add(contentHost);

        // =====================================================================
        // BOTTOM BUTTONS
        // =====================================================================
        var btnCancel = new Button { Text = "Cancel", Width = 90 };
        btnCancel.Click += (s, e) =>
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        };

        var btnSave = new Button { Text = "Save", Width = 90 };
        btnSave.Click += (s, e) =>
        {
            if (!this.ValidateCopilotPath(showTooltip: true))
            {
                tree.SelectedNode = tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => string.Equals(n.Text, "AI", StringComparison.Ordinal));
                this._aiCopilotPathBox.Focus();
                return;
            }

            // General
            Program._settings.AlwaysOnTop = alwaysOnTopCheck.Checked;
            Program._settings.MaxActiveSessions = (int)maxSessionsBox.Value;
            Program._settings.PinnedOrder = pinnedOrderCombo.SelectedIndex switch
            {
                1 => "created",
                2 => "alias",
                _ => "running"
            };
            Program._settings.NotifyOnBell = notifyOnBellCheck.Checked;
            Program._settings.AutoHideOnFocus = autoHideOnFocusCheck.Checked;
            Program._settings.DateFormat = dateFormatCombo.SelectedIndex switch
            {
                1 => "MM/dd hh:mm tt",
                2 => "dd/MM HH:mm",
                _ => "yyyy-MM-dd HH:mm"
            };

            // Directories
            Program._settings.DefaultWorkDir = workDirBox.Text.Trim();
            Program._settings.WorkspacesDir = wsDirBox.Text.Trim();
            Program._settings.AllowedDirs = dirsList.Items.Cast<string>()
                .Select(SettingsVisuals.StripNotFoundPrefix).ToList();

            // Copilot CLI
            Program._settings.AllowedTools = toolsList.Items.Cast<string>().ToList();
            CopilotConfigService.SaveAllowedUrls(urlsList.Items.Cast<string>().ToList());

            // IDEs
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
            Program._settings.IdeSearchIgnoredDirs = ignoredDirsList.Items.Cast<string>().ToList();

            // Git & GitHub
            Program._settings.IssueBranchPattern = string.IsNullOrWhiteSpace(issueBranchBox.Text)
                ? "issues/{number}-{alias}" : issueBranchBox.Text.Trim();
            Program._settings.PrBranchPattern = string.IsNullOrWhiteSpace(prBranchBox.Text)
                ? "prs/{number}-{alias}" : prBranchBox.Text.Trim();

            // Session Tabs
            Program._settings.SessionTabs = sessionTabsList.Items.Cast<string>().ToList();

            // Spotlight
            Program._settings.ToastMode = toastModeCheck.Checked;
            Program._settings.SpotlightAutoHide = autoHideCheck.Checked;
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

            // GitHub
            Program._settings.TrackActiveSession = trackActiveCheck.Checked;

            // AI
            Program._settings.AiDetection = this.GetCurrentAiDetectionFormState();
            var copilotPathChanged = !string.Equals(
                this._initialCopilotPath,
                Program._settings.AiDetection.CopilotPath,
                StringComparison.OrdinalIgnoreCase);

            // Persist
            Program._settings.Save();
            if (copilotPathChanged)
            {
                this._copilotProbe?.InvalidateCache();
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        };

        var btnAbout = new Button { Text = "About", Width = 90 };
        btnAbout.Click += (s, e) =>
        {
            AboutDialog.Show(this, this._latestUpdate);
        };

        bottomPanel.Controls.Add(btnCancel);
        bottomPanel.Controls.Add(btnSave);
        bottomPanel.Controls.Add(btnAbout);

        this.Controls.Add(split);
        this.Controls.Add(bottomPanel);

        // SplitterDistance must be set after the control is parented and sized
        split.SplitterDistance = 200;
    }

    internal AiDetectionSettings GetCurrentAiDetectionFormState()
    {
        return new AiDetectionSettings
        {
            Enabled = this._aiEnabledCheck.Checked,
            TimeoutSeconds = (int)this._aiTimeoutSecondsBox.Value,
            ConfidenceThreshold = this._aiConfidenceThresholdBox.Value,
            CopilotPath = this._aiCopilotPathBox.Text.Trim(),
            Model = this._aiModelBox.Text.Trim()
        };
    }

    internal void LoadAiDetectionFromSettings(AiDetectionSettings s)
    {
        if (this.InvokeRequired)
        {
            this.Invoke(() => this.LoadAiDetectionFromSettings(s));
            return;
        }

        this._aiEnabledCheck.Checked = s.Enabled;
        this._aiTimeoutSecondsBox.Value = Math.Clamp(s.TimeoutSeconds, 30, 1800);
        this._aiConfidenceThresholdBox.Value = Math.Clamp(s.ConfidenceThreshold, 0.00M, 1.00M);
        this._aiCopilotPathBox.Text = s.CopilotPath ?? "";
        this._initialCopilotPath = this._aiCopilotPathBox.Text;
        this._aiModelBox.Text = s.Model ?? "";
        this.ValidateCopilotPath(showTooltip: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this._validationToolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool ValidateCopilotPath(bool showTooltip)
    {
        if (this.InvokeRequired)
        {
            return this.Invoke(() => this.ValidateCopilotPath(showTooltip));
        }

        var value = this._aiCopilotPathBox.Text.Trim();
        var invalid = value.Length > 0 && !File.Exists(value);
        this._aiCopilotPathError.Visible = invalid;
        this._aiCopilotPathBorder.BackColor = invalid
            ? Color.Red
            : Application.IsDarkModeEnabled ? Color.FromArgb(80, 80, 80) : SystemColors.ControlDark;
        this._validationToolTip.SetToolTip(this._aiCopilotPathBox, invalid ? "File not found" : "");
        this._validationToolTip.SetToolTip(this._aiCopilotPathBorder, invalid ? "File not found" : "");
        if (invalid && showTooltip)
        {
            this._validationToolTip.Show("File not found", this._aiCopilotPathBorder, 0, this._aiCopilotPathBorder.Height, 3000);
        }

        return !invalid;
    }

    private (Panel Outer, Panel Body) CreateCategoryPanel(string title, string? description = null, bool autoScroll = false, Padding? padding = null)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Visible = false };
        var header = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font(this.Font.FontFamily, 13f, FontStyle.Bold),
            Padding = new Padding(2, 8, 0, 0)
        };
        var body = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = autoScroll,
            Padding = padding ?? Padding.Empty
        };
        // body first (Dock.Fill), header last (Dock.Top) — last added docks first
        panel.Controls.Add(body);
        if (description != null)
        {
            var info = new Label
            {
                Text = $"ℹ️ {description}",
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(2, 4, 0, 0),
                ForeColor = Application.IsDarkModeEnabled ? Color.FromArgb(100, 149, 237) : SystemColors.GrayText
            };
            panel.Controls.Add(info);
        }
        panel.Controls.Add(header);
        return (panel, body);
    }
}
