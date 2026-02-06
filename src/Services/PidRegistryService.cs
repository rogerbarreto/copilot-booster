using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CopilotApp.Services;

/// <summary>
/// Manages process ID registration for tracking running Copilot application instances.
/// </summary>
internal class PidRegistryService
{
    private readonly string _copilotDir;
    private readonly string _pidRegistryFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="PidRegistryService"/> class.
    /// </summary>
    /// <param name="copilotDir">Path to the Copilot data directory.</param>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file.</param>
    internal PidRegistryService(string copilotDir, string pidRegistryFile)
    {
        this._copilotDir = copilotDir;
        this._pidRegistryFile = pidRegistryFile;
    }

    /// <summary>
    /// Registers the specified process ID in the configured PID registry.
    /// </summary>
    /// <param name="pid">The process ID to register.</param>
    internal void RegisterPid(int pid) => RegisterPid(pid, this._copilotDir, this._pidRegistryFile);

    /// <summary>
    /// Removes the specified process ID from the configured PID registry.
    /// </summary>
    /// <param name="pid">The process ID to unregister.</param>
    internal void UnregisterPid(int pid) => UnregisterPid(pid, this._pidRegistryFile);

    /// <summary>
    /// Associates a session ID and copilot process ID with the specified launcher process ID.
    /// </summary>
    /// <param name="pid">The launcher process ID to update.</param>
    /// <param name="sessionId">The session ID to associate with the process.</param>
    /// <param name="copilotPid">The copilot CLI process ID.</param>
    /// <param name="windowHandle">The terminal window handle for focus tracking.</param>
    internal void UpdatePidSessionId(int pid, string sessionId, int copilotPid = 0, long windowHandle = 0) => UpdatePidSessionId(pid, sessionId, this._pidRegistryFile, copilotPid, windowHandle);

    /// <summary>
    /// Registers a process ID in the PID registry file, creating the directory and file if needed.
    /// </summary>
    /// <param name="pid">The process ID to register.</param>
    /// <param name="copilotDir">Path to the Copilot data directory.</param>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file.</param>
    internal static void RegisterPid(int pid, string copilotDir, string pidRegistryFile)
    {
        try
        {
            if (!Directory.Exists(copilotDir))
            {
                Directory.CreateDirectory(copilotDir);
            }

            Dictionary<string, object> registry = [];
            if (File.Exists(pidRegistryFile))
            {
                try
                {
                    registry = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(pidRegistryFile)) ?? [];
                }
                catch { }
            }
            registry[pid.ToString()] = new { started = System.DateTime.Now.ToString("o"), sessionId = (string?)null };
            File.WriteAllText(pidRegistryFile, JsonSerializer.Serialize(registry));
        }
        catch { }
    }

    /// <summary>
    /// Updates the session ID and copilot process ID associated with a process ID in the PID registry file.
    /// </summary>
    /// <param name="pid">The launcher process ID to update.</param>
    /// <param name="sessionId">The session ID to associate with the process.</param>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file.</param>
    /// <param name="copilotPid">The copilot CLI process ID.</param>
    /// <param name="windowHandle">The terminal window handle for focus tracking.</param>
    internal static void UpdatePidSessionId(int pid, string sessionId, string pidRegistryFile, int copilotPid = 0, long windowHandle = 0)
    {
        try
        {
            if (!File.Exists(pidRegistryFile))
            {
                return;
            }

            var json = File.ReadAllText(pidRegistryFile);
            var registry = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

            registry[pid.ToString()] = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(new { started = System.DateTime.Now.ToString("o"), sessionId, copilotPid, windowHandle }));

            File.WriteAllText(pidRegistryFile, JsonSerializer.Serialize(registry));
        }
        catch { }
    }

    /// <summary>
    /// Removes a process ID from the PID registry file.
    /// </summary>
    /// <param name="pid">The process ID to remove.</param>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file.</param>
    internal static void UnregisterPid(int pid, string pidRegistryFile)
    {
        try
        {
            if (!File.Exists(pidRegistryFile))
            {
                return;
            }

            var registry = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(pidRegistryFile)) ?? [];
            registry.Remove(pid.ToString());
            File.WriteAllText(pidRegistryFile, JsonSerializer.Serialize(registry));
        }
        catch { }
    }
}
