using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class ViewCameraTests
{
    [Fact]
    public void ZoomingAroundTheCursorKeepsThePointUnderTheCursor()
    {
        var camera = new ViewCamera();

        // 1000 × 760 view, cursor 100 px right of the centre: the spot there must still be there after zooming twice as close
        camera.ZoomAt(2, 600, 380, 1000, 760);

        Assert.Equal(2, camera.Zoom, 9);
        Assert.Equal(-100, camera.PanX, 9);
        Assert.Equal(0, camera.PanY, 9);
    }

    [Fact]
    public void ZoomingAtTheCentreDoesNotMoveTheView()
    {
        var camera = new ViewCamera { PanX = 0, PanY = 0 };

        camera.ZoomAt(3, 500, 380, 1000, 760);

        Assert.Equal(0, camera.PanX, 9);
        Assert.Equal(0, camera.PanY, 9);
    }

    [Fact]
    public void ThereIsNoPracticalLimitToZoomingInOrOut()
    {
        var camera = new ViewCamera();

        for (var i = 0; i < 80; i++) camera.ZoomAt(1.12, 500, 380, 1000, 760);
        var deep = camera.Zoom;
        for (var i = 0; i < 200; i++) camera.ZoomAt(1 / 1.12, 500, 380, 1000, 760);

        Assert.True(deep > 1000);
        Assert.True(camera.Zoom < 1e-3);
        Assert.True(camera.Zoom > 0);
    }

    [Fact]
    public void TheZoomStaysFiniteWhateverTheWheelDoes()
    {
        var camera = new ViewCamera();

        camera.ZoomAt(1e300, 10, 10, 1000, 760);
        Assert.Equal(ViewCamera.MaxZoom, camera.Zoom);
        camera.ZoomAt(1e-300, 10, 10, 1000, 760);
        Assert.Equal(ViewCamera.MinZoom, camera.Zoom);
        Assert.True(double.IsFinite(camera.PanX) && double.IsFinite(camera.PanY));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonsenseFactorChangesNothing(double factor)
    {
        var camera = new ViewCamera { Zoom = 2, PanX = 5, PanY = 7 };

        camera.ZoomAt(factor, 100, 100, 1000, 760);

        Assert.Equal(2, camera.Zoom);
        Assert.Equal(5, camera.PanX);
        Assert.Equal(7, camera.PanY);
    }
}
