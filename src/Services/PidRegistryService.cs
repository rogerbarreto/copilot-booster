using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CopilotApp.Services;

class PidRegistryService
{
    private readonly string _copilotDir;
    private readonly string _pidRegistryFile;

    internal PidRegistryService(string copilotDir, string pidRegistryFile)
    {
        _copilotDir = copilotDir;
        _pidRegistryFile = pidRegistryFile;
    }

    internal void RegisterPid(int pid) => RegisterPid(pid, _copilotDir, _pidRegistryFile);
    internal void UnregisterPid(int pid) => UnregisterPid(pid, _pidRegistryFile);
    internal void UpdatePidSessionId(int pid, string sessionId) => UpdatePidSessionId(pid, sessionId, _pidRegistryFile);

    internal static void RegisterPid(int pid, string copilotDir, string pidRegistryFile)
    {
        try
        {
            if (!Directory.Exists(copilotDir))
                Directory.CreateDirectory(copilotDir);

            Dictionary<string, object> registry = new();
            if (File.Exists(pidRegistryFile))
            {
                try { registry = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(pidRegistryFile)) ?? new(); }
                catch { }
            }
            registry[pid.ToString()] = new { started = System.DateTime.Now.ToString("o"), sessionId = (string?)null };
            File.WriteAllText(pidRegistryFile, JsonSerializer.Serialize(registry));
        }
        catch { }
    }

    internal static void UpdatePidSessionId(int pid, string sessionId, string pidRegistryFile)
    {
        try
        {
            if (!File.Exists(pidRegistryFile)) return;
            var json = File.ReadAllText(pidRegistryFile);
            var registry = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();

            registry[pid.ToString()] = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(new { started = System.DateTime.Now.ToString("o"), sessionId }));

            File.WriteAllText(pidRegistryFile, JsonSerializer.Serialize(registry));
        }
        catch { }
    }

    internal static void UnregisterPid(int pid, string pidRegistryFile)
    {
        try
        {
            if (!File.Exists(pidRegistryFile)) return;
            var registry = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(pidRegistryFile)) ?? new();
            registry.Remove(pid.ToString());
            File.WriteAllText(pidRegistryFile, JsonSerializer.Serialize(registry));
        }
        catch { }
    }
}
