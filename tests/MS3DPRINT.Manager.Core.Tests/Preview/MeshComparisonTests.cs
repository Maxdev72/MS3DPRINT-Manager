using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class MeshComparisonTests
{
    private static readonly int[] CubeIndices =
    [
        0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
        1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7
    ];

    private static MeshModel Box(float x, float y, float z, Vector3 origin = default) => new(
    [
        origin, origin + new Vector3(x, 0, 0), origin + new Vector3(x, y, 0), origin + new Vector3(0, y, 0),
        origin + new Vector3(0, 0, z), origin + new Vector3(x, 0, z), origin + new Vector3(x, y, z), origin + new Vector3(0, y, z)
    ], CubeIndices);

    [Fact]
    public void Compare_ReportsHowTheSecondVersionDiffers()
    {
        var first = Box(10, 10, 10);
        var second = Box(12, 10, 5);

        var rows = MeshComparison.Compare(first, second, MeshVolume.Compute(first), MeshVolume.Compute(second), 1);

        Assert.Equal("+2 mm", rows[0].Difference);          // width 10 → 12
        Assert.Equal("identique", rows[1].Difference);      // depth unchanged
        Assert.Equal("−5 mm", rows[2].Difference);          // height 10 → 5
        Assert.Equal("1 cm³", rows[3].First);               // 1000 mm³
        Assert.Equal("0,6 cm³", rows[3].Second);            // 600 mm³
        Assert.Contains("−0,4 cm³", rows[3].Difference);
        Assert.Contains("−40 %", rows[3].Difference);
        Assert.Equal("identique", rows[5].Difference);      // same triangle count
    }

    [Fact]
    public void Compare_UsesTheModelUnit()
    {
        var first = Box(1, 1, 1);
        var second = Box(2, 1, 1);

        var rows = MeshComparison.Compare(first, second, MeshVolume.Compute(first), MeshVolume.Compute(second), 25.4);

        Assert.Equal("25,4 mm", rows[0].First);
        Assert.Equal("+25,4 mm", rows[0].Difference);
    }

    [Fact]
    public void CentreOffset_MovesTheSecondCentreOntoTheFirst()
    {
        var first = Box(10, 10, 10);
        var second = Box(10, 10, 10, new Vector3(100, 0, -20));

        Assert.Equal(new Vector3(-100, 0, 20), MeshComparison.CentreOffset(first, second));
    }

    [Theory]
    [InlineData(0.0, "identique")]
    [InlineData(0.004, "identique")]
    [InlineData(2.5, "+2,5 mm")]
    [InlineData(-1.25, "−1,25 mm")]
    public void Signed_UsesATrueMinusAndHidesNoise(double value, string expected)
        => Assert.Equal(expected, MeshComparison.Signed(value, 2, " mm"));
}
