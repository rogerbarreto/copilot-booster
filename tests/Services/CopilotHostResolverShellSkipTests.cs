namespace CopilotBooster.Tests.Services;

/// <summary>
/// Tests for CopilotHostResolver shell-wrapper skip behavior (Bug: pwsh intercepts Warp focus).
/// Option A fix: Skip shell wrappers (PowerShell, Command Prompt, Console) with HWNDs,
/// continue walking to find terminal hosts (Warp, Windows Terminal, WezTerm).
/// Fall back to shell if no terminal host found (standalone pwsh scenario).
/// </summary>
public sealed class CopilotHostResolverShellSkipTests
{
    private sealed class FakeProcessTree : IProcessTreeProvider
    {
        private readonly Dictionary<int, int?> _parents = [];
        private readonly Dictionary<int, string?> _names = [];
        private readonly Dictionary<int, IntPtr> _windows = [];

        internal FakeProcessTree Add(int pid, int? parentPid, string? name, IntPtr window)
        {
            this._parents[pid] = parentPid;
            this._names[pid] = name;
            this._windows[pid] = window;
            return this;
        }

        public int? GetParentPid(int pid) => this._parents.TryGetValue(pid, out var p) ? p : null;
        public string? GetProcessName(int pid) => this._names.TryGetValue(pid, out var n) ? n : null;
        public IntPtr GetTopLevelWindow(int pid) => this._windows.TryGetValue(pid, out var w) ? w : IntPtr.Zero;
        public IReadOnlyList<IntPtr> EnumerateTopLevelWindows(int pid)
        {
            return this._windows.TryGetValue(pid, out var w) && w != IntPtr.Zero
                ? [w]
                : Array.Empty<IntPtr>();
        }
    }

    [Fact]
    public void Resolve_PwshInsideWarp_ReturnsWarpNotPwsh()
    {
        // Arrange: copilot(100) → pwsh(200, HWND=0x123) → warp(300, HWND=0x456)
        var tree = new FakeProcessTree()
            .Add(100, 200, "copilot", IntPtr.Zero)
            .Add(200, 300, "pwsh", new IntPtr(0x123))
            .Add(300, null, "warp", new IntPtr(0x456));
        var resolver = new CopilotHostResolver(tree, 0);

        // Act
        var result = resolver.Resolve(100);

        // Assert: Should return Warp (PID 300), NOT pwsh (PID 200)
        Assert.NotNull(result);
        Assert.Equal("Warp", result!.HostKindLabel);
        Assert.Equal(300, result.HostPid);
        Assert.Equal(new IntPtr(0x456), result.HostHwnd);
        Assert.Equal("warp", result.HostProcessName);
    }

    [Fact]
    public void Resolve_CmdInsideWindowsTerminal_ReturnsWindowsTerminalNotCmd()
    {
        // Arrange: copilot(100) → cmd(200, HWND=0xAAA) → WindowsTerminal(300, HWND=0xBBB)
        var tree = new FakeProcessTree()
            .Add(100, 200, "copilot", IntPtr.Zero)
            .Add(200, 300, "cmd", new IntPtr(0xAAA))
            .Add(300, null, "WindowsTerminal", new IntPtr(0xBBB));
        var resolver = new CopilotHostResolver(tree, 0);

        // Act
        var result = resolver.Resolve(100);

        // Assert: Should return Windows Terminal (PID 300), NOT cmd (PID 200)
        Assert.NotNull(result);
        Assert.Equal("Windows Terminal", result!.HostKindLabel);
        Assert.Equal(300, result.HostPid);
        Assert.Equal(new IntPtr(0xBBB), result.HostHwnd);
        Assert.Equal("WindowsTerminal", result.HostProcessName);
    }

    [Fact]
    public void Resolve_PwshInsideWezTerm_ReturnsWezTermNotPwsh()
    {
        // Arrange: copilot(100) → pwsh(200, HWND=0x789) → wezterm-gui(300, HWND=0x999)
        var tree = new FakeProcessTree()
            .Add(100, 200, "copilot", IntPtr.Zero)
            .Add(200, 300, "pwsh", new IntPtr(0x789))
            .Add(300, null, "wezterm-gui", new IntPtr(0x999));
        var resolver = new CopilotHostResolver(tree, 0);

        // Act
        var result = resolver.Resolve(100);

        // Assert: Should return WezTerm (PID 300), NOT pwsh (PID 200)
        Assert.NotNull(result);
        Assert.Equal("WezTerm", result!.HostKindLabel);
        Assert.Equal(300, result.HostPid);
        Assert.Equal(new IntPtr(0x999), result.HostHwnd);
        Assert.Equal("wezterm-gui", result.HostProcessName);
    }

    [Fact]
    public void Resolve_StandalonePwsh_ReturnsPwshAsFallback()
    {
        // Arrange: copilot(100) → pwsh(200, HWND=0x111) — chain ends (no terminal ancestor)
        var tree = new FakeProcessTree()
            .Add(100, 200, "copilot", IntPtr.Zero)
            .Add(200, null, "pwsh", new IntPtr(0x111));
        var resolver = new CopilotHostResolver(tree, 0);

        // Act
        var result = resolver.Resolve(100);

        // Assert: Should return pwsh (PID 200) as fallback since no terminal host found
        Assert.NotNull(result);
        Assert.Equal("PowerShell", result!.HostKindLabel);
        Assert.Equal(200, result.HostPid);
        Assert.Equal(new IntPtr(0x111), result.HostHwnd);
        Assert.Equal("pwsh", result.HostProcessName);
    }

    [Fact]
    public void Resolve_PwshThenAnotherShell_PrefersOuterTerminal()
    {
        // Arrange: copilot(100) → pwsh(200, HWND=0x222) → cmd(300, HWND=0x333) → warp(400, HWND=0x444)
        var tree = new FakeProcessTree()
            .Add(100, 200, "copilot", IntPtr.Zero)
            .Add(200, 300, "pwsh", new IntPtr(0x222))
            .Add(300, 400, "cmd", new IntPtr(0x333))
            .Add(400, null, "warp", new IntPtr(0x444));
        var resolver = new CopilotHostResolver(tree, 0);

        // Act
        var result = resolver.Resolve(100);

        // Assert: Should skip BOTH shells (pwsh, cmd) and return Warp (PID 400)
        Assert.NotNull(result);
        Assert.Equal("Warp", result!.HostKindLabel);
        Assert.Equal(400, result.HostPid);
        Assert.Equal(new IntPtr(0x444), result.HostHwnd);
        Assert.Equal("warp", result.HostProcessName);
    }

    [Fact]
    public void Resolve_DirectWarpNoShell_StillReturnsWarp()
    {
        // Arrange: copilot(100) → warp(200, HWND=0x555) — no shell wrapper in chain
        var tree = new FakeProcessTree()
            .Add(100, 200, "copilot", IntPtr.Zero)
            .Add(200, null, "warp", new IntPtr(0x555));
        var resolver = new CopilotHostResolver(tree, 0);

        // Act
        var result = resolver.Resolve(100);

        // Assert: Should return Warp (PID 200) immediately (regression prevention)
        Assert.NotNull(result);
        Assert.Equal("Warp", result!.HostKindLabel);
        Assert.Equal(200, result.HostPid);
        Assert.Equal(new IntPtr(0x555), result.HostHwnd);
        Assert.Equal("warp", result.HostProcessName);
    }

    [Fact]
    public void Resolve_PwshWithoutHwnd_StillSkippedAndWalkedPast()
    {
        // Arrange: copilot(100) → pwsh(200, HWND=0) → warp(300, HWND=0x777)
        // This already works today via existing HWND==0 skip; test guards against regressions.
        var tree = new FakeProcessTree()
            .Add(100, 200, "copilot", IntPtr.Zero)
            .Add(200, 300, "pwsh", IntPtr.Zero)
            .Add(300, null, "warp", new IntPtr(0x777));
        var resolver = new CopilotHostResolver(tree, 0);

        // Act
        var result = resolver.Resolve(100);

        // Assert: Should return Warp (PID 300) — pwsh with no HWND is already skipped
        Assert.NotNull(result);
        Assert.Equal("Warp", result!.HostKindLabel);
        Assert.Equal(300, result.HostPid);
        Assert.Equal(new IntPtr(0x777), result.HostHwnd);
        Assert.Equal("warp", result.HostProcessName);
    }
}
