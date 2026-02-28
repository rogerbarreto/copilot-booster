public sealed class ToastPositionTests
{
    private static readonly Rectangle s_workArea = new(0, 0, 1920, 1040); // 1080p minus taskbar
    private static readonly Size s_windowSize = new(1000, 550);

    [Theory]
    [InlineData("bottom-center")]
    [InlineData("bottom-left")]
    [InlineData("bottom-right")]
    public void BottomPositions_TargetAboveTaskbar(string position)
    {
        var (target, _, fromBottom) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, position);

        Assert.True(fromBottom);
        Assert.Equal(s_workArea.Bottom - s_windowSize.Height, target.Y);
    }

    [Theory]
    [InlineData("top-center")]
    [InlineData("top-left")]
    [InlineData("top-right")]
    public void TopPositions_TargetAtTop(string position)
    {
        var (target, _, fromBottom) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, position);

        Assert.False(fromBottom);
        Assert.Equal(s_workArea.Top, target.Y);
    }

    [Fact]
    public void BottomCenter_HorizontallyCentered()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, "bottom-center");

        int expectedLeft = (s_workArea.Width - s_windowSize.Width) / 2;
        Assert.Equal(expectedLeft, target.X);
    }

    [Fact]
    public void BottomLeft_AlignedLeft()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, "bottom-left");

        Assert.Equal(s_workArea.Left, target.X);
    }

    [Fact]
    public void BottomRight_AlignedRight()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, "bottom-right");

        Assert.Equal(s_workArea.Right - s_windowSize.Width, target.X);
    }

    [Fact]
    public void TopCenter_HorizontallyCentered()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, "top-center");

        int expectedLeft = (s_workArea.Width - s_windowSize.Width) / 2;
        Assert.Equal(expectedLeft, target.X);
    }

    [Fact]
    public void TopLeft_AlignedLeft()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, "top-left");

        Assert.Equal(s_workArea.Left, target.X);
    }

    [Fact]
    public void TopRight_AlignedRight()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, "top-right");

        Assert.Equal(s_workArea.Right - s_windowSize.Width, target.X);
    }

    [Fact]
    public void BottomCenter_AnimStartBelowScreen()
    {
        var (_, animStart, _) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, "bottom-center");

        // Animation starts at the very bottom of the work area (window fully off-screen)
        Assert.Equal(s_workArea.Bottom, animStart.Y);
    }

    [Fact]
    public void TopCenter_AnimStartAboveScreen()
    {
        var (_, animStart, _) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, "top-center");

        // Animation starts above the work area (window fully off-screen)
        Assert.Equal(s_workArea.Top - s_windowSize.Height, animStart.Y);
    }

    [Fact]
    public void AnimStart_SameXAsTarget()
    {
        var (target, animStart, _) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, "bottom-center");

        Assert.Equal(target.X, animStart.X);
    }

    [Fact]
    public void MultiMonitor_OffsetWorkArea_PositionsCorrectly()
    {
        // Secondary monitor at X=1920
        var secondaryWorkArea = new Rectangle(1920, 0, 2560, 1400);

        var (target, _, _) = MainForm.CalculateToastPosition(secondaryWorkArea, s_windowSize, "bottom-center");

        int expectedLeft = 1920 + (2560 - s_windowSize.Width) / 2;
        Assert.Equal(expectedLeft, target.X);
        Assert.Equal(1400 - s_windowSize.Height, target.Y);
    }

    [Fact]
    public void UnknownPosition_DefaultsToCentered()
    {
        var (target, _, _) = MainForm.CalculateToastPosition(s_workArea, s_windowSize, "invalid-value");

        // Unknown position: centered horizontally, positioned at top (doesn't start with "bottom")
        int expectedLeft = (s_workArea.Width - s_windowSize.Width) / 2;
        Assert.Equal(expectedLeft, target.X);
        Assert.Equal(s_workArea.Top, target.Y);
    }
}
