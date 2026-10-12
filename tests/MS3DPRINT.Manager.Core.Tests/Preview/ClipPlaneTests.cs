using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class ClipPlaneTests
{
    private static readonly Vector3[] Corners =
    [
        new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0),
        new(0, 0, 10), new(10, 0, 10), new(10, 10, 10), new(0, 10, 10)
    ];

    private static readonly int[] CubeIndices =
    [
        0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
        1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7
    ];

    [Theory]
    [InlineData(2, 5, false, 7, true)]   // keep z <= 5: a point at z = 7 is cut away
    [InlineData(2, 5, false, 3, false)]
    [InlineData(2, 5, true, 3, true)]    // flipped: keep z >= 5
    [InlineData(0, 5, false, 6, true)]
    [InlineData(1, 5, false, 6, true)]
    public void Clips_TheSideBeyondThePlane(int axis, float offset, bool flip, float coordinate, bool expected)
    {
        var point = axis == 0 ? new Vector3(coordinate, 0, 0) : axis == 1 ? new Vector3(0, coordinate, 0) : new Vector3(0, 0, coordinate);

        Assert.Equal(expected, new ClipPlane(axis, offset, flip).Clips(point));
    }

    [Fact]
    public void Render_DrawsLessOfTheModelWhenItIsCut()
    {
        var mesh = new MeshModel(Corners, CubeIndices);
        var camera = new ViewCamera();
        var whole = new RenderOptions(96, 96, false, 0xFFFFFFFF, 0xFF000000);
        var cut = whole with { Clip = new ClipPlane(2, 0.5f, false) };

        var wholePixels = SoftwareRenderer.Render(mesh, camera, whole).Count(pixel => pixel != 0xFF000000);
        var cutPixels = SoftwareRenderer.Render(mesh, camera, cut).Count(pixel => pixel != 0xFF000000);

        Assert.True(cutPixels < wholePixels);
    }

    [Fact]
    public void Pick_IgnoresTrianglesOnTheCutAwaySide()
    {
        var mesh = new MeshModel(Corners, CubeIndices);
        var (origin, direction) = ViewMath.Ray(mesh, new ViewCamera { Yaw = 0, Pitch = 0 }, 800, 600, 400, 300);

        Assert.True(MeshPicker.TryPick(mesh, origin, direction, out var hit, out _));
        // cutting everything above z = -1 removes every triangle: nothing left to pick
        Assert.False(MeshPicker.TryPick(mesh, origin, direction, out _, out _, new ClipPlane(2, -1, false)));
        Assert.Equal(mesh.Min.Y, hit.Y, 3);
    }

    [Fact]
    public void Compute_FlagsAMeshWhoseTrianglesAreInsideOut()
    {
        var flipped = new int[CubeIndices.Length];
        for (var i = 0; i < CubeIndices.Length; i += 3) { flipped[i] = CubeIndices[i]; flipped[i + 1] = CubeIndices[i + 2]; flipped[i + 2] = CubeIndices[i + 1]; }

        Assert.False(MeshVolume.Compute(new MeshModel(Corners, CubeIndices)).Inverted);
        Assert.True(MeshVolume.Compute(new MeshModel(Corners, flipped)).Inverted);
    }
}
