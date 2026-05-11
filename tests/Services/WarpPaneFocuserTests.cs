namespace CopilotBooster.Tests.Services;

public sealed class WarpPaneFocuserTests
{
    [Fact]
    public void TryFocusPane_NoMainWindow_ReturnsFalse()
    {
        var titleReader = new StubTitleReader(hwndToReturn: IntPtr.Zero, titleSequence: []);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();
        var focusHwndCalls = new List<(IntPtr hwnd, bool result)>();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            hwnd => { focusHwndCalls.Add((hwnd, true)); return true; }
        );

        var result = focuser.TryFocusPane(12345, "Hi 1");

        Assert.False(result);
        Assert.Equal(0, keys.SendNextTabCallCount);
        Assert.Empty(focusHwndCalls);
    }

    [Fact]
    public void TryFocusPane_AlreadyOnTargetTab_ReturnsTrueWithoutCycling()
    {
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();
        var focusHwndCalls = new List<(IntPtr hwnd, bool result)>();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => { focusHwndCalls.Add((h, true)); return true; }
        );

        var result = focuser.TryFocusPane(12345, "Hi 1");

        Assert.True(result);
        Assert.Equal(0, keys.SendNextTabCallCount);
        Assert.Single(focusHwndCalls);
        Assert.Equal(hwnd, focusHwndCalls[0].hwnd);
    }

    [Fact]
    public void TryFocusPane_TitleMatchIsCaseInsensitive()
    {
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => true
        );

        var result = focuser.TryFocusPane(12345, "hi 1");

        Assert.True(result);
        Assert.Equal(0, keys.SendNextTabCallCount);
    }

    [Fact]
    public void TryFocusPane_MatchOnSecondTab_StopsAfterOneCycle()
    {
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1", "Hi 2"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => true
        );

        var result = focuser.TryFocusPane(12345, "Hi 2");

        Assert.True(result);
        Assert.Equal(1, keys.SendNextTabCallCount);
        Assert.Single(clock.SleepDurations);
        Assert.Equal(150, clock.SleepDurations[0]);
    }

    [Fact]
    public void TryFocusPane_MatchOnThirdTab_StopsAfterTwoCycles()
    {
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1", "Hi 2", "Hi 3"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => true
        );

        var result = focuser.TryFocusPane(12345, "Hi 3");

        Assert.True(result);
        Assert.Equal(2, keys.SendNextTabCallCount);
    }

    [Fact]
    public void TryFocusPane_NoMatch_CyclesBackToOriginal_ReturnsFalse()
    {
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1", "Hi 2", "Hi 3", "Hi 1"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => true
        );

        var result = focuser.TryFocusPane(12345, "Missing");

        Assert.False(result);
        Assert.Equal(3, keys.SendNextTabCallCount);
    }

    [Fact]
    public void TryFocusPane_HitsIterationCap_ReturnsFalse()
    {
        var hwnd = (IntPtr)0x1234;
        var titles = Enumerable.Range(1, 50).Select(i => $"Tab{i}").ToArray();
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: titles);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => true,
            maxIterations: 30
        );

        var result = focuser.TryFocusPane(12345, "Missing");

        Assert.False(result);
        Assert.Equal(30, keys.SendNextTabCallCount);
    }

    [Fact]
    public void TryFocusPane_DuplicateTitlesAcrossPanes_FirstMatchWins()
    {
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Tab A", "pwsh", "pwsh"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => true
        );

        var result = focuser.TryFocusPane(12345, "pwsh");

        Assert.True(result);
        Assert.Equal(1, keys.SendNextTabCallCount);
    }

    [Fact]
    public void TryFocusPane_EmptyExpectedTitle_ReturnsFalseImmediately()
    {
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => true
        );

        var result = focuser.TryFocusPane(12345, "");

        Assert.False(result);
        Assert.Equal(0, keys.SendNextTabCallCount);
    }

    [Fact]
    public void TryFocusPane_NullExpectedTitle_ReturnsFalseImmediately()
    {
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => true
        );

        var result = focuser.TryFocusPane(12345, null!);

        Assert.False(result);
        Assert.Equal(0, keys.SendNextTabCallCount);
    }

    [Fact]
    public void TryFocusPane_FocusHwndFails_ReturnsFalseImmediately()
    {
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1", "Hi 2"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => false
        );

        var result = focuser.TryFocusPane(12345, "Hi 2");

        Assert.False(result);
        Assert.Equal(0, keys.SendNextTabCallCount);
    }

    [Fact]
    public void TryFocusPane_TitleReaderReturnsEmptyDuringProbe_TreatedAsCycleBreak()
    {
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1", "", "Hi 1"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => true
        );

        var result = focuser.TryFocusPane(12345, "Missing");

        Assert.False(result);
        Assert.Equal(2, keys.SendNextTabCallCount);
    }

    [Fact]
    public void TryFocusPane_WarmupSleep_AppliedAfterFocusBeforeFirstCtrlTab()
    {
        // Regression: Without a warmup, SetForegroundWindow → SendInput races
        // because focus transfer is async on Windows. The Ctrl+Tab keystroke
        // can land on Booster instead of Warp. This test asserts the warmup
        // sleep happens between focusHwnd and the first SendNextTab.
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1", "Hi 2"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();
        var sleepCallsBeforeFirstKey = 0;

        var keysWithProbe = new ProbeKeyboardSender(() => sleepCallsBeforeFirstKey = clock.SleepDurations.Count);

        var focuser = new WarpPaneFocuser(
            titleReader,
            keysWithProbe,
            clock,
            h => true,
            warmupMillis: 75);

        var result = focuser.TryFocusPane(12345, "Hi 2");

        Assert.True(result);
        Assert.Equal(1, keysWithProbe.SendNextTabCallCount);
        // First sleep entry should be the 75ms warmup, captured BEFORE first SendNextTab.
        Assert.True(sleepCallsBeforeFirstKey >= 1, "warmup sleep must run before first Ctrl+Tab");
        Assert.Equal(75, clock.SleepDurations[0]);
    }

    [Fact]
    public void TryFocusPane_WarmupZero_NoExtraSleepBeforeFirstCtrlTab()
    {
        // Default behavior: when warmupMillis is omitted (0), no extra pre-loop
        // sleep is performed. Existing tests rely on this.
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1", "Hi 2"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => true);

        var result = focuser.TryFocusPane(12345, "Hi 2");

        Assert.True(result);
        // Only the 150ms iteration sleep should be recorded, not a warmup.
        Assert.Single(clock.SleepDurations);
        Assert.Equal(150, clock.SleepDurations[0]);
    }

    [Fact]
    public void TryFocusPane_FocusHwndFails_NoWarmupSleep()
    {
        // When focusHwnd returns false (window couldn't be foregrounded, or
        // foreground confirmation timed out), we must bail BEFORE warmup —
        // otherwise we burn 100ms doing nothing.
        var hwnd = (IntPtr)0x1234;
        var titleReader = new StubTitleReader(hwndToReturn: hwnd, titleSequence: ["Hi 1"]);
        var keys = new StubKeyboardSender();
        var clock = new StubPaneFocusClock();

        var focuser = new WarpPaneFocuser(
            titleReader,
            keys,
            clock,
            h => false,
            warmupMillis: 100);

        var result = focuser.TryFocusPane(12345, "Hi 2");

        Assert.False(result);
        Assert.Empty(clock.SleepDurations);
        Assert.Equal(0, keys.SendNextTabCallCount);
    }

    private sealed class StubTitleReader : IWindowTitleReader
    {
        private readonly IntPtr _hwndToReturn;
        private readonly Queue<string> _titleQueue;

        public StubTitleReader(IntPtr hwndToReturn, string[] titleSequence)
        {
            this._hwndToReturn = hwndToReturn;
            this._titleQueue = new Queue<string>(titleSequence);
        }

        public IntPtr FindMainWindowHandle(int processId)
        {
            return this._hwndToReturn;
        }

        public string ReadTitle(IntPtr hwnd)
        {
            return this._titleQueue.Count > 0 ? this._titleQueue.Dequeue() : "";
        }
    }

    private sealed class StubKeyboardSender : IKeyboardSender
    {
        public int SendNextTabCallCount { get; private set; }

        public void SendNextTab()
        {
            this.SendNextTabCallCount++;
        }
    }

    private sealed class ProbeKeyboardSender : IKeyboardSender
    {
        private readonly Action _onFirstSend;

        public ProbeKeyboardSender(Action onFirstSend)
        {
            this._onFirstSend = onFirstSend;
        }

        public int SendNextTabCallCount { get; private set; }

        public void SendNextTab()
        {
            if (this.SendNextTabCallCount == 0)
            {
                this._onFirstSend();
            }

            this.SendNextTabCallCount++;
        }
    }

    private sealed class StubPaneFocusClock : IPaneFocusClock
    {
        public List<int> SleepDurations { get; } = [];

        public void Sleep(int millis)
        {
            this.SleepDurations.Add(millis);
        }
    }
}
