using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace CopilotBooster.Services;

/// <summary>
/// Checks for application updates via the GitHub Releases API.
/// </summary>
internal sealed class UpdateService
{
    private const string ReleasesUrl = "https://api.github.com/repos/rogerbarreto/copilot-booster/releases/latest";

    private static readonly HttpClient s_httpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "CopilotBooster" },
            { "Accept", "application/vnd.github+json" }
        },
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Checks GitHub Releases for a newer version.
    /// </summary>
    /// <returns>Update info if a newer version is available; otherwise null.</returns>
    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var json = await s_httpClient.GetStringAsync(ReleasesUrl).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(tagName))
            {
                return null;
            }

            var versionString = tagName.TrimStart('v');
            if (!Version.TryParse(versionString, out var latestVersion))
            {
                return null;
            }

            if (latestVersion <= CurrentVersion)
            {
                return null;
            }

            string? installerUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name != null && name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        installerUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            return new UpdateInfo(latestVersion, tagName, installerUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the installer to a temp file and launches it.
    /// </summary>
    public static async Task DownloadAndLaunchInstallerAsync(string downloadUrl)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CopilotBooster-Setup.exe");

        using (var response = await s_httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs).ConfigureAwait(false);
        }

        Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
    }
}

/// <summary>
/// Information about an available update.
/// </summary>
internal sealed class UpdateInfo
{
    public Version Version { get; }
    public string TagName { get; }
    public string? InstallerUrl { get; }

    public UpdateInfo(Version version, string tagName, string? installerUrl)
    {
        this.Version = version;
        this.TagName = tagName;
        this.InstallerUrl = installerUrl;
    }
}
