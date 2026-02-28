public sealed class ToastPositionTests
{
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1040); // 1080p minus taskbar
    private static readonly Size WindowSize = new(1000, 550);

    [Theory]
    [InlineData("bottom-center")]
    [InlineData("bottom-left")]
    [InlineData("bottom-right")]
    public void BottomPositions_TargetAboveTaskbar(string position)
    {
        var (target, _, fromBottom) = MainForm.CalculateToastPosition(WorkArea, WindowSize, position);

        Assert.True(fromBottom);
        Assert.Equal(WorkArea.Bottom - WindowSize.Height, target.Y);
    }

    [Theory]
    [InlineData("top-center")]
    [InlineData("top-left")]
    [InlineData("top-right")]
    public void TopPositions_TargetAtTop(string position)
    {
        var (target, _, fromBottom) = MainForm.CalculateToastPosition(WorkArea, WindowSize, position);

        Assert.False(fromBottom);
        Assert.Equal(WorkArea.Top, target.Y);
    }

    [Fact]
    public void BottomCenter_HorizontallyCentered()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(WorkArea, WindowSize, "bottom-center");

        int expectedLeft = (WorkArea.Width - WindowSize.Width) / 2;
        Assert.Equal(expectedLeft, target.X);
    }

    [Fact]
    public void BottomLeft_AlignedLeft()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(WorkArea, WindowSize, "bottom-left");

        Assert.Equal(WorkArea.Left, target.X);
    }

    [Fact]
    public void BottomRight_AlignedRight()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(WorkArea, WindowSize, "bottom-right");

        Assert.Equal(WorkArea.Right - WindowSize.Width, target.X);
    }

    [Fact]
    public void TopCenter_HorizontallyCentered()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(WorkArea, WindowSize, "top-center");

        int expectedLeft = (WorkArea.Width - WindowSize.Width) / 2;
        Assert.Equal(expectedLeft, target.X);
    }

    [Fact]
    public void TopLeft_AlignedLeft()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(WorkArea, WindowSize, "top-left");

        Assert.Equal(WorkArea.Left, target.X);
    }

    [Fact]
    public void TopRight_AlignedRight()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(WorkArea, WindowSize, "top-right");

        Assert.Equal(WorkArea.Right - WindowSize.Width, target.X);
    }

    [Fact]
    public void BottomCenter_AnimStartBelowScreen()
    {
        var (_, animStart, _) = MainForm.CalculateToastPosition(WorkArea, WindowSize, "bottom-center");

        // Animation starts at the very bottom of the work area (window fully off-screen)
        Assert.Equal(WorkArea.Bottom, animStart.Y);
    }

    [Fact]
    public void TopCenter_AnimStartAboveScreen()
    {
        var (_, animStart, _) = MainForm.CalculateToastPosition(WorkArea, WindowSize, "top-center");

        // Animation starts above the work area (window fully off-screen)
        Assert.Equal(WorkArea.Top - WindowSize.Height, animStart.Y);
    }

    [Fact]
    public void AnimStart_SameXAsTarget()
    {
        var (target, animStart, _) = MainForm.CalculateToastPosition(WorkArea, WindowSize, "bottom-center");

        Assert.Equal(target.X, animStart.X);
    }

    [Fact]
    public void MultiMonitor_OffsetWorkArea_PositionsCorrectly()
    {
        // Secondary monitor at X=1920
        var secondaryWorkArea = new Rectangle(1920, 0, 2560, 1400);

        var (target, _, _) = MainForm.CalculateToastPosition(secondaryWorkArea, WindowSize, "bottom-center");

        int expectedLeft = 1920 + (2560 - WindowSize.Width) / 2;
        Assert.Equal(expectedLeft, target.X);
        Assert.Equal(1400 - WindowSize.Height, target.Y);
    }

    [Fact]
    public void UnknownPosition_DefaultsToCentered()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(WorkArea, WindowSize, "invalid-value");

        // Unknown position: centered horizontally, positioned at top (doesn't start with "bottom")
        int expectedLeft = (WorkArea.Width - WindowSize.Width) / 2;
        Assert.Equal(expectedLeft, target.X);
        Assert.Equal(WorkArea.Top, target.Y);
    }
}
