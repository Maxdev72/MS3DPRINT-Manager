using System.Numerics;
using MS3DPRINT.Estimator;

namespace MS3DPRINT.Estimator.Tests;

public sealed class OrientationTests
{
    private static PrintMesh Block(float x, float y, float z)
    {
        Vector3[] corners = [new(0, 0, 0), new(x, 0, 0), new(x, y, 0), new(0, y, 0), new(0, 0, z), new(x, 0, z), new(x, y, z), new(0, y, z)];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        return new PrintMesh(corners, faces);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"attendu {expected}, obtenu {actual}");

    [Fact]
    public void BetweenTakesOneDirectionOntoTheOther()
    {
        var turn = OrientationMath.Between(Vector3.UnitX, -Vector3.UnitZ);

        AssertNear(-Vector3.UnitZ, Vector3.Transform(Vector3.UnitX, turn));
    }

    [Fact]
    public void BetweenTwoEqualDirectionsDoesNothing() =>
        Assert.Equal(Quaternion.Identity, OrientationMath.Between(new Vector3(0, 0, -3), -Vector3.UnitZ));

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    public void BetweenOppositeDirectionsIsAHalfTurn(float x, float y, float z)
    {
        var from = new Vector3(x, y, z);

        var turn = OrientationMath.Between(from, -from);

        AssertNear(-from, Vector3.Transform(from, turn));
    }

    [Fact]
    public void OrientedStandsTheMeshOnTheBedAndCentresIt()
    {
        var mesh = Block(10, 20, 30);

        var stood = mesh.Oriented(Quaternion.Identity);

        Assert.Equal(0, stood.Min.Z, 4);
        Assert.Equal(0, (stood.Min.X + stood.Max.X) / 2, 4);
        Assert.Equal(0, (stood.Min.Y + stood.Max.Y) / 2, 4);
        Assert.Equal(new Vector3(10, 20, 30), stood.Size);
    }

    [Fact]
    public void OrientedTurnsAboutTheCentreThenSitsOnTheBed()
    {
        var mesh = Block(10, 20, 30);

        var lying = mesh.Oriented(Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));

        Assert.Equal(10, lying.Size.X, 3);
        Assert.Equal(30, lying.Size.Y, 3);
        Assert.Equal(20, lying.Size.Z, 3);
        Assert.Equal(0, lying.Min.Z, 4);
    }

    [Fact]
    public void OrientedKeepsTheTrianglesAndLeavesTheSourceUntouched()
    {
        var mesh = Block(10, 20, 30);
        var before = mesh.Positions[6];

        var turned = mesh.Oriented(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f));

        Assert.Same(mesh.Indices, turned.Indices);
        Assert.Equal(before, mesh.Positions[6]);
    }

    [Fact]
    public void OrientedOfAnEmptyMeshIsTheSameMesh()
    {
        var empty = new PrintMesh([], []);

        Assert.Same(empty, empty.Oriented(Quaternion.Identity));
    }
}
