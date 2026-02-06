using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Taskbar;

namespace CopilotApp.Services;

/// <summary>
/// Manages Windows taskbar jump list entries for Copilot sessions.
/// </summary>
internal class JumpListService
{
    /// <summary>
    /// Acquires a named mutex lock and updates the jump list, recording the update timestamp.
    /// </summary>
    /// <param name="updateLockName">Name of the mutex used to synchronize updates.</param>
    /// <param name="lastUpdateFile">Path to the file storing the last update timestamp.</param>
    /// <param name="launcherExePath">Path to the launcher executable.</param>
    /// <param name="copilotExePath">Path to the Copilot executable for icon references.</param>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file.</param>
    /// <param name="sessionStateDir">Path to the directory containing session state.</param>
    /// <param name="logFile">Path to the log file.</param>
    /// <param name="hiddenForm">The hidden form used for UI thread invocation, or <c>null</c>.</param>
    [ExcludeFromCodeCoverage]
    internal static void TryUpdateJumpListWithLock(string updateLockName, string lastUpdateFile, string launcherExePath, string copilotExePath, string pidRegistryFile, string sessionStateDir, string logFile, Form? hiddenForm)
    {
        try
        {
            using var updateLock = new Mutex(false, updateLockName);
            if (updateLock.WaitOne(TimeSpan.FromSeconds(5)))
            {
                try
                {
                    File.WriteAllText(lastUpdateFile, DateTime.UtcNow.ToString("o"));
                    UpdateJumpList(launcherExePath, copilotExePath, pidRegistryFile, sessionStateDir, logFile, hiddenForm);
                }
                finally
                {
                    updateLock.ReleaseMutex();
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"TryUpdateJumpListWithLock error: {ex.Message}", logFile);
        }
    }

    /// <summary>
    /// Determines whether a background jump list update should be performed based on the elapsed time.
    /// </summary>
    /// <param name="minInterval">Minimum interval between updates.</param>
    /// <param name="lastUpdateFile">Path to the file storing the last update timestamp.</param>
    /// <returns><c>true</c> if the minimum interval has elapsed since the last update; otherwise, <c>false</c>.</returns>
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
    /// <param name="ct">Cancellation token to stop the loop.</param>
    /// <param name="updateLockName">Name of the mutex used to synchronize updates.</param>
    /// <param name="lastUpdateFile">Path to the file storing the last update timestamp.</param>
    /// <param name="launcherExePath">Path to the launcher executable.</param>
    /// <param name="copilotExePath">Path to the Copilot executable for icon references.</param>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file.</param>
    /// <param name="sessionStateDir">Path to the directory containing session state.</param>
    /// <param name="logFile">Path to the log file.</param>
    /// <param name="hiddenForm">The hidden form used for UI thread invocation, or <c>null</c>.</param>
    [ExcludeFromCodeCoverage]
    internal static void UpdaterLoop(CancellationToken ct, string updateLockName, string lastUpdateFile, string launcherExePath, string copilotExePath, string pidRegistryFile, string sessionStateDir, string logFile, Form? hiddenForm)
    {
        while (!ct.IsCancellationRequested)
        {
            if (ShouldBackgroundUpdate(TimeSpan.FromMinutes(1), lastUpdateFile))
            {
                hiddenForm?.Invoke(() => TryUpdateJumpListWithLock(updateLockName, lastUpdateFile, launcherExePath, copilotExePath, pidRegistryFile, sessionStateDir, logFile, hiddenForm));
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
    /// <param name="launcherExePath">Path to the launcher executable.</param>
    /// <param name="copilotExePath">Path to the Copilot executable for icon references.</param>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file.</param>
    /// <param name="sessionStateDir">Path to the directory containing session state.</param>
    /// <param name="logFile">Path to the log file.</param>
    /// <param name="hiddenForm">The hidden form used for UI thread invocation, or <c>null</c>.</param>
    [ExcludeFromCodeCoverage]
    internal static void UpdateJumpList(string launcherExePath, string copilotExePath, string pidRegistryFile, string sessionStateDir, string logFile, Form? hiddenForm)
    {
        try
        {
            var activeSessions = SessionService.GetActiveSessions(pidRegistryFile, sessionStateDir);

            if (hiddenForm == null || !hiddenForm.IsHandleCreated)
            {
                LogService.Log("Hidden form not ready", logFile);
                return;
            }

            var jumpList = JumpList.CreateJumpList();
            jumpList.KnownCategoryToDisplay = JumpListKnownCategoryType.Neither;
            jumpList.ClearAllUserTasks();

            var newSessionTask = new JumpListLink(launcherExePath, "New Copilot Session")
            {
                IconReference = new IconReference(copilotExePath, 0)
            };

            var openExistingTask = new JumpListLink(launcherExePath, "Existing Sessions")
            {
                Arguments = "--open-existing",
                IconReference = new IconReference(copilotExePath, 0)
            };

            var settingsTask = new JumpListLink(launcherExePath, "Settings")
            {
                Arguments = "--settings",
                IconReference = new IconReference(copilotExePath, 0)
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
                        IconReference = new IconReference(copilotExePath, 0),
                        WorkingDirectory = session.Cwd
                    };
                    category.AddJumpListItems(link);
                }
                jumpList.AddCustomCategories(category);
            }

            jumpList.Refresh();
            LogService.Log($"Jump list updated: {activeSessions.Count} sessions", logFile);
        }
        catch (Exception ex)
        {
            LogService.Log($"UpdateJumpList error: {ex.Message}", logFile);
        }
    }
}
