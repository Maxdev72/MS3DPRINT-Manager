using System.Windows;
using MS3DPRINT.Manager.App;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class WindowLaunchSizeTests
{
    [Fact]
    public void ForMainWindow_UsesTheComfortableMaximumOnALargeWorkArea()
    {
        var size = WindowLaunchSize.ForMainWindow(new Size(1920, 1040));

        Assert.Equal(new Size(1080, 700), size);
    }

    [Fact]
    public void ForMainWindow_StaysInsideNinetyPercentOfASmallWorkArea()
    {
        var size = WindowLaunchSize.ForMainWindow(new Size(1000, 700));

        Assert.Equal(new Size(900, 630), size);
    }

    [Fact]
    public void ForMainWindow_ConstrainsASavedSizeToTheAvailableWorkArea()
    {
        var size = WindowLaunchSize.ForMainWindow(new Size(1000, 700), new Size(1400, 900));

        Assert.Equal(new Size(900, 630), size);
    }

    [Fact]
    public void ForMainWindow_RejectsAnUnavailableWorkArea()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowLaunchSize.ForMainWindow(new Size(0, 700)));
    }
}
