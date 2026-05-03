using System.Diagnostics;

namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Integration tests proving that internally-launched sessions resolve a Copilot Host
/// that matches the title-pattern resolution from pre-0.21.0 behavior.
/// Uses real process trees where possible; documents seams that need Trinity's API changes.
/// </summary>
public sealed class InternalSessionHostResolutionIntegrationTests : IDisposable
{
    private Process? _testProcess;

    public void Dispose()
    {
        if (this._testProcess != null && !this._testProcess.HasExited)
        {
            try { this._testProcess.Kill(entireProcessTree: true); } catch { }
        }
        this._testProcess?.Dispose();
    }

    [Fact]
    public void Resolver_RealProcessTree_ResolvesAncestorWithWindow()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command Start-Sleep 30",
            CreateNoWindow = false,
            UseShellExecute = false
        };

        this._testProcess = Process.Start(startInfo);
        Assert.NotNull(this._testProcess);

        Thread.Sleep(500);

        var resolver = new CopilotHostResolver();
        var result = resolver.Resolve(this._testProcess.Id);

        Assert.NotNull(result);
        Assert.True(result!.HostPid > 0);
        Assert.NotEqual(IntPtr.Zero, result.HostHwnd);
        Assert.Equal(this._testProcess.Id, result.CopilotPid);
    }

    [Fact]
    public void HostResolver_SkipsBoosterOwnPid()
    {
        var ownPid = Process.GetCurrentProcess().Id;
        _ = new CopilotHostResolver();

        var fakeTree = new FakeProcessTreeForIntegration()
            .Add(1000, ownPid, "copilot", IntPtr.Zero)
            .Add(ownPid, 800, "CopilotBooster", new IntPtr(0x111))
            .Add(800, null, "WindowsTerminal", new IntPtr(0x222));

        var testResolver = new CopilotHostResolver(fakeTree, ownPid);
        var result = testResolver.Resolve(1000);

        Assert.NotNull(result);
        Assert.Equal(800, result!.HostPid);
        Assert.Equal(new IntPtr(0x222), result.HostHwnd);
    }

    [Fact]
    public void HostResolver_DeepNesting_FindsFocusableAncestor()
    {
        var fakeTree = new FakeProcessTreeForIntegration()
            .Add(1000, 1001, "copilot", IntPtr.Zero)
            .Add(1001, 1002, "node", IntPtr.Zero)
            .Add(1002, 1003, "intermediate", IntPtr.Zero)
            .Add(1003, null, "pwsh", new IntPtr(0x123));

        var resolver = new CopilotHostResolver(fakeTree, 0);
        var result = resolver.Resolve(1000);

        Assert.NotNull(result);
        Assert.Equal(1003, result!.HostPid);
        Assert.Equal("pwsh", result.HostProcessName);
        Assert.Equal("PowerShell", result.HostKindLabel);
    }

    [Fact]
    public void HostResolver_ConsoleHostWindow_ClassifiedAsConsole()
    {
        var fakeTree = new FakeProcessTreeForIntegration()
            .Add(1000, 1001, "copilot", IntPtr.Zero)
            .Add(1001, null, "conhost", new IntPtr(0x456));

        var resolver = new CopilotHostResolver(fakeTree, 0);
        var result = resolver.Resolve(1000);

        Assert.NotNull(result);
        Assert.Equal("conhost", result!.HostProcessName);
        Assert.Equal("Console", result.HostKindLabel);
    }

    private sealed class FakeProcessTreeForIntegration : IProcessTreeProvider
    {
        private readonly Dictionary<int, int?> _parents = [];
        private readonly Dictionary<int, string?> _names = [];
        private readonly Dictionary<int, IntPtr> _windows = [];

        internal FakeProcessTreeForIntegration Add(int pid, int? parentPid, string? name, IntPtr window)
        {
            this._parents[pid] = parentPid;
            this._names[pid] = name;
            this._windows[pid] = window;
            return this;
        }

        public int? GetParentPid(int pid) => this._parents.TryGetValue(pid, out var p) ? p : null;
        public string? GetProcessName(int pid) => this._names.TryGetValue(pid, out var n) ? n : null;
        public IntPtr GetTopLevelWindow(int pid) => this._windows.TryGetValue(pid, out var w) ? w : IntPtr.Zero;
    }
}
