using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

internal enum AiMenuState
{
    Enabled,
    FeatureDisabled,
    CopilotUnavailable,
    NoRepo,
    NonGitHubRemote,
    DetectionInFlight,
    Unavailable
}

internal enum UndecidedReason
{
    LowConfidence,
    AllAlreadyLinked
}

internal enum OutcomeKind
{
    None,
    Success,
    Cancelled,
    UndecidedLowConfidence,
    NoCandidatesVariant,
    Failure
}

internal static class AiDetectionTooltips
{
    internal const string FeatureDisabled = "AI auto-detect is disabled in Settings.";
    internal const string CopilotUnavailable = "Copilot CLI not found. Install via WinGet or ensure 'copilot' is on PATH.";
    internal const string NoRepo = "No GitHub repository detected for this session.";
    internal const string NonGitHubRemote = "Non-GitHub providers are currently not supported.";
    internal const string DetectionInFlight = "Detection in progress...";
    internal const string UndecidedLowConfidence = "AI couldn't decide with confidence. Top candidates: ...";
    internal const string UndecidedAllAlreadyLinked = "All matches were already linked to this session.";
    internal const string ErrorTimeout = "Detection timed out after {0} seconds.";
    internal const string ErrorMalformedJson = "Copilot returned an invalid response. See app log for details.";
    internal const string ErrorSchemaViolation = "Copilot returned an invalid response. See app log for details.";
    internal const string ErrorNoCandidates = "No matching GitHub issue or PR was found.";
    internal const string ErrorProcessSpawn = "Detection failed: could not start Copilot. See app log for details.";
    internal const string ErrorProcessFailure = "Detection failed: Copilot exited with error. See app log for details.";

    internal static string For(AiMenuState state)
    {
        return state switch
        {
            AiMenuState.FeatureDisabled => FeatureDisabled,
            AiMenuState.CopilotUnavailable => CopilotUnavailable,
            AiMenuState.NoRepo => NoRepo,
            AiMenuState.NonGitHubRemote => NonGitHubRemote,
            AiMenuState.DetectionInFlight => DetectionInFlight,
            AiMenuState.Unavailable => "AI auto-detect unavailable",
            _ => string.Empty
        };
    }

    internal static string ForFailure(AiFailureClass failureClass, int? timeoutSeconds = null)
    {
        return failureClass switch
        {
            AiFailureClass.Timeout => string.Format(ErrorTimeout, timeoutSeconds ?? 0),
            AiFailureClass.MalformedJson => ErrorMalformedJson,
            AiFailureClass.SchemaViolation => ErrorSchemaViolation,
            AiFailureClass.NoCandidates => ErrorNoCandidates,
            AiFailureClass.ProcessSpawn => ErrorProcessSpawn,
            AiFailureClass.ProcessFailure => ErrorProcessFailure,
            _ => string.Empty
        };
    }

    internal static string ForUndecided(UndecidedReason reason, IReadOnlyList<AiCandidate>? candidates)
    {
        if (reason == UndecidedReason.AllAlreadyLinked)
        {
            return UndecidedAllAlreadyLinked;
        }

        var lines = new List<string> { "AI couldn't decide with confidence. Top candidates:" };
        foreach (var candidate in (candidates ?? []).Take(3))
        {
            lines.Add($"{FormatType(candidate.Type)} #{candidate.Number} (confidence: {candidate.Confidence:0.00}) - {candidate.Reasoning}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatType(string type)
    {
        return type.Equals("pr", StringComparison.OrdinalIgnoreCase) ? "PR" : "Issue";
    }
}

internal sealed class DetectionState
{
    internal DetectionStatus Status { get; set; } = DetectionStatus.Idle;

    internal CancellationTokenSource? Cts { get; set; }

    internal Task? Task { get; set; }

    internal IReadOnlyList<AiCandidate>? TopCandidates { get; set; }

    internal AiFailureClass? FailureClass { get; set; }

    internal UndecidedReason? UndecidedReason { get; set; }

    internal OutcomeKind OutcomeKind { get; set; }

    internal static DetectionState Idle => new();
}

internal sealed class AiDetectionService : IDisposable
{
    private const int MinTimeoutSeconds = 30;
    private const int MaxTimeoutSeconds = 1800;
    private const decimal MinConfidenceThreshold = 0.0m;
    private const decimal MaxConfidenceThreshold = 1.0m;

    private readonly ConcurrentDictionary<string, DetectionState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly GitHubApiService _githubApi;
    private readonly IProcessRunner _processRunner;
    private readonly Func<string, string?> _getSessionCwd;
    private readonly Action<string> _toastSink;
    private readonly GitHubPollingService? _githubPoller;
    private readonly string _sessionStateRoot;
    private readonly string _appLogRoot;
    private readonly Func<AiDetectionSettings?> _settingsGetter;
    private readonly ICopilotProbe _copilotProbe;

    internal event Action<string, DetectionStatus, DetectionStatus>? DetectionStateChanged;

    internal AiDetectionService(
        GitHubApiService githubApi,
        IProcessRunner processRunner,
        Func<string, string?> getSessionCwd,
        Action<string> toastSink,
        GitHubPollingService? githubPoller = null,
        string? sessionStateRoot = null,
        string? appLogRoot = null,
        Func<AiDetectionSettings>? settingsGetter = null,
        ICopilotProbe? copilotProbe = null)
    {
        this._githubApi = githubApi;
        this._processRunner = processRunner;
        this._getSessionCwd = getSessionCwd;
        this._toastSink = toastSink;
        this._githubPoller = githubPoller;
        this._sessionStateRoot = sessionStateRoot ?? Program.SessionStateDir;
        this._appLogRoot = appLogRoot ?? Program.AppDataDir;
        this._settingsGetter = settingsGetter ?? (() => new AiDetectionSettings());
        this._copilotProbe = copilotProbe ?? AlwaysAvailableCopilotProbe.Instance;
    }

    internal AiDetectionService(
        IProcessRunner processRunner,
        GitHubApiService githubApi,
        GitHubPollingService? githubPoller,
        Action<string> toastSink,
        string? sessionStateRoot = null,
        string? appLogRoot = null,
        Func<AiDetectionSettings>? settingsGetter = null,
        ICopilotProbe? copilotProbe = null)
        : this(githubApi, processRunner, sid => ReadSessionCwdFromWorkspace(sessionStateRoot ?? Program.SessionStateDir, sid), toastSink, githubPoller, sessionStateRoot, appLogRoot, settingsGetter, copilotProbe)
    {
    }

    internal Task StartDetectionAsync(string sessionId)
    {
        var invocationSettings = this.ResolveInvocationSettings();
        if (!invocationSettings.Enabled)
        {
            Program.Logger.LogWarning("AI detection skipped session_id={SessionId} reason=disabled_in_settings", sessionId);
            return Task.CompletedTask;
        }

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
            state.UndecidedReason = null;
            state.OutcomeKind = OutcomeKind.None;
            state.Status = DetectionStatus.Running;
            task = Task.Run(() => this.RunDetectionAsync(sessionId, state, cts, invocationSettings), CancellationToken.None);
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

    internal void Reset(string sessionId)
    {
        DetectionStatus oldStatus;
        DetectionState? removedState;
        lock (this._gate)
        {
            if (!this._states.TryGetValue(sessionId, out var state)
                || state.Status is not (DetectionStatus.Undecided or DetectionStatus.Error))
            {
                return;
            }

            oldStatus = state.Status;
            this._states.TryRemove(sessionId, out removedState);
        }

        removedState?.Cts?.Dispose();
        this.DetectionStateChanged?.Invoke(sessionId, oldStatus, DetectionStatus.Idle);
    }

    internal DetectionState TryGetState(string sessionId)
    {
        return this._states.TryGetValue(sessionId, out var state) ? state : DetectionState.Idle;
    }

    internal bool TryGetState(string sessionId, out DetectionState? state)
    {
        return this._states.TryGetValue(sessionId, out state);
    }

    internal AiMenuState EvaluateMenuState(string sessionId, string? sessionCwd)
    {
        if (!this.GetSettings().Enabled)
        {
            return AiMenuState.FeatureDisabled;
        }

        if (!this._copilotProbe.IsCopilotAvailable())
        {
            return AiMenuState.CopilotUnavailable;
        }

        var tracking = GitHubTrackingService.Load(sessionId);
        var hasPriorTrackingRepo = !string.IsNullOrWhiteSpace(tracking?.Owner) && !string.IsNullOrWhiteSpace(tracking.Repo);
        if (!hasPriorTrackingRepo)
        {
            var repo = GitService.ResolveGitHubRepo(sessionCwd ?? string.Empty);
            var repoState = repo.Status switch
            {
                GitHubRepoResolution.Resolved => AiMenuState.Enabled,
                GitHubRepoResolution.NonGitHubRemote => AiMenuState.NonGitHubRemote,
                _ => AiMenuState.NoRepo
            };

            if (repoState != AiMenuState.Enabled)
            {
                return repoState;
            }
        }

        if (this.TryGetState(sessionId).Status == DetectionStatus.Running)
        {
            return AiMenuState.DetectionInFlight;
        }

        return AiMenuState.Enabled;
    }

    public void Dispose()
    {
        foreach (var pair in this._states)
        {
            var state = pair.Value;
            if (state.Status == DetectionStatus.Running)
            {
                Program.Logger.LogInformation("AI detection shutdown cancel session_id={SessionId} outcome=cancelled", pair.Key);
            }

            state.Cts?.Cancel();
        }
    }

    private async Task RunDetectionAsync(string sessionId, DetectionState state, CancellationTokenSource cts, AiDetectionInvocationSettings invocationSettings)
    {
        var stopwatch = Stopwatch.StartNew();
        var outcome = "error";
        var candidateCount = 0;
        double? topConfidence = null;
        var applied = new List<GitHubTrackedItem>();
        AiFailureClass? failureClass = null;
        string? failureReason = null;
        int? failureExitCode = null;
        var terminalStatus = DetectionStatus.Idle;

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

            Program.Logger.LogInformation("AI detection start session_id={SessionId} resolved_owner_repo={Owner}/{Repo} configured_timeout_seconds={TimeoutSeconds}", sessionId, repo.Value.owner, repo.Value.repo, invocationSettings.TimeoutSeconds);

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
            if (!string.IsNullOrWhiteSpace(invocationSettings.Model))
            {
                args.Add("--model");
                args.Add(invocationSettings.Model);
            }

            Program.Logger.LogDebug("AI detection debug session_id={SessionId} exact_prompt_sent={ExactPromptSent}", sessionId, prompt);

            ProcessResult processResult;
            try
            {
                var copilotPath = CopilotLocator.FindCopilotExe();
                processResult = await this._processRunner.RunAsync(copilotPath, args, cwd, invocationSettings.TimeoutSeconds, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failureClass = AiFailureClass.ProcessSpawn;
                failureReason = ex.Message;
                outcome = ToOutcome(failureClass.Value);
                state.FailureClass = failureClass;
                state.OutcomeKind = OutcomeKind.Failure;
                terminalStatus = DetectionStatus.Error;
                Program.Logger.LogDebug(ex, "AI detection process spawn exception session_id={SessionId}", sessionId);
                return;
            }

            Program.Logger.LogDebug("AI detection debug session_id={SessionId} raw_stdout={RawStdout}", sessionId, processResult.Stdout);
            Program.Logger.LogDebug("AI detection debug session_id={SessionId} raw_stderr={RawStderr}", sessionId, processResult.Stderr);

            if (processResult.WasKilled)
            {
                if (cts.IsCancellationRequested)
                {
                    outcome = "cancelled";
                    state.OutcomeKind = OutcomeKind.Cancelled;
                    return;
                }

                failureClass = AiFailureClass.Timeout;
                failureReason = "process was killed after configured timeout";
                outcome = ToOutcome(failureClass.Value);
                state.FailureClass = failureClass;
                state.OutcomeKind = OutcomeKind.Failure;
                terminalStatus = DetectionStatus.Error;
                return;
            }

            if (processResult.ExitCode != 0)
            {
                failureClass = AiFailureClass.ProcessFailure;
                failureReason = string.IsNullOrWhiteSpace(processResult.Stderr) ? "process exited with non-zero exit code" : processResult.Stderr;
                failureExitCode = processResult.ExitCode;
                outcome = ToOutcome(failureClass.Value);
                state.FailureClass = failureClass;
                state.OutcomeKind = OutcomeKind.Failure;
                terminalStatus = DetectionStatus.Error;
                return;
            }

            var parseResult = AiResponseParser.Parse(processResult.Stdout);
            if (parseResult is AiParseResult.Failure parseFailure)
            {
                failureClass = parseFailure.Class;
                failureReason = parseFailure.Reason;
                outcome = ToOutcome(failureClass.Value);
                state.FailureClass = failureClass;
                state.OutcomeKind = OutcomeKind.Failure;
                terminalStatus = DetectionStatus.Error;
                return;
            }

            var candidates = ((AiParseResult.Success)parseResult).Candidates;
            candidateCount = candidates.Count;
            topConfidence = candidates.Count > 0 ? candidates.Max(c => c.Confidence) : null;
            state.TopCandidates = candidates.OrderByDescending(c => c.Confidence).Take(3).ToList();

            if (candidates.Count == 0)
            {
                failureClass = AiFailureClass.NoCandidates;
                failureReason = "response contained no candidates";
                outcome = ToOutcome(failureClass.Value);
                state.FailureClass = failureClass;
                state.OutcomeKind = OutcomeKind.Failure;
                terminalStatus = DetectionStatus.Error;
                return;
            }

            var aboveThresholdCandidates = candidates
                .Where(c => c.Confidence >= (double)invocationSettings.ConfidenceThreshold)
                .ToList();
            if (aboveThresholdCandidates.Count == 0)
            {
                outcome = "undecided";
                state.UndecidedReason = UndecidedReason.LowConfidence;
                state.OutcomeKind = OutcomeKind.UndecidedLowConfidence;
                terminalStatus = DetectionStatus.Undecided;
                return;
            }

            var current = GitHubTrackingService.Load(sessionId);
            var duplicateCandidates = aboveThresholdCandidates
                .Where(candidate => IsAlreadyLinked(current, candidate))
                .ToList();
            var candidatesToApply = aboveThresholdCandidates
                .Where(candidate => !IsAlreadyLinked(current, candidate))
                .ToList();

            if (duplicateCandidates.Count == aboveThresholdCandidates.Count)
            {
                outcome = "no_candidates_variant";
                state.UndecidedReason = UndecidedReason.AllAlreadyLinked;
                state.OutcomeKind = OutcomeKind.NoCandidatesVariant;
                terminalStatus = DetectionStatus.Undecided;
                return;
            }

            foreach (var candidate in candidatesToApply)
            {
                var normalizedType = NormalizeType(candidate.Type);
                if (normalizedType == null)
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
                this._toastSink(FormatSuccessToast(applied, duplicateCandidates));
                outcome = "success";
                state.OutcomeKind = OutcomeKind.Success;
            }
            else
            {
                failureClass = AiFailureClass.NoCandidates;
                failureReason = "no candidates met threshold or survived filtering";
                outcome = ToOutcome(failureClass.Value);
                state.FailureClass = failureClass;
                state.OutcomeKind = OutcomeKind.Failure;
                terminalStatus = DetectionStatus.Error;
            }
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            state.OutcomeKind = OutcomeKind.Cancelled;
        }
        catch (Exception ex)
        {
            outcome = "error";
            failureReason = ex.Message;
            Program.Logger.LogError(ex, "AI detection failed for {SessionId}", sessionId);
        }
        finally
        {
            stopwatch.Stop();
            LogDetectionEnd(sessionId, outcome, candidateCount, topConfidence, applied, stopwatch.ElapsedMilliseconds, failureClass, failureReason, failureExitCode);
            this.TransitionToStatus(sessionId, state, cts, terminalStatus);
        }
    }

    private static void LogDetectionEnd(
        string sessionId,
        string outcome,
        int candidateCount,
        double? topConfidence,
        IReadOnlyList<GitHubTrackedItem> applied,
        long durationMs,
        AiFailureClass? failureClass,
        string? failureReason,
        int? failureExitCode)
    {
        if (failureClass == null)
        {
            if (outcome is "undecided" or "no_candidates_variant")
            {
                Program.Logger.LogWarning(
                    "AI detection end session_id={SessionId} outcome={Outcome} candidate_count={CandidateCount} top_confidence={TopConfidence} applied_items={AppliedItems} duration_ms={DurationMs}",
                    sessionId,
                    outcome,
                    candidateCount,
                    topConfidence,
                    FormatAppliedItems(applied),
                    durationMs);
                return;
            }

            Program.Logger.LogInformation(
                "AI detection end session_id={SessionId} outcome={Outcome} candidate_count={CandidateCount} top_confidence={TopConfidence} applied_items={AppliedItems} duration_ms={DurationMs}",
                sessionId,
                outcome,
                candidateCount,
                topConfidence,
                FormatAppliedItems(applied),
                durationMs);
            return;
        }

        if (failureClass is AiFailureClass.Timeout or AiFailureClass.NoCandidates)
        {
            Program.Logger.LogWarning(
                "AI detection end session_id={SessionId} outcome={Outcome} failure_class={FailureClass} reason={Reason} exit_code={ExitCode} candidate_count={CandidateCount} top_confidence={TopConfidence} applied_items={AppliedItems} duration_ms={DurationMs}",
                sessionId,
                outcome,
                failureClass,
                failureReason,
                failureExitCode,
                candidateCount,
                topConfidence,
                FormatAppliedItems(applied),
                durationMs);
            return;
        }

        Program.Logger.LogError(
            "AI detection end session_id={SessionId} outcome={Outcome} failure_class={FailureClass} reason={Reason} exit_code={ExitCode} candidate_count={CandidateCount} top_confidence={TopConfidence} applied_items={AppliedItems} duration_ms={DurationMs}",
            sessionId,
            outcome,
            failureClass,
            failureReason,
            failureExitCode,
            candidateCount,
            topConfidence,
            FormatAppliedItems(applied),
            durationMs);
    }

    private static string ToOutcome(AiFailureClass failureClass)
    {
        return failureClass switch
        {
            AiFailureClass.Timeout => "timeout",
            AiFailureClass.ProcessSpawn => "process_spawn",
            AiFailureClass.ProcessFailure => "process_failure",
            AiFailureClass.MalformedJson => "malformed_json",
            AiFailureClass.SchemaViolation => "schema_violation",
            AiFailureClass.NoCandidates => "no_candidates",
            _ => failureClass.ToString()
        };
    }

    private static (string owner, string repo)? ResolveOwnerRepo(string sessionId, string cwd)
    {
        var tracking = GitHubTrackingService.Load(sessionId);
        if (!string.IsNullOrWhiteSpace(tracking?.Owner) && !string.IsNullOrWhiteSpace(tracking.Repo))
        {
            return (tracking.Owner, tracking.Repo);
        }

        return GitService.TryResolveGitHubRepo(cwd);
    }

    private AiDetectionInvocationSettings ResolveInvocationSettings()
    {
        var settings = this.GetSettings();
        var timeoutSeconds = Math.Clamp(settings.TimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);
        var confidenceThreshold = Math.Min(MaxConfidenceThreshold, Math.Max(MinConfidenceThreshold, settings.ConfidenceThreshold));
        if (timeoutSeconds != settings.TimeoutSeconds || confidenceThreshold != settings.ConfidenceThreshold)
        {
            Program.Logger.LogWarning(
                "AI detection settings clamped configured_timeout_seconds={ConfiguredTimeoutSeconds} resolved_timeout_seconds={ResolvedTimeoutSeconds} configured_confidence_threshold={ConfiguredConfidenceThreshold} resolved_confidence_threshold={ResolvedConfidenceThreshold}",
                settings.TimeoutSeconds,
                timeoutSeconds,
                settings.ConfidenceThreshold,
                confidenceThreshold);
        }

        return new AiDetectionInvocationSettings(
            settings.Enabled,
            timeoutSeconds,
            confidenceThreshold,
            settings.Model?.Trim() ?? "");
    }

    private AiDetectionSettings GetSettings()
    {
        return this._settingsGetter() ?? new AiDetectionSettings();
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
            if (root.TryGetProperty("labels", out var labelsArray) && labelsArray.ValueKind == System.Text.Json.JsonValueKind.Array)
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
                StateReason = root.TryGetProperty("state_reason", out var stateReason) && stateReason.ValueKind != System.Text.Json.JsonValueKind.Null ? stateReason.GetString() : null,
                Title = root.GetProperty("title").GetString() ?? "",
                Author = root.TryGetProperty("user", out var user) && user.TryGetProperty("login", out var login) ? login.GetString() ?? "" : "",
                Labels = labels,
                LastModifiedAt = root.TryGetProperty("updated_at", out var updatedAt) ? updatedAt.GetString() ?? "" : "",
                LastSeenAt = DateTime.UtcNow.ToString("o")
            };
        }
    }

    private void TransitionToStatus(string sessionId, DetectionState state, CancellationTokenSource cts, DetectionStatus newStatus)
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

            state.Status = newStatus;
        }

        cts.Dispose();
        if (oldStatus != newStatus)
        {
            this.DetectionStateChanged?.Invoke(sessionId, oldStatus, newStatus);
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
        if (type == "pr")
        {
            return "pr";
        }

        if (type == "issue")
        {
            return "issue";
        }

        return null;
    }

    private static bool IsAlreadyLinked(GitHubTrackingData? current, AiCandidate candidate)
    {
        var normalizedType = NormalizeType(candidate.Type);
        return normalizedType != null
            && current?.Items.Any(i => i.Type.Equals(normalizedType, StringComparison.OrdinalIgnoreCase) && i.Number == candidate.Number) == true;
    }

    private static string FormatSuccessToast(IReadOnlyList<GitHubTrackedItem> applied, List<AiCandidate> duplicates)
    {
        if (duplicates.Count > 0)
        {
            return $"✅ AI added {FormatAppliedItems(applied)} (already linked: {FormatCandidateItems(duplicates)})";
        }

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

    private static string FormatCandidateItems(List<AiCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return "none";
        }

        return string.Join(" + ", candidates.Select(candidate => $"{FormatType(candidate.Type)} #{candidate.Number}"));
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

    private sealed record AiDetectionInvocationSettings(
        bool Enabled,
        int TimeoutSeconds,
        decimal ConfidenceThreshold,
        string Model);

    private sealed class AlwaysAvailableCopilotProbe : ICopilotProbe
    {
        internal static readonly AlwaysAvailableCopilotProbe Instance = new();

        public bool IsCopilotAvailable() => true;

        public void InvalidateCache()
        {
        }
    }
}
