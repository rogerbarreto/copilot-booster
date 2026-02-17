
using System;

namespace CopilotBooster.Models;

/// <summary>
/// Represents a saved session with metadata for display and selection.
/// </summary>
internal class NamedSession
{
    /// <summary>
    /// Gets or sets the unique identifier for the session.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the current working directory of the session.
    /// </summary>
    public string Cwd { get; set; } = "";

    /// <summary>
    /// Gets or sets a brief summary or description of the session.
    /// </summary>
    public string Summary { get; set; } = "";

    /// <summary>
    /// Gets or sets the folder name extracted from the CWD path.
    /// </summary>
    public string Folder { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the session CWD is inside a Git repository.
    /// </summary>
    public bool IsGitRepo { get; set; }

    /// <summary>
    /// Gets or sets the user-defined alias for the session.
    /// When set, this is displayed instead of the summary in the UI.
    /// </summary>
    public string Alias { get; set; } = "";

    /// <summary>
    /// Gets or sets the timestamp when the session was last modified.
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Gets or sets whether this session has been archived.
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Gets or sets whether this session is pinned to the top of the list.
    /// </summary>
    public bool IsPinned { get; set; }
}
