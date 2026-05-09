using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

internal sealed class CopilotModelsService
{
    private const string ModelsApiUrl = "https://api.githubcopilot.com/models";
    private const string CacheFileName = "models-cache.json";
    private const string AppDataFolderName = "CopilotBooster";
    private const int CacheTtlHours = 24;
    private const int ProcessTimeoutMilliseconds = 15_000;

    private static readonly ReadOnlyCollection<string> s_fallbackModels = Array.AsReadOnly(
    [
        "claude-sonnet-4.6",
        "claude-sonnet-4.5",
        "claude-haiku-4.5",
        "claude-opus-4.7",
        "claude-opus-4.6",
        "claude-opus-4.6-fast",
        "claude-opus-4.5",
        "claude-sonnet-4",
        "gpt-5.5",
        "gpt-5.4",
        "gpt-5.3-codex",
        "gpt-5.2-codex",
        "gpt-5.2",
        "gpt-5.1",
        "gpt-5.4-mini",
        "gpt-5-mini",
        "gpt-4.1"
    ]);

    private static readonly TimeSpan s_cacheTtl = TimeSpan.FromHours(CacheTtlHours);
    private static readonly JsonWriterOptions s_writerOptions = new() { Indented = true };

    private readonly HttpClient _httpClient;
    private readonly Func<DateTime> _clock;
    private readonly Func<string, string?, Task<(int ExitCode, string Stdout, string Stderr)>>? _processRunner;
    private readonly string _cacheFile;

    internal CopilotModelsService(
        HttpMessageHandler? httpHandler = null,
        Func<DateTime>? clock = null,
        Func<string, string?, Task<(int ExitCode, string Stdout, string Stderr)>>? processRunner = null)
    {
        this._httpClient = httpHandler == null ? new HttpClient() : new HttpClient(httpHandler, disposeHandler: false);
        this._clock = clock ?? (() => DateTime.UtcNow);
        this._processRunner = processRunner;
        this._cacheFile = Path.Combine(
            GetLocalApplicationDataPath(),
            AppDataFolderName,
            CacheFileName);
    }

    internal async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var cache = await this.TryReadCacheAsync(cancellationToken).ConfigureAwait(false);
        if (cache != null && this.IsFresh(cache.FetchedAt))
        {
            Program.Logger.LogDebug(
                "CopilotModelsService cache hit fetched_at={FetchedAt} count={Count}",
                cache.FetchedAt,
                cache.Models.Count);
            return cache.Models;
        }

        var fetchedModels = await this.TryFetchModelsAsync(cancellationToken).ConfigureAwait(false);
        if (fetchedModels != null)
        {
            await this.TryWriteCacheAsync(fetchedModels, cancellationToken).ConfigureAwait(false);
            return fetchedModels;
        }

        if (cache != null && cache.Models.Count > 0)
        {
            Program.Logger.LogDebug(
                "CopilotModelsService API fetch failed — using stale cache fetched_at={FetchedAt} count={Count}",
                cache.FetchedAt,
                cache.Models.Count);
            return cache.Models;
        }

        return s_fallbackModels;
    }

    private static ReadOnlyCollection<string> ParseModelsResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Models response is missing data array.");
        }

        var models = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id) ||
                id.StartsWith("text-embedding", StringComparison.Ordinal) ||
                !seen.Add(id))
            {
                continue;
            }

            models.Add(id);
        }

        if (models.Count == 0)
        {
            throw new JsonException("Models response did not contain selectable model ids.");
        }

        return models.AsReadOnly();
    }

    private static string GetClientVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static string GetLocalApplicationDataPath()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        return string.IsNullOrWhiteSpace(localAppData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppData;
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunRealProcessAsync(
        string fileName,
        string? arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = arguments ?? string.Empty
        };

        if (!process.Start())
        {
            return (-1, string.Empty, "Failed to start process");
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var exitTask = process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(ProcessTimeoutMilliseconds);
            var completedTask = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);
            if (completedTask == timeoutTask)
            {
                TryKillProcess(process);

                return (-1, string.Empty, "Process timed out");
            }

            await exitTask.ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return (process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }
    }

    private async Task<IReadOnlyList<string>?> TryFetchModelsAsync(CancellationToken cancellationToken)
    {
        string token;
        try
        {
            var result = await this.RunProcessAsync("gh", "auth token", cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
            {
                Program.Logger.LogDebug("CopilotModelsService gh auth token failed — using fallback");
                return null;
            }

            token = result.Stdout.Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug(ex, "CopilotModelsService gh auth token failed — using fallback");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ModelsApiUrl);
            var clientVersion = GetClientVersion();
            request.Headers.TryAddWithoutValidation("Authorization", token);
            request.Headers.TryAddWithoutValidation("Editor-Version", $"copilot-booster/{clientVersion}");
            request.Headers.TryAddWithoutValidation("User-Agent", $"copilot-booster/{clientVersion}");

            using var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Program.Logger.LogDebug(
                    "CopilotModelsService API fetch failed status={StatusCode} reason={Reason} — using fallback",
                    (int)response.StatusCode,
                    response.ReasonPhrase);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var models = ParseModelsResponse(json);
            Program.Logger.LogDebug("CopilotModelsService API fetch succeeded count={Count}", models.Count);
            return models;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug(ex, "CopilotModelsService API fetch failed — using fallback");
            return null;
        }
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string fileName,
        string? arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this._processRunner != null)
        {
            var result = await this._processRunner(fileName, arguments).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        return await RunRealProcessAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CacheEntry?> TryReadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(this._cacheFile))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(this._cacheFile, cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("fetchedAt", out var fetchedAtElement) ||
                fetchedAtElement.ValueKind != JsonValueKind.String ||
                !DateTime.TryParse(
                    fetchedAtElement.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var fetchedAt) ||
                !doc.RootElement.TryGetProperty("models", out var modelsElement) ||
                modelsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var models = modelsElement.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.String)
                .Select(element => element.GetString())
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Select(model => model!)
                .ToList();

            return models.Count == 0 ? null : new CacheEntry(fetchedAt, models.AsReadOnly());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug(ex, "CopilotModelsService cache read failed");
            return null;
        }
    }

    private async Task TryWriteCacheAsync(IReadOnlyList<string> models, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(this._cacheFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = new MemoryStream();
            await using (var writer = new Utf8JsonWriter(stream, s_writerOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("fetchedAt", this._clock().ToString("O", CultureInfo.InvariantCulture));
                writer.WriteStartArray("models");
                foreach (var model in models)
                {
                    writer.WriteStringValue(model);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var json = stream.ToArray();
            await File.WriteAllBytesAsync(this._cacheFile, json, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug(ex, "CopilotModelsService cache write failed");
        }
    }

    private bool IsFresh(DateTime fetchedAt)
    {
        return this._clock() - fetchedAt < s_cacheTtl;
    }

    private sealed record CacheEntry(DateTime FetchedAt, IReadOnlyList<string> Models);
}
