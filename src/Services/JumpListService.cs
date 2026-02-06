using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Taskbar;
using Microsoft.WindowsAPICodePack.Shell;

namespace CopilotApp.Services;

internal class JumpListService
{
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
