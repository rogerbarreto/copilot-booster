using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CopilotBooster.Models;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

internal enum DetectionStatus
{
    Idle,
    Running,
    Undecided,
    Error
}

internal sealed class DetectionState
{
    internal DetectionStatus Status { get; set; } = DetectionStatus.Idle;

    internal CancellationTokenSource? Cts { get; set; }

    internal Task? Task { get; set; }

    internal IReadOnlyList<AiCandidate>? TopCandidates { get; set; }

    internal string? FailureClass { get; set; }

    internal static DetectionState Idle => new();
}

internal sealed class AiDetectionService : IDisposable
{
    private const int TimeoutSeconds = 300;
    private const double ConfidenceThreshold = 0.5;
    private const string CopilotBinary = "copilot";

    private readonly ConcurrentDictionary<string, DetectionState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly GitHubApiService _githubApi;
    private readonly IProcessRunner _processRunner;
    private readonly Func<string, string?> _getSessionCwd;
    private readonly Action<string> _toastSink;
    private readonly GitHubPollingService? _githubPoller;
    private readonly string _sessionStateRoot;
    private readonly string _appLogRoot;

    internal event Action<string, DetectionStatus, DetectionStatus>? DetectionStateChanged;

    internal AiDetectionService(
        GitHubApiService githubApi,
        IProcessRunner processRunner,
        Func<string, string?> getSessionCwd,
        Action<string> toastSink,
        GitHubPollingService? githubPoller = null,
        string? sessionStateRoot = null,
        string? appLogRoot = null)
    {
        this._githubApi = githubApi;
        this._processRunner = processRunner;
        this._getSessionCwd = getSessionCwd;
        this._toastSink = toastSink;
        this._githubPoller = githubPoller;
        this._sessionStateRoot = sessionStateRoot ?? Program.SessionStateDir;
        this._appLogRoot = appLogRoot ?? Program.AppDataDir;
    }

    internal AiDetectionService(
        IProcessRunner processRunner,
        GitHubApiService githubApi,
        GitHubPollingService? githubPoller,
        Action<string> toastSink,
        string? sessionStateRoot = null,
        string? appLogRoot = null)
        : this(githubApi, processRunner, sid => ReadSessionCwdFromWorkspace(sessionStateRoot ?? Program.SessionStateDir, sid), toastSink, githubPoller, sessionStateRoot, appLogRoot)
    {
    }

    internal Task StartDetectionAsync(string sessionId)
    {
        DetectionState state;
        DetectionStatus oldStatus;
        Task task;
        var cts = new CancellationTokenSource();

        lock (this._gate)
        {
            state = this._states.GetOrAdd(sessionId, _ => new DetectionState());
            if (state.Status == DetectionStatus.Running)
            {
                cts.Dispose();
                return state.Task ?? Task.CompletedTask;
            }

            oldStatus = state.Status;
            state.Cts?.Dispose();
            state.Cts = cts;
            state.TopCandidates = null;
            state.FailureClass = null;
            state.Status = DetectionStatus.Running;
            task = Task.Run(() => this.RunDetectionAsync(sessionId, state, cts), CancellationToken.None);
            state.Task = task;
        }

        this.DetectionStateChanged?.Invoke(sessionId, oldStatus, DetectionStatus.Running);
        return task;
    }

    internal void CancelDetection(string sessionId)
    {
        if (this._states.TryGetValue(sessionId, out var state) && state.Status == DetectionStatus.Running)
        {
            state.Cts?.Cancel();
        }
    }

    internal DetectionState TryGetState(string sessionId)
    {
        return this._states.TryGetValue(sessionId, out var state) ? state : DetectionState.Idle;
    }

    internal bool TryGetState(string sessionId, out DetectionState? state)
    {
        return this._states.TryGetValue(sessionId, out state);
    }

    public void Dispose()
    {
        foreach (var state in this._states.Values)
        {
            state.Cts?.Cancel();
        }
    }

    private async Task RunDetectionAsync(string sessionId, DetectionState state, CancellationTokenSource cts)
    {
        var stopwatch = Stopwatch.StartNew();
        var outcome = "error";
        var candidateCount = 0;
        double? topConfidence = null;
        var applied = new List<GitHubTrackedItem>();

        try
        {
            var sessionStateFolder = Path.Combine(this._sessionStateRoot, sessionId);
            if (!Directory.Exists(sessionStateFolder))
            {
                outcome = "session_missing";
                Program.Logger.LogWarning("AI detection session folder missing for {SessionId}: {SessionStateFolder}", sessionId, sessionStateFolder);
                return;
            }

            var cwd = this._getSessionCwd(sessionId);
            if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
            {
                outcome = "no_cwd";
                Program.Logger.LogWarning("AI detection could not resolve CWD for {SessionId}", sessionId);
                return;
            }

            var repo = ResolveOwnerRepo(sessionId, cwd);
            if (repo == null)
            {
                outcome = "no_repo";
                Program.Logger.LogWarning("AI detection could not resolve GitHub repo for {SessionId}", sessionId);
                return;
            }

            Program.Logger.LogInformation("AI detection start session_id={SessionId} resolved_owner_repo={Owner}/{Repo} configured_timeout_seconds={TimeoutSeconds}", sessionId, repo.Value.owner, repo.Value.repo, TimeoutSeconds);

            var prompt = AiPromptBuilder.Build(repo.Value.owner, repo.Value.repo, sessionStateFolder);
            var logDir = this.CreateInvocationLogDir(sessionId);
            var args = new List<string>
            {
                "-p", prompt,
                "-s",
                "--no-ask-user",
                "--allow-all-tools",
                "--add-dir", sessionStateFolder,
                "--allow-url", "github.com",
                "--allow-url", "api.github.com",
                "-C", cwd,
                "--log-dir", logDir
            };

            Program.Logger.LogDebug("AI detection prompt for {SessionId}: {Prompt}", sessionId, prompt);

            ProcessResult processResult;
            try
            {
                processResult = await this._processRunner.RunAsync(CopilotBinary, args, cwd, TimeoutSeconds, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome = "process_spawn";
                state.FailureClass = outcome;
                Program.Logger.LogError(ex, "AI detection process spawn failed for {SessionId}", sessionId);
                return;
            }

            Program.Logger.LogDebug("AI detection raw stdout for {SessionId}: {Stdout}", sessionId, processResult.Stdout);
            Program.Logger.LogDebug("AI detection raw stderr for {SessionId}: {Stderr}", sessionId, processResult.Stderr);

            if (processResult.WasKilled)
            {
                outcome = cts.IsCancellationRequested ? "cancelled" : "timeout";
                state.FailureClass = outcome;
                Program.Logger.LogWarning("AI detection ended with {Outcome} for {SessionId}", outcome, sessionId);
                return;
            }

            if (processResult.ExitCode != 0)
            {
                outcome = "process_failure";
                state.FailureClass = outcome;
                Program.Logger.LogError("AI detection process failed for {SessionId} with exit code {ExitCode}: {Stderr}", sessionId, processResult.ExitCode, processResult.Stderr);
                return;
            }

            if (!IsParseableJson(processResult.Stdout))
            {
                outcome = "malformed_json";
                state.FailureClass = outcome;
                Program.Logger.LogError("AI detection returned malformed JSON for {SessionId}", sessionId);
                return;
            }

            var candidates = AiResponseParser.Parse(processResult.Stdout);
            candidateCount = candidates.Count;
            topConfidence = candidates.Count > 0 ? candidates.Max(c => c.Confidence) : null;
            state.TopCandidates = candidates;

            foreach (var candidate in candidates.Where(c => c.Confidence >= ConfidenceThreshold))
            {
                var normalizedType = NormalizeType(candidate.Type);
                if (normalizedType == null)
                {
                    continue;
                }

                var current = GitHubTrackingService.Load(sessionId);
                if (current?.Items.Any(i => i.Type.Equals(normalizedType, StringComparison.OrdinalIgnoreCase) && i.Number == candidate.Number) == true)
                {
                    continue;
                }

                var item = await this.EnrichCandidateAsync(normalizedType, candidate.Number, repo.Value.owner, repo.Value.repo).ConfigureAwait(false);
                if (item == null)
                {
                    continue;
                }

                GitHubTrackingService.AddItem(sessionId, repo.Value.owner, repo.Value.repo, item);
                this._githubPoller?.PollSessionNow(sessionId);
                applied.Add(item);
            }

            if (applied.Count > 0)
            {
                this._toastSink(FormatSuccessToast(applied));
            }

            outcome = applied.Count > 0 ? "success" : "no_candidates";
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            state.FailureClass = outcome;
            Program.Logger.LogWarning("AI detection cancelled for {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            outcome = "error";
            state.FailureClass = outcome;
            Program.Logger.LogError(ex, "AI detection failed for {SessionId}", sessionId);
        }
        finally
        {
            stopwatch.Stop();
            Program.Logger.LogInformation("AI detection end outcome={Outcome} candidate_count={CandidateCount} top_confidence={TopConfidence} applied_items={AppliedItems} duration_ms={DurationMs}", outcome, candidateCount, topConfidence, FormatAppliedItems(applied), stopwatch.ElapsedMilliseconds);
            this.TransitionToIdle(sessionId, state, cts);
        }
    }

    private static (string owner, string repo)? ResolveOwnerRepo(string sessionId, string cwd)
    {
        var tracking = GitHubTrackingService.Load(sessionId);
        if (!string.IsNullOrWhiteSpace(tracking?.Owner) && !string.IsNullOrWhiteSpace(tracking.Repo))
        {
            return (tracking.Owner, tracking.Repo);
        }

        var gitRoot = SessionService.FindGitRoot(cwd);
        if (gitRoot == null)
        {
            return null;
        }

        // TODO(slice #19): replace this origin-only fallback with GitService.TryResolveGitHubRepo.
        var originUrl = GitService.GetRemoteUrl(gitRoot, "origin");
        return originUrl == null ? null : GitService.ParseGitHubOwnerRepo(originUrl);
    }

    private async Task<GitHubTrackedItem?> EnrichCandidateAsync(string type, int number, string owner, string repo)
    {
        if (type == "pr")
        {
            var doc = await this._githubApi.GetPullRequestAsync(owner, repo, number).ConfigureAwait(false);
            if (doc == null)
            {
                return null;
            }

            using (doc)
            {
                var root = doc.RootElement;
                var merged = root.TryGetProperty("merged", out var mergedProp) && mergedProp.GetBoolean();
                var state = root.GetProperty("state").GetString() ?? "open";
                return new GitHubTrackedItem
                {
                    Type = "pr",
                    Number = number,
                    State = merged ? "merged" : state,
                    Draft = root.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean(),
                    Title = root.GetProperty("title").GetString() ?? "",
                    Author = root.TryGetProperty("user", out var user) && user.TryGetProperty("login", out var login) ? login.GetString() ?? "" : "",
                    HeadBranch = root.TryGetProperty("head", out var head) && head.TryGetProperty("ref", out var headRef) ? headRef.GetString() ?? "" : "",
                    LastModifiedAt = root.TryGetProperty("updated_at", out var updatedAt) ? updatedAt.GetString() ?? "" : "",
                    LastSeenAt = DateTime.UtcNow.ToString("o")
                };
            }
        }

        var issueDoc = await this._githubApi.GetIssueAsync(owner, repo, number).ConfigureAwait(false);
        if (issueDoc == null)
        {
            return null;
        }

        using (issueDoc)
        {
            var root = issueDoc.RootElement;
            var labels = new List<string>();
            if (root.TryGetProperty("labels", out var labelsArray) && labelsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var label in labelsArray.EnumerateArray())
                {
                    if (label.TryGetProperty("name", out var name))
                    {
                        labels.Add(name.GetString() ?? "");
                    }
                }
            }

            return new GitHubTrackedItem
            {
                Type = "issue",
                Number = number,
                State = root.GetProperty("state").GetString() ?? "open",
                StateReason = root.TryGetProperty("state_reason", out var stateReason) && stateReason.ValueKind != JsonValueKind.Null ? stateReason.GetString() : null,
                Title = root.GetProperty("title").GetString() ?? "",
                Author = root.TryGetProperty("user", out var user) && user.TryGetProperty("login", out var login) ? login.GetString() ?? "" : "",
                Labels = labels,
                LastModifiedAt = root.TryGetProperty("updated_at", out var updatedAt) ? updatedAt.GetString() ?? "" : "",
                LastSeenAt = DateTime.UtcNow.ToString("o")
            };
        }
    }

    private void TransitionToIdle(string sessionId, DetectionState state, CancellationTokenSource cts)
    {
        DetectionStatus oldStatus;
        lock (this._gate)
        {
            oldStatus = state.Status;
            if (ReferenceEquals(state.Cts, cts))
            {
                state.Cts = null;
                state.Task = Task.CompletedTask;
            }

            state.Status = DetectionStatus.Idle;
        }

        cts.Dispose();
        if (oldStatus != DetectionStatus.Idle)
        {
            this.DetectionStateChanged?.Invoke(sessionId, oldStatus, DetectionStatus.Idle);
        }
    }

    private string CreateInvocationLogDir(string sessionId)
    {
        var shortSessionId = sessionId[..Math.Min(8, sessionId.Length)];
        var folder = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{shortSessionId}";
        var logDir = Path.Combine(this._appLogRoot, "copilot-booster-detect", folder);
        Directory.CreateDirectory(logDir);
        return logDir;
    }

    private static string? NormalizeType(string type)
    {
        if (type.Equals("pr", StringComparison.OrdinalIgnoreCase))
        {
            return "pr";
        }

        if (type.Equals("issue", StringComparison.OrdinalIgnoreCase))
        {
            return "issue";
        }

        return null;
    }

    private static bool IsParseableJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string FormatSuccessToast(IReadOnlyList<GitHubTrackedItem> applied)
    {
        return $"✅ AI added {FormatAppliedItems(applied)} to session";
    }

    private static string FormatAppliedItems(IReadOnlyList<GitHubTrackedItem> applied)
    {
        if (applied.Count == 0)
        {
            return "none";
        }

        return string.Join(" + ", applied.Select(item => $"{FormatType(item.Type)} #{item.Number}"));
    }

    private static string FormatType(string type)
    {
        return type.Equals("pr", StringComparison.OrdinalIgnoreCase) ? "PR" : "Issue";
    }

    private static string? ReadSessionCwdFromWorkspace(string sessionStateRoot, string sessionId)
    {
        var workspaceFile = Path.Combine(sessionStateRoot, sessionId, "workspace.yaml");
        if (!File.Exists(workspaceFile))
        {
            return null;
        }

        foreach (var line in File.ReadLines(workspaceFile))
        {
            if (line.StartsWith("cwd:", StringComparison.Ordinal))
            {
                return line[4..].Trim();
            }
        }

        return null;
    }
}
