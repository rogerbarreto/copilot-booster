namespace CopilotBooster.Tests.Services;

public sealed class CopilotHostResolverTests
{
    private sealed class FakeProcessTree : IProcessTreeProvider
    {
        private readonly Dictionary<int, int?> _parents = [];
        private readonly Dictionary<int, string?> _names = [];
        private readonly Dictionary<int, IntPtr> _windows = [];
        private readonly Dictionary<int, IReadOnlyList<IntPtr>> _allWindows = [];

        internal FakeProcessTree Add(int pid, int? parentPid, string? name, IntPtr window)
        {
            this._parents[pid] = parentPid;
            this._names[pid] = name;
            this._windows[pid] = window;
            return this;
        }

        internal FakeProcessTree AddWindows(int pid, params IntPtr[] hwnds)
        {
            this._allWindows[pid] = hwnds;
            if (hwnds.Length > 0)
            {
                this._windows[pid] = hwnds[0];
            }
            return this;
        }

        public int? GetParentPid(int pid) => this._parents.TryGetValue(pid, out var p) ? p : null;
        public string? GetProcessName(int pid) => this._names.TryGetValue(pid, out var n) ? n : null;
        public IntPtr GetTopLevelWindow(int pid) => this._windows.TryGetValue(pid, out var w) ? w : IntPtr.Zero;
        public IReadOnlyList<IntPtr> EnumerateTopLevelWindows(int pid)
        {
            if (this._allWindows.TryGetValue(pid, out var list))
            {
                return list;
            }
            return this._windows.TryGetValue(pid, out var w) && w != IntPtr.Zero
                ? [w]
                : Array.Empty<IntPtr>();
        }
    }

    [Fact]
    public void Resolve_NoParent_ReturnsNull()
    {
        var tree = new FakeProcessTree().Add(1000, null, "copilot", IntPtr.Zero);
        var resolver = new CopilotHostResolver(tree, 0);

        var result = resolver.Resolve(1000);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_ParentHasNoWindow_KeepsWalking()
    {
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(900, 800, "intermediate", IntPtr.Zero)
            .Add(800, null, "WindowsTerminal", new IntPtr(0xABC));
        var resolver = new CopilotHostResolver(tree, 0);

        var result = resolver.Resolve(1000);

        Assert.NotNull(result);
        Assert.Equal(new IntPtr(0xABC), result!.HostHwnd);
        Assert.Equal(800, result.HostPid);
        Assert.Equal("WindowsTerminal", result.HostProcessName);
        Assert.Equal("Windows Terminal", result.HostKindLabel);
        Assert.Equal(1000, result.CopilotPid);
    }

    [Fact]
    public void Resolve_FirstFocusableAncestorWins()
    {
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(900, 800, "pwsh", new IntPtr(0x123));
        var resolver = new CopilotHostResolver(tree, 0);

        var result = resolver.Resolve(1000);

        Assert.NotNull(result);
        Assert.Equal(new IntPtr(0x123), result!.HostHwnd);
        Assert.Equal(900, result.HostPid);
        Assert.Equal("pwsh", result.HostProcessName);
        Assert.Equal("PowerShell", result.HostKindLabel);
    }

    [Fact]
    public void Resolve_SkipsBoosterOwnPid()
    {
        var tree = new FakeProcessTree()
            .Add(1000, 555, "copilot", IntPtr.Zero)
            .Add(555, 800, "CopilotBooster", new IntPtr(0x111))
            .Add(800, null, "WindowsTerminal", new IntPtr(0x222));
        var resolver = new CopilotHostResolver(tree, 555);

        var result = resolver.Resolve(1000);

        Assert.NotNull(result);
        Assert.Equal(800, result!.HostPid);
        Assert.Equal("WindowsTerminal", result.HostProcessName);
        Assert.Equal(new IntPtr(0x222), result.HostHwnd);
    }

    [Fact]
    public void Resolve_NoAncestorHasWindow_ReturnsNull()
    {
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(900, 800, "intermediate", IntPtr.Zero)
            .Add(800, null, "root", IntPtr.Zero);
        var resolver = new CopilotHostResolver(tree, 0);

        var result = resolver.Resolve(1000);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_CycleDetected_ReturnsNull()
    {
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(900, 1000, "looper", IntPtr.Zero);
        var resolver = new CopilotHostResolver(tree, 0);

        var result = resolver.Resolve(1000);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_StopsAtCap()
    {
        var tree = new FakeProcessTree();
        for (int i = 1000; i < 1033; i++)
        {
            tree.Add(i, i + 1, $"proc{i}", IntPtr.Zero);
        }
        tree.Add(1033, null, "root", IntPtr.Zero);
        var resolver = new CopilotHostResolver(tree, 0);

        var result = resolver.Resolve(1000);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_UnknownProcessName_ReturnsUnknownLabel()
    {
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(900, null, "explorer", new IntPtr(0x456));
        var resolver = new CopilotHostResolver(tree, 0);

        var result = resolver.Resolve(1000);

        Assert.NotNull(result);
        Assert.Equal("explorer", result!.HostProcessName);
        Assert.Equal("Unknown", result.HostKindLabel);
    }

    [Fact]
    public void Resolve_PowerShellAncestor_LabelIsPowerShell()
    {
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(900, null, "pwsh", new IntPtr(0x789));
        var resolver = new CopilotHostResolver(tree, 0);

        var result = resolver.Resolve(1000);

        Assert.NotNull(result);
        Assert.Equal("PowerShell", result!.HostKindLabel);
    }

    [Fact]
    public void Resolve_ExceptionInProvider_ReturnsNull()
    {
        var throwingTree = new ThrowingProcessTree();
        var resolver = new CopilotHostResolver(throwingTree, 0);

        var result = resolver.Resolve(1000);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveWindowsTerminalContext_MultipleWtHwndsForOnePid_ExposesAllCandidates()
    {
        // Sun Valley wt monarch: one WindowsTerminal.exe (pid 5000) hosts TWO wt windows
        // (hwnd 0x100, hwnd 0x200). The resolver itself doesn't know which pane physically
        // owns our copilot — that requires session terms — so it must expose ALL candidate
        // wt hwnds via WindowsTerminalHostContext.CandidateHostHwnds for the tracker to
        // disambiguate downstream.
        var tree = new FakeProcessTree()
            .Add(7000, 6000, "copilot", IntPtr.Zero)
            .Add(6000, 5000, "pwsh", IntPtr.Zero)
            .Add(5000, null, "WindowsTerminal", IntPtr.Zero)
            .AddWindows(5000, new IntPtr(0x100), new IntPtr(0x200));

        var resolver = new CopilotHostResolver(tree, ownPid: 0);

        var result = resolver.ResolveWindowsTerminalContext(7000);

        Assert.NotNull(result);
        Assert.Equal(new IntPtr(0x100), result!.HostHwnd);
        Assert.Equal(new[] { new IntPtr(0x100), new IntPtr(0x200) }, result.CandidateHostHwnds);
        Assert.Equal(5000, result.HostPid);
        Assert.Equal(6000, result.PaneRootPid);
    }

    [Fact]
    public void ResolveWindowsTerminalContext_SingleWtHwnd_CandidateListHasJustOne()
    {
        var tree = new FakeProcessTree()
            .Add(7000, 6000, "copilot", IntPtr.Zero)
            .Add(6000, 5000, "pwsh", IntPtr.Zero)
            .Add(5000, null, "WindowsTerminal", IntPtr.Zero)
            .AddWindows(5000, new IntPtr(0x300));

        var resolver = new CopilotHostResolver(tree, ownPid: 0);

        var result = resolver.ResolveWindowsTerminalContext(7000);

        Assert.NotNull(result);
        Assert.Equal(new IntPtr(0x300), result!.HostHwnd);
        Assert.Equal(new[] { new IntPtr(0x300) }, result.CandidateHostHwnds);
    }

    [Fact]
    public void ResolveWindowsTerminalContext_LegacyFakeWithoutEnumerateOverride_StillWorks()
    {
        // Fakes that don't override EnumerateTopLevelWindows must still produce a valid
        // single-element CandidateHostHwnds list via the GetTopLevelWindow fallback.
        var tree = new FakeProcessTree()
            .Add(7000, 6000, "copilot", IntPtr.Zero)
            .Add(6000, 5000, "pwsh", IntPtr.Zero)
            .Add(5000, null, "WindowsTerminal", new IntPtr(0xABC));

        var resolver = new CopilotHostResolver(tree, ownPid: 0);

        var result = resolver.ResolveWindowsTerminalContext(7000);

        Assert.NotNull(result);
        Assert.Equal(new IntPtr(0xABC), result!.HostHwnd);
        Assert.Single(result.CandidateHostHwnds);
        Assert.Equal(new IntPtr(0xABC), result.CandidateHostHwnds[0]);
    }

    private sealed class ThrowingProcessTree : IProcessTreeProvider
    {
        public int? GetParentPid(int pid) => throw new InvalidOperationException("Test exception");
        public string? GetProcessName(int pid) => throw new InvalidOperationException("Test exception");
        public IntPtr GetTopLevelWindow(int pid) => throw new InvalidOperationException("Test exception");
        public IReadOnlyList<IntPtr> EnumerateTopLevelWindows(int pid) => throw new InvalidOperationException("Test exception");
    }
}
