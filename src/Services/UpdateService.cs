using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CopilotBooster.Services;

/// <summary>
/// Checks for application updates via the GitHub tags page.
/// </summary>
internal sealed partial class UpdateService
{
    private const string TagsUrl = "https://github.com/rogerbarreto/copilot-booster/tags";
    private const string InstallerDownloadUrlTemplate = "https://github.com/rogerbarreto/copilot-booster/releases/download/v{0}/CopilotBooster-Setup.exe";

    [GeneratedRegex(@"/rogerbarreto/copilot-booster/releases/tag/v([0-9\.]+)")]
    private static partial Regex TagRegex();

    private static readonly HttpClient s_httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Checks the GitHub tags page for a newer version.
    /// </summary>
    /// <returns>Update info if a newer version is available; otherwise null.</returns>
    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var html = await s_httpClient.GetStringAsync(TagsUrl).ConfigureAwait(false);
            return ParseUpdate(html, CurrentVersion);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses the GitHub tags HTML and returns update info if a newer version is found.
    /// </summary>
    internal static UpdateInfo? ParseUpdate(string html, Version currentVersion)
    {
        var match = TagRegex().Match(html);

        if (!match.Success)
        {
            return null;
        }

        var versionString = match.Groups[1].Value;
        if (!Version.TryParse(versionString, out var latestVersion))
        {
            return null;
        }

        if (latestVersion <= currentVersion)
        {
            return null;
        }

        var tagName = $"v{versionString}";
        var installerUrl = string.Format(InstallerDownloadUrlTemplate, versionString);

        return new UpdateInfo(latestVersion, tagName, installerUrl);
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
