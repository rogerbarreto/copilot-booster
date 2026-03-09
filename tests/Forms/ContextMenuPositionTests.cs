using System.Drawing;
using CopilotBooster.Forms;

public sealed class ContextMenuPositionTests
{
    private static readonly Rectangle s_primaryScreen = new(0, 0, 1920, 1040);
    private static readonly Rectangle s_secondaryScreen = new(1920, 0, 2560, 1440);
    private static readonly Size s_menuSize = new(200, 150);

    [Fact]
    public void MenuFitsEntirely_PositionUnchanged()
    {
        var origin = new Point(500, 500);
        var result = ContextMenuHelper.ClampToScreen(origin, s_menuSize, s_primaryScreen);
        Assert.Equal(origin, result);
    }

    [Fact]
    public void MenuOverflowsRight_ClampsToRightEdge()
    {
        var origin = new Point(1800, 500); // 1800 + 200 = 2000 > 1920
        var result = ContextMenuHelper.ClampToScreen(origin, s_menuSize, s_primaryScreen);
        Assert.Equal(1920 - 200, result.X);
        Assert.Equal(500, result.Y);
    }

    [Fact]
    public void MenuOverflowsBottom_ClampsToBottomEdge()
    {
        var origin = new Point(500, 950); // 950 + 150 = 1100 > 1040
        var result = ContextMenuHelper.ClampToScreen(origin, s_menuSize, s_primaryScreen);
        Assert.Equal(500, result.X);
        Assert.Equal(1040 - 150, result.Y);
    }

    [Fact]
    public void MenuOverflowsRightAndBottom_ClampsBoth()
    {
        var origin = new Point(1800, 950);
        var result = ContextMenuHelper.ClampToScreen(origin, s_menuSize, s_primaryScreen);
        Assert.Equal(1920 - 200, result.X);
        Assert.Equal(1040 - 150, result.Y);
    }

    [Fact]
    public void SecondaryMonitor_ClampsWithinSecondaryBounds()
    {
        // Point near the right edge of the secondary monitor
        var origin = new Point(1920 + 2400, 500); // 4320 + 200 = 4520 > 4480 (1920+2560)
        var result = ContextMenuHelper.ClampToScreen(origin, s_menuSize, s_secondaryScreen);
        Assert.Equal(1920 + 2560 - 200, result.X);
        Assert.Equal(500, result.Y);
    }

    [Fact]
    public void PointAtExactRightEdge_ClampsLeft()
    {
        var origin = new Point(1920, 500);
        var result = ContextMenuHelper.ClampToScreen(origin, s_menuSize, s_primaryScreen);
        Assert.Equal(1920 - 200, result.X);
    }

    [Fact]
    public void PointAtExactBottomEdge_ClampsUp()
    {
        var origin = new Point(500, 1040);
        var result = ContextMenuHelper.ClampToScreen(origin, s_menuSize, s_primaryScreen);
        Assert.Equal(1040 - 150, result.Y);
    }

    [Fact]
    public void NegativeScreenOrigin_ClampsCorrectly()
    {
        // Monitor to the left of primary: (-1920, 0) to (0, 1080)
        var leftScreen = new Rectangle(-1920, 0, 1920, 1080);
        var origin = new Point(-100, 500); // -100 + 200 = 100 > 0 (right edge)
        var result = ContextMenuHelper.ClampToScreen(origin, s_menuSize, leftScreen);
        Assert.Equal(-200, result.X); // 0 - 200 = -200
        Assert.Equal(500, result.Y);
    }

    [Fact]
    public void MenuLargerThanScreen_ClampsToTopLeft()
    {
        var tinyScreen = new Rectangle(0, 0, 100, 100);
        var origin = new Point(50, 50);
        var result = ContextMenuHelper.ClampToScreen(origin, s_menuSize, tinyScreen);
        Assert.Equal(0, result.X);
        Assert.Equal(0, result.Y);
    }
}
