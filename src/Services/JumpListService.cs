using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Taskbar;

namespace CopilotBooster.Services;

/// <summary>
/// Manages Windows taskbar jump list entries for Copilot sessions.
/// </summary>
internal class JumpListService
{
    /// <summary>
    /// Acquires a named mutex lock and updates the jump list, recording the update timestamp.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal static void TryUpdateJumpListWithLock(string updateLockName, string lastUpdateFile, string launcherExePath, string copilotExePath, string pidRegistryFile, string sessionStateDir, Form? hiddenForm)
    {
        try
        {
            using var updateLock = new Mutex(false, updateLockName);
            if (updateLock.WaitOne(TimeSpan.FromSeconds(5)))
            {
                try
                {
                    File.WriteAllText(lastUpdateFile, DateTime.UtcNow.ToString("o"));
                    UpdateJumpList(launcherExePath, copilotExePath, pidRegistryFile, sessionStateDir, hiddenForm);
                }
                finally
                {
                    updateLock.ReleaseMutex();
                }
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogError("TryUpdateJumpListWithLock error: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Determines whether a background jump list update should be performed based on the elapsed time.
    /// </summary>
    internal static bool ShouldBackgroundUpdate(TimeSpan minInterval, string lastUpdateFile)
    {
        try
        {
            if (!File.Exists(lastUpdateFile))
            {
                return true;
            }

            var lastUpdate = DateTime.Parse(File.ReadAllText(lastUpdateFile).Trim());
            return DateTime.UtcNow - lastUpdate > minInterval;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Continuously checks and updates the jump list at regular intervals until cancellation is requested.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal static void UpdaterLoop(string updateLockName, string lastUpdateFile, string launcherExePath, string copilotExePath, string pidRegistryFile, string sessionStateDir, Form? hiddenForm, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (ShouldBackgroundUpdate(TimeSpan.FromMinutes(1), lastUpdateFile))
            {
                hiddenForm?.Invoke(() => TryUpdateJumpListWithLock(updateLockName, lastUpdateFile, launcherExePath, copilotExePath, pidRegistryFile, sessionStateDir, hiddenForm));
            }

            for (int i = 0; i < 300 && !ct.IsCancellationRequested; i++)
            {
                Thread.Sleep(1000);
            }
        }
    }

    /// <summary>
    /// Rebuilds the Windows taskbar jump list with current active sessions and standard tasks.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal static void UpdateJumpList(string launcherExePath, string copilotExePath, string pidRegistryFile, string sessionStateDir, Form? hiddenForm)
    {
        try
        {
            var activeSessions = SessionService.GetActiveSessions(pidRegistryFile, sessionStateDir);

            if (hiddenForm == null || !hiddenForm.IsHandleCreated)
            {
                Program.Logger.LogWarning("Hidden form not ready");
                return;
            }

            var jumpList = JumpList.CreateJumpList();
            jumpList.KnownCategoryToDisplay = JumpListKnownCategoryType.Neither;
            jumpList.ClearAllUserTasks();

            var newSessionTask = new JumpListLink(launcherExePath, "New Copilot Session")
            {
                Arguments = "--new-session",
                IconReference = new IconReference(launcherExePath, 0)
            };

            var openExistingTask = new JumpListLink(launcherExePath, "Existing Sessions")
            {
                Arguments = "--open-existing",
                IconReference = new IconReference(launcherExePath, 0)
            };

            var settingsTask = new JumpListLink(launcherExePath, "Settings")
            {
                Arguments = "--settings",
                IconReference = new IconReference(launcherExePath, 0)
            };

            jumpList.AddUserTasks(newSessionTask, new JumpListSeparator(), openExistingTask, new JumpListSeparator(), settingsTask);

            if (activeSessions.Count > 0)
            {
                var category = new JumpListCustomCategory("Active Sessions");
                foreach (var session in activeSessions)
                {
                    var link = new JumpListLink(launcherExePath, session.Summary)
                    {
                        Arguments = $"--resume {session.Id}",
                        IconReference = new IconReference(launcherExePath, 0),
                        WorkingDirectory = session.Cwd
                    };
                    category.AddJumpListItems(link);
                }
                jumpList.AddCustomCategories(category);
            }

            jumpList.Refresh();
            Program.Logger.LogInformation("Jump list updated: {Count} sessions", activeSessions.Count);
        }
        catch (Exception ex)
        {
            Program.Logger.LogError("UpdateJumpList error: {Error}", ex.Message);
        }
    }
}
