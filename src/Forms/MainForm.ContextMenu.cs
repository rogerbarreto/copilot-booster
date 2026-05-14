using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopilotBooster.Models;
using CopilotBooster.Services;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Forms;

/// <summary>
/// Context menu event handler wiring for MainForm.
/// Separated to keep MainForm focused on core orchestration.
/// </summary>
internal partial class MainForm
{
    private void WireContextMenuEvents()
    {
        this._sessionsVisuals.OnOpenSession += (sid) =>
        {
            this.SelectedSessionId = sid;
            this.LaunchSession();
        };

        this._sessionsVisuals.OnOpenSessionById += (sid) =>
        {
            this.SelectedSessionId = sid;
            this.LaunchSession();
        };

        this._sessionsVisuals.OnEditSession += (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session == null)
            {
                return;
            }

            var editedAlias = SessionEditorVisuals.ShowEditor(session.Id, session.Alias, session.Summary, session.Cwd);
            if (editedAlias != null)
            {
                SessionAliasService.SetAlias(Program.SessionAliasFile, sid, editedAlias);

                // Update the cached session and grid row in-place to avoid a full list refresh
                // which resets the tab view. The natural background refresh will pick up the changes.
                session.Alias = editedAlias;

                var gridName = !string.IsNullOrEmpty(editedAlias) ? editedAlias : session.Summary;
                foreach (DataGridViewRow row in this._sessionsVisuals.SessionGrid.Rows)
                {
                    if ((row.Tag as string) == sid)
                    {
                        row.Cells["Session"].Value = session.IsPinned ? $"\U0001F4CC {gridName}" : gridName;
                        break;
                    }
                }
            }
        };

        this._sessionsVisuals.OnOpenAsNewSession += async (selectedSessionId) =>
        {
            var selectedCwd = this._interactionManager.GetSessionCwd(selectedSessionId);
            selectedCwd = this.ValidateCwdOrPrompt(selectedSessionId, selectedCwd);

            if (!string.IsNullOrEmpty(selectedCwd))
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

                // Handle branch/PR checkout in the CWD before creating the session
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
                var sourceDir = Path.Combine(Program.SessionStateDir, selectedSessionId);
                var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(selectedCwd, sessionName, sourceDir).ConfigureAwait(true);
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
                    await this.RefreshGridAsync().ConfigureAwait(true);
                }
                else
                {
                    MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        };

        this._sessionsVisuals.OnOpenAsNewSessionWorkspace += async (selectedSessionId) =>
        {
            var selectedCwd = this._interactionManager.GetSessionCwd(selectedSessionId);
            selectedCwd = this.ValidateCwdOrPrompt(selectedSessionId, selectedCwd);

            if (!string.IsNullOrEmpty(selectedCwd))
            {
                var gitRoot = SessionService.FindGitRoot(selectedCwd);
                if (gitRoot != null)
                {
                    var wsResult = WorkspaceCreatorVisuals.ShowWorkspaceCreator(gitRoot, this._githubApi);
                    if (wsResult != null)
                    {
                        var sourceDir = Path.Combine(Program.SessionStateDir, selectedSessionId);
                        var sid = await CopilotSessionCreatorService.CreateSessionAsync(wsResult.Value.WorktreePath, wsResult.Value.SessionName, sourceDir).ConfigureAwait(true);
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
                            await this.RefreshGridAsync().ConfigureAwait(true);
                        }
                        else
                        {
                            MessageBox.Show("Failed to create session. Check that Copilot CLI is installed and authenticated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
        };

        this._sessionsVisuals.OnOpenTerminal += (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session == null || string.IsNullOrEmpty(session.Cwd))
            {
                return;
            }

            var validCwd = this.ValidateCwdOrPrompt(sid, session.Cwd);
            if (validCwd == null)
            {
                return;
            }

            var proc = this._interactionManager.OpenTerminal(validCwd, sid);
            if (proc != null)
            {
                this.RefreshActiveStatusAsync();
            }
        };

        this._sessionsVisuals.OnOpenInIde += (sid, capturedIde, useRepoRoot) =>
        {
            if (this._activeTracker.TryFocusExistingIde(sid, capturedIde.Description))
            {
                return;
            }

            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session == null || string.IsNullOrEmpty(session.Cwd))
            {
                return;
            }

            var validCwd = this.ValidateCwdOrPrompt(sid, session.Cwd);
            if (validCwd == null)
            {
                return;
            }

            var targetPath = useRepoRoot ? SessionService.FindGitRoot(validCwd) : validCwd;
            if (targetPath == null)
            {
                return;
            }

            var pid = SessionInteractionManager.OpenInIde(capturedIde.Path, targetPath);
            if (pid != null)
            {
                this._activeTracker.TrackProcess(sid, new ActiveProcess(capturedIde.Description, pid.Value, targetPath));
                this._processExitTracker?.Watch(pid.Value);
                this.RefreshActiveStatusAsync();
            }
        };

        this._sessionsVisuals.OnOpenInIdeFile += (sid, capturedIde, filePath) =>
        {
            var pid = SessionInteractionManager.OpenInIde(capturedIde.Path, filePath);
            if (pid != null)
            {
                var dir = Path.GetDirectoryName(filePath) ?? filePath;
                this._activeTracker.TrackProcess(sid, new ActiveProcess(capturedIde.Description, pid.Value, dir));
                this._processExitTracker?.Watch(pid.Value);
                this.RefreshActiveStatusAsync();
            }
        };

        this._sessionsVisuals.GetSessionPaths = (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session == null || string.IsNullOrEmpty(session.Cwd))
            {
                return (null, null);
            }

            return (session.Cwd, SessionService.FindGitRoot(session.Cwd));
        };

        this._sessionsVisuals.OnAddPr += (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            var (item, owner, repo) = AddPrForm.Show(sid, session?.Cwd, this._githubApi);
            if (item != null && owner != null && repo != null)
            {
                GitHubTrackingService.AddItem(sid, owner, repo, item);
                this._githubPoller?.PollSessionNow(sid);
                this.AiDetectionService.Reset(sid);
                this.RequestRefresh(sessionId: sid, trackingChanged: true);
                this._toast.Show($"✅ PR #{item.Number} added to session");
            }
        };

        this._sessionsVisuals.OnAddIssue += (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            var (item, owner, repo) = AddIssueForm.Show(sid, session?.Cwd, this._githubApi);
            if (item != null && owner != null && repo != null)
            {
                GitHubTrackingService.AddItem(sid, owner, repo, item);
                this._githubPoller?.PollSessionNow(sid);
                this.AiDetectionService.Reset(sid);
                this.RequestRefresh(sessionId: sid, trackingChanged: true);
                this._toast.Show($"✅ Issue #{item.Number} added to session");
            }
        };

        this._sessionsVisuals.OnAiAutoDetect += (sid) =>
        {
            _ = this.AiDetectionService.StartDetectionAsync(sid);
        };

        this._sessionsVisuals.OnShowCiJobs += (sid, prNumber) =>
        {
            var data = GitHubTrackingService.Load(sid);
            if (data == null)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                var prDoc = await this._githubApi.GetPullRequestAsync(data.Owner, data.Repo, prNumber);
                string headSha = "";
                if (prDoc != null)
                {
                    using (prDoc)
                    {
                        if (prDoc.RootElement.TryGetProperty("head", out var head)
                            && head.TryGetProperty("sha", out var sha))
                        {
                            headSha = sha.GetString() ?? "";
                        }
                    }
                }

                if (!string.IsNullOrEmpty(headSha))
                {
                    this.BeginInvoke(async () =>
                    {
                        await CiInformationForm.ShowAsync(
                            data.Owner, data.Repo, prNumber, headSha,
                            sid, this._githubApi, this._activeTracker);
                    });
                }
            });
        };

        this._sessionsVisuals.OnOpenGitHubItem += (sid, type, number) =>
        {
            var data = GitHubTrackingService.Load(sid);
            if (data == null)
            {
                return;
            }

            var url = type == "pr"
                ? GitHubLinkService.GetPrUrl(data.Owner, data.Repo, number)
                : GitHubLinkService.GetIssueUrl(data.Owner, data.Repo, number);
            GitHubLinkService.OpenUrl(url);
            GitHubTrackingService.MarkSeen(sid, type, number);
            this.RequestRefresh(sessionId: sid, trackingChanged: true);
        };

        this._sessionsVisuals.OnRemoveGitHubItem += (sid, type, number) =>
        {
            GitHubTrackingService.RemoveItem(sid, type, number);
            this.RequestRefresh(sessionId: sid, trackingChanged: true);
            var prefix = type == "pr" ? "PR" : "Issue";
            this._toast.Show($"✅ {prefix} #{number} removed from session");
        };

        this._sessionsVisuals.OnOpenEdge += async (sid) =>
        {
            if (this._activeTracker.TryGetEdge(sid, out var existing) && existing.IsOpen)
            {
                existing.Focus();
                return;
            }

            var session = this._cachedSessions.Find(x => x.Id == sid);
            var sessionName = !string.IsNullOrEmpty(session?.Alias) ? session.Alias : session?.Summary;

            var workspace = SessionInteractionManager.CreateEdgeWorkspace(sid);
            workspace.WindowClosed += () =>
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
            this._activeTracker.TrackEdge(sid, workspace);

            var savedTabs = EdgeTabPersistenceService.LoadTabs(sid);
            await workspace.OpenAsync(sessionName).ConfigureAwait(true);

            // Restore previously saved tabs
            if (savedTabs.Count > 0)
            {
                workspace.RestoreTabs(savedTabs);
            }

            this.RefreshActiveStatusAsync();
        };

        this._sessionsVisuals.OnSaveEdgeTabs += (sid) =>
        {
            if (!this._activeTracker.TryGetEdge(sid, out var ws) || !ws.IsOpen)
            {
                return;
            }

            _ = Task.Factory.StartNew(() =>
            {
                var urls = ws.GetTabUrls();
                if (urls.Count > 0)
                {
                    EdgeTabPersistenceService.SaveTabs(sid, urls);
                    this.BeginInvoke(() =>
                    {
                        this._contextWatcher?.UpdateTabCount(sid, urls.Count);
                        this._toast.Show($"✅ Edge state saved — {urls.Count} tab(s) stored");
                    });
                }
                else if (EdgeTabPersistenceService.HasSavedTabs(sid))
                {
                    EdgeTabPersistenceService.SaveTabs(sid, []);
                    this.BeginInvoke(() =>
                    {
                        this._contextWatcher?.UpdateTabCount(sid, 0);
                        this._toast.Show("✅ Edge state saved — previous tabs cleared");
                    });
                }
                else
                {
                    this.BeginInvoke(() => this._toast.Show("No tabs to save — only the session anchor tab was found"));
                }
            }, CancellationToken.None, TaskCreationOptions.None, StaTaskScheduler.Instance);
        };

        this._sessionsVisuals.IsEdgeOpen = (sid) =>
            this._activeTracker.TryGetEdge(sid, out var ws) && ws.IsOpen;

        this._sessionsVisuals.OnOpenTeams += async (sid) =>
        {
            if (this._activeTracker.TryGetTeams(sid, out var existing) && existing.IsOpen)
            {
                existing.Focus();
                return;
            }

            var teamsWindow = new TeamsWindowService();
            teamsWindow.WindowClosed += () =>
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(() =>
                    {
                        this._activeTracker.RemoveTeams(sid);
                        this.RefreshActiveStatusAsync();
                    });
                }
                else
                {
                    this._activeTracker.RemoveTeams(sid);
                    this.RefreshActiveStatusAsync();
                }
            };
            this._activeTracker.TrackTeams(sid, teamsWindow);
            await teamsWindow.OpenAsync().ConfigureAwait(true);
            this.RefreshActiveStatusAsync();
        };

        this._sessionsVisuals.IsTeamsOpen = (sid) =>
            this._activeTracker.TryGetTeams(sid, out var tw) && tw.IsOpen;

        this._sessionsVisuals.GetGitRootInfo = (sessionId) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sessionId);
            if (session != null && !string.IsNullOrEmpty(session.Cwd))
            {
                var repoRoot = SessionService.FindGitRoot(session.Cwd);
                var hasGitRoot = repoRoot != null;
                var isSubfolder = hasGitRoot && !string.Equals(repoRoot, session.Cwd, StringComparison.OrdinalIgnoreCase);
                return (hasGitRoot, isSubfolder);
            }
            return (false, false);
        };

        this._sessionsVisuals.OnDeleteSessions += (sids) =>
        {
            var sessionNames = sids.Select(sid =>
            {
                var session = this._cachedSessions.Find(x => x.Id == sid);
                return !string.IsNullOrEmpty(session?.Alias) ? session.Alias : session?.Summary ?? sid;
            }).ToList();

            var message = sids.Count == 1
                ? $"Delete session \"{sessionNames[0]}\"?\n\n"
                : $"Delete {sids.Count} sessions?\n\n";
            message += "This will only remove the session(s) from Copilot — your code and files are not affected.\n" +
                "This action can be reversed.";

            var result = MessageBox.Show(
                message,
                sids.Count == 1 ? "Delete Session" : "Delete Sessions",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result == DialogResult.Yes)
            {
                foreach (var sid in sids)
                {
                    if (this._interactionManager.DeleteSession(sid))
                    {
                        this._cachedSessions.RemoveAll(x => x.Id == sid);
                        this._sessionsVisuals.GridVisuals.RemoveRowBySessionId(sid);
                    }
                }

                this.UpdateTabCounts();
            }
        };

        this._sessionsVisuals.OnOpenCwdExplorer += (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session != null && !string.IsNullOrEmpty(session.Cwd))
            {
                var validCwd = this.ValidateCwdOrPrompt(sid, session.Cwd);
                if (validCwd == null)
                {
                    return;
                }

                SessionInteractionManager.OpenExplorer(validCwd);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500).ConfigureAwait(false);
                    this._activeTracker.TrackExplorerWindow(sid, validCwd, "Explorer (CWD)");
                    this.BeginInvoke(this.RefreshActiveStatusAsync);
                });
            }
        };

        this._sessionsVisuals.OnOpenSessionFolder += (sid) =>
        {
            var sessionDir = Path.Combine(Program.SessionStateDir, sid);
            if (Directory.Exists(sessionDir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", sessionDir) { UseShellExecute = true });
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500).ConfigureAwait(false);
                    this._activeTracker.TrackExplorerWindow(sid, sessionDir, "Files");
                    this.BeginInvoke(this.RefreshActiveStatusAsync);
                });
            }
        };

        this._sessionsVisuals.OnOpenFile += (fullPath) =>
        {
            if (File.Exists(fullPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fullPath) { UseShellExecute = true });
            }
        };

        this._sessionsVisuals.GetSessionFiles = (sid) =>
        {
            return GetSessionFiles(Program.SessionStateDir, sid);
        };

        // Wire context column callbacks on the grid visuals
        this._sessionsVisuals.GridVisuals.GetSessionFileCount = (sid) =>
        {
            return GetSessionFiles(Program.SessionStateDir, sid).Count;
        };

        this._sessionsVisuals.GridVisuals.GetContextCounts = (sid) =>
        {
            return this._contextWatcher?.GetCounts(sid) ?? (0, 0);
        };

        this._sessionsVisuals.GridVisuals.GetGitHubValue = (sid) =>
        {
            var data = GitHubTrackingService.Load(sid);
            if (data == null || data.Items.Count == 0)
            {
                return "";
            }

            // Build a compact display: "PR#42 I#15"
            var parts = new System.Collections.Generic.List<string>();
            foreach (var item in data.Items)
            {
                var prefix = item.IsPr ? "PR" : "I";
                parts.Add($"{prefix}#{item.Number}");
            }

            return string.Join(" ", parts);
        };

        this._sessionsVisuals.GridVisuals.OnGitHubColumnClick += (sid, clickPos, cellBounds) =>
        {
            var data = GitHubTrackingService.Load(sid);
            if (data == null || data.Items.Count == 0)
            {
                return;
            }

            // Determine which icon was clicked based on X position
            // clickPos is cell-relative (from CellMouseClick event)
            const int IconSize = 16;
            const int Spacing = 4;
            int totalWidth = (data.Items.Count * IconSize) + ((data.Items.Count - 1) * Spacing);
            int startX = (cellBounds.Width - totalWidth) / 2;
            int relativeX = clickPos.X - startX;

            int index = relativeX / (IconSize + Spacing);
            if (index < 0 || index >= data.Items.Count)
            {
                return;
            }

            var item = data.Items[index];

            if (item.IsPr && (item.Checks == "failure" || item.Checks == "success"))
            {
                // Open CI Information Form
                var headSha = ""; // Need to fetch from API
                _ = Task.Run(async () =>
                {
                    var prDoc = await this._githubApi.GetPullRequestAsync(data.Owner, data.Repo, item.Number);
                    if (prDoc != null)
                    {
                        using (prDoc)
                        {
                            if (prDoc.RootElement.TryGetProperty("head", out var head)
                                && head.TryGetProperty("sha", out var sha))
                            {
                                headSha = sha.GetString() ?? "";
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(headSha))
                    {
                        this.BeginInvoke(async () =>
                        {
                            await CiInformationForm.ShowAsync(
                                data.Owner, data.Repo, item.Number, headSha,
                                sid, this._githubApi, this._activeTracker);
                        });
                    }
                });
            }
            else
            {
                // Open PR/Issue in browser
                var url = item.IsPr
                    ? GitHubLinkService.GetPrUrl(data.Owner, data.Repo, item.Number)
                    : GitHubLinkService.GetIssueUrl(data.Owner, data.Repo, item.Number);
                GitHubLinkService.OpenUrl(url);

                // Mark as seen (clear red dot)
                GitHubTrackingService.MarkSeen(sid, item.Type, item.Number);
                this.RequestRefresh(sessionId: sid, trackingChanged: true);
            }
        };

        this._sessionsVisuals.GridVisuals.GetSessionFiles = (sid) =>
        {
            return GetSessionFiles(Program.SessionStateDir, sid);
        };

        this._sessionsVisuals.GridVisuals.OnOpenFile += (fullPath) =>
        {
            if (File.Exists(fullPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fullPath) { UseShellExecute = true });
            }
        };

        this._sessionsVisuals.GridVisuals.OnContextEdgeClicked += async (sid) =>
        {
            if (this._activeTracker.TryGetEdge(sid, out var existing) && existing.IsOpen)
            {
                existing.Focus();
                return;
            }

            var session = this._cachedSessions.Find(x => x.Id == sid);
            var sessionName = !string.IsNullOrEmpty(session?.Alias) ? session.Alias : session?.Summary;

            var workspace = SessionInteractionManager.CreateEdgeWorkspace(sid);
            workspace.WindowClosed += () =>
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
            this._activeTracker.TrackEdge(sid, workspace);

            var savedTabs = EdgeTabPersistenceService.LoadTabs(sid);
            await workspace.OpenAsync(sessionName).ConfigureAwait(true);

            if (savedTabs.Count > 0)
            {
                workspace.RestoreTabs(savedTabs);
            }

            this.RefreshActiveStatusAsync();
        };

        this._sessionsVisuals.HasPlanFile = (sid) =>
        {
            return SessionInteractionManager.HasPlanFile(Program.SessionStateDir, sid);
        };

        this._sessionsVisuals.OnMoveToTab += (sid, tabName) =>
        {
            SessionArchiveService.SetTab(Program.SessionStateFile, sid, tabName);
            var session = this._cachedSessions.Find(x => x.Id == sid);
            session?.Tab = tabName;

            this._sessionsVisuals.GridVisuals.RemoveRowBySessionId(sid);
            this.UpdateTabCounts();
        };

        this._sessionsVisuals.OnPinSession += (sid) =>
        {
            SessionArchiveService.SetPinned(Program.SessionStateFile, sid, true);
            var session = this._cachedSessions.Find(x => x.Id == sid);
            session?.IsPinned = true;

            this.PopulateGridWithFilter(this._lastSnapshot);
        };

        this._sessionsVisuals.OnUnpinSession += (sid) =>
        {
            SessionArchiveService.SetPinned(Program.SessionStateFile, sid, false);
            var session = this._cachedSessions.Find(x => x.Id == sid);
            session?.IsPinned = false;

            this.PopulateGridWithFilter(this._lastSnapshot);
        };

        this._sessionsVisuals.IsSessionPinned = (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            return session?.IsPinned ?? false;
        };

        this._sessionsVisuals.OnCopySessionId += (sid) =>
        {
            this._toast.Show($"✅ Session ID copied: {sid}");
        };
    }
}
