
using System;

namespace CopilotApp.Models;

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
    /// Gets or sets the timestamp when the session was last modified.
    /// </summary>
    public DateTime LastModified { get; set; }
}
