using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CopilotBooster.Models;

/// <summary>
/// Persisted GitHub tracking data for a session.
/// Stored as <c>github-tracking.json</c> in the session's app data directory.
/// </summary>
internal class GitHubTrackingData
{
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = "";

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = "";

    [JsonPropertyName("branch")]
    public string Branch { get; set; } = "";

    [JsonPropertyName("items")]
    public List<GitHubTrackedItem> Items { get; set; } = [];
}

/// <summary>
/// A tracked PR or Issue within a session's GitHub tracking data.
/// </summary>
internal class GitHubTrackedItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ""; // "pr" or "issue"

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = "open"; // "open", "closed", "merged" (PR only)

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("labels")]
    public List<string> Labels { get; set; } = [];

    /// <summary>
    /// PR-only: combined check status — "success", "failure", "pending", or "neutral".
    /// </summary>
    [JsonPropertyName("checks")]
    public string Checks { get; set; } = "";

    /// <summary>
    /// PR-only: number of approving reviews.
    /// </summary>
    [JsonPropertyName("approvals")]
    public int Approvals { get; set; }

    /// <summary>
    /// PR-only: GitHub usernames of approving reviewers.
    /// </summary>
    [JsonPropertyName("approvers")]
    public List<string> Approvers { get; set; } = [];

    /// <summary>
    /// PR-only: head branch name.
    /// </summary>
    [JsonPropertyName("headBranch")]
    public string HeadBranch { get; set; } = "";

    /// <summary>
    /// Last time the user viewed/acknowledged this item (clears red dot).
    /// </summary>
    [JsonPropertyName("lastSeenAt")]
    public string LastSeenAt { get; set; } = "";

    /// <summary>
    /// Last known modification time from GitHub (updated_at field).
    /// </summary>
    [JsonPropertyName("lastModifiedAt")]
    public string LastModifiedAt { get; set; } = "";

    /// <summary>
    /// True if the item has new activity since <see cref="LastSeenAt"/>.
    /// </summary>
    [JsonPropertyName("hasNewActivity")]
    public bool HasNewActivity { get; set; }

    /// <summary>
    /// Returns true if this is a PR item.
    /// </summary>
    [JsonIgnore]
    public bool IsPr => this.Type.Equals("pr", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true if this item is in a final state (merged, closed).
    /// </summary>
    [JsonIgnore]
    public bool IsFinal => this.State is "merged" or "closed";
}
