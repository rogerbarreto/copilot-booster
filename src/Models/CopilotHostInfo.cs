using System;

namespace CopilotBooster.Models;

/// <summary>
/// Information about the resolved Copilot Host for a Copilot CLI session.
/// HostPid is the focusable ancestor process; CopilotPid is the copilot.exe leaf process.
/// HostHwnd is the focusable top-level window owned by HostPid.
/// HostKindLabel is the friendly label from HostKindClassifier (e.g., "Windows Terminal", "Console", "Unknown").
/// </summary>
internal record CopilotHostInfo(
    IntPtr HostHwnd,
    int HostPid,
    int CopilotPid,
    string HostProcessName,
    string HostKindLabel,
    IntPtr ParentHostHwnd = default,
    string? PaneTitle = null);
