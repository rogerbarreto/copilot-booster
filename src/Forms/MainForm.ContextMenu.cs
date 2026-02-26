using System;
using System.Collections.Generic;
using System.IO;
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

        this._sessionsVisuals.OnEditSession += async (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session == null)
            {
                return;
            }

            var edited = SessionEditorVisuals.ShowEditor(session.Id, session.Alias, session.Summary, session.Cwd);
            if (edited != null)
            {
                SessionAliasService.SetAlias(Program.SessionAliasFile, sid, edited.Value.Alias);

                // Update Edge tab title if workspace is open and setting is enabled
                if (Program._settings.UpdateEdgeTabOnRename
                    && this._activeTracker.TryGetEdge(sid, out var edgeWs) && edgeWs.IsOpen)
                {
                    var displayName = !string.IsNullOrEmpty(edited.Value.Alias) ? edited.Value.Alias : session.Summary;
                    edgeWs.UpdateSessionName(displayName);
                }

                var sessionDir = Path.Combine(Program.SessionStateDir, sid);
                SessionService.UpdateSessionCwd(sessionDir, edited.Value.Cwd);

                // Always reload and refresh after edit
                this._cachedSessions = (List<NamedSession>)await Task.Run(() => this._refreshCoordinator.LoadSessions()).ConfigureAwait(true);
                var snapshot = await Task.Run(() => this._refreshCoordinator.RefreshActiveStatus(this._cachedSessions)).ConfigureAwait(true);
                this.PopulateGridWithFilter(snapshot);
            }
        };

        this._sessionsVisuals.OnOpenAsNewSession += async (selectedSessionId) =>
        {
            var selectedCwd = this._interactionManager.GetSessionCwd(selectedSessionId);

            if (!string.IsNullOrEmpty(selectedCwd))
            {
                var promptResult = NewSessionNameVisuals.ShowNamePrompt(selectedCwd);
                if (promptResult == null)
                {
                    return;
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
                                GitService.FetchAndCheckoutPr(gitRoot, promptResult.Remote, promptResult.Platform.Value, promptResult.PrNumber.Value, $"pr-{promptResult.PrNumber.Value}"),
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

            if (!string.IsNullOrEmpty(selectedCwd))
            {
                var gitRoot = SessionService.FindGitRoot(selectedCwd);
                if (gitRoot != null)
                {
                    var wsResult = WorkspaceCreatorVisuals.ShowWorkspaceCreator(gitRoot);
                    if (wsResult != null)
                    {
                        var sourceDir = Path.Combine(Program.SessionStateDir, selectedSessionId);
                        var newSessionId = await CopilotSessionCreatorService.CreateSessionAsync(wsResult.Value.WorktreePath, wsResult.Value.SessionName, sourceDir).ConfigureAwait(true);
                        if (newSessionId != null)
                        {
                            if (!string.IsNullOrWhiteSpace(wsResult.Value.SessionName))
                            {
                                SessionAliasService.SetAlias(Program.SessionAliasFile, newSessionId, wsResult.Value.SessionName);
                            }

                            this._interactionManager.LaunchSession(newSessionId);
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

            var proc = this._interactionManager.OpenTerminal(session.Cwd, sid);
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

            var targetPath = useRepoRoot ? SessionService.FindGitRoot(session.Cwd) : session.Cwd;
            if (targetPath == null)
            {
                return;
            }

            var proc = SessionInteractionManager.OpenInIde(capturedIde.Path, targetPath);
            if (proc != null)
            {
                this._activeTracker.TrackProcess(sid, new ActiveProcess(capturedIde.Description, proc.Id, targetPath));
                this.RefreshActiveStatusAsync();
            }
        };

        this._sessionsVisuals.OnOpenInIdeFile += (sid, capturedIde, filePath) =>
        {
            var proc = SessionInteractionManager.OpenInIde(capturedIde.Path, filePath);
            if (proc != null)
            {
                var dir = Path.GetDirectoryName(filePath) ?? filePath;
                this._activeTracker.TrackProcess(sid, new ActiveProcess(capturedIde.Description, proc.Id, dir));
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
            await workspace.OpenAsync(sessionName, savedTabs.Count > 0).ConfigureAwait(true);

            // Restore previously saved tabs
            if (savedTabs.Count > 0)
            {
                workspace.RestoreTabs(savedTabs);
            }

            // Save initial title hash baseline after tabs have loaded
            _ = Task.Factory.StartNew(() =>
            {
                Thread.Sleep(3000); // Let tabs load their titles
                var hash = workspace.GetTabNameHash();
                if (hash != null)
                {
                    EdgeTabPersistenceService.SaveTabTitleHash(sid, hash);
                    Program.Logger.LogInformation("[Baseline] Saved initial title hash for {Sid}: {Hash}", sid, hash);
                }
            }, CancellationToken.None, TaskCreationOptions.None, StaTaskScheduler.Instance);

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
                    var titleHash = ws.GetTabNameHash();
                    if (titleHash != null)
                    {
                        EdgeTabPersistenceService.SaveTabTitleHash(sid, titleHash);
                    }
                    this.BeginInvoke(() => this._toast.Show($"✅ Edge state saved — {urls.Count} tab(s) stored"));
                }
                else
                {
                    this.BeginInvoke(() => this._toast.Show("No tabs to save — only the session anchor tab was found"));
                }
            }, CancellationToken.None, TaskCreationOptions.None, StaTaskScheduler.Instance);
        };

        this._sessionsVisuals.IsEdgeOpen = (sid) =>
            this._activeTracker.TryGetEdge(sid, out var ws) && ws.IsOpen;

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

        this._sessionsVisuals.OnDeleteSession += (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            var sessionName = !string.IsNullOrEmpty(session?.Alias) ? session.Alias : session?.Summary ?? sid;
            var result = MessageBox.Show(
                $"Delete session \"{sessionName}\"?\n\n" +
                "This will only remove the session from Copilot — your code and files are not affected.\n" +
                "This action can be reversed.",
                "Delete Session",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result == DialogResult.Yes && this._interactionManager.DeleteSession(sid))
            {
                SessionArchiveService.Remove(Program.SessionStateFile, sid);
                this._cachedSessions.RemoveAll(x => x.Id == sid);
                this._sessionsVisuals.GridVisuals.RemoveRowBySessionId(sid);
                this.UpdateTabCounts();
            }
        };

        this._sessionsVisuals.OnOpenCwdExplorer += (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session != null && !string.IsNullOrEmpty(session.Cwd))
            {
                SessionInteractionManager.OpenExplorer(session.Cwd);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500).ConfigureAwait(false);
                    this._activeTracker.TrackExplorerWindow(sid, session.Cwd, "Explorer (CWD)");
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

        this._sessionsVisuals.HasPlanFile = (sid) =>
        {
            return SessionInteractionManager.HasPlanFile(Program.SessionStateDir, sid);
        };

        this._sessionsVisuals.OnMoveToTab += (sid, tabName) =>
        {
            SessionArchiveService.SetTab(Program.SessionStateFile, sid, tabName);
            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session != null)
            {
                session.Tab = tabName;
            }

            this._sessionsVisuals.GridVisuals.RemoveRowBySessionId(sid);
            this.UpdateTabCounts();
        };

        this._sessionsVisuals.OnPinSession += (sid) =>
        {
            SessionArchiveService.SetPinned(Program.SessionStateFile, sid, true);
            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session != null)
            {
                session.IsPinned = true;
            }

            this.PopulateGridWithFilter(this._lastSnapshot);
        };

        this._sessionsVisuals.OnUnpinSession += (sid) =>
        {
            SessionArchiveService.SetPinned(Program.SessionStateFile, sid, false);
            var session = this._cachedSessions.Find(x => x.Id == sid);
            if (session != null)
            {
                session.IsPinned = false;
            }

            this.PopulateGridWithFilter(this._lastSnapshot);
        };

        this._sessionsVisuals.IsSessionPinned = (sid) =>
        {
            var session = this._cachedSessions.Find(x => x.Id == sid);
            return session?.IsPinned ?? false;
        };
    }
}
