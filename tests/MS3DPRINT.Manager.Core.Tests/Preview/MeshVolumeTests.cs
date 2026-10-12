using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class MeshVolumeTests
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

    [Fact]
    public void Compute_ReturnsVolumeAndAreaOfAClosedCube()
    {
        var measures = MeshVolume.Compute(new MeshModel(Corners, CubeIndices));

        Assert.Equal(1000, measures.Volume, 3);
        Assert.Equal(600, measures.SurfaceArea, 3);
        Assert.True(measures.IsClosed);
    }

    [Fact]
    public void Compute_WeldsTheCornersOfAnStlStyleMesh()
    {
        // every triangle owns its three corners, as in an STL file
        var positions = CubeIndices.Select(index => Corners[index]).ToArray();
        var indices = Enumerable.Range(0, positions.Length).ToArray();

        var measures = MeshVolume.Compute(new MeshModel(positions, indices));

        Assert.Equal(1000, measures.Volume, 3);
        Assert.True(measures.IsClosed);
    }

    [Fact]
    public void Compute_ReportsAnOpenMeshWhenATriangleIsMissing()
    {
        var measures = MeshVolume.Compute(new MeshModel(Corners, CubeIndices[..^3]));

        Assert.False(measures.IsClosed);
    }

    [Fact]
    public void Compute_GivesTheSameVolumeWhateverTheOrientation()
    {
        var flipped = new int[CubeIndices.Length];
        for (var i = 0; i < CubeIndices.Length; i += 3) { flipped[i] = CubeIndices[i]; flipped[i + 1] = CubeIndices[i + 2]; flipped[i + 2] = CubeIndices[i + 1]; }

        Assert.Equal(1000, MeshVolume.Compute(new MeshModel(Corners, flipped)).Volume, 3);
    }

    [Theory]
    [InlineData("PLA+", 1.24)]
    [InlineData("PETG-CF", 1.27)]
    [InlineData("Nylon", 1.14)]
    [InlineData("inconnu", 1.24)]
    public void DensityFor_RecognisesCommonMaterials(string material, double expected)
        => Assert.Equal(expected, MeshVolume.DensityFor(material));

    [Fact]
    public void EstimateMass_PrintsTheShellSolidAndTheCoreAtTheInfillRate()
    {
        // 1000 cm³, 600 cm² of surface, 1 mm walls: 60 cm³ of shell, 940 cm³ of core filled at 10 %
        var mass = MeshVolume.EstimateMass(1000, 600, density: 1.0, infillPercent: 10, wallMm: 1);

        Assert.Equal(60 + 94, mass, 6);
    }

    [Fact]
    public void EstimateMass_NeverExceedsTheSolidMass()
    {
        // walls thicker than the part: the whole volume is shell
        Assert.Equal(8 * 1.24, MeshVolume.EstimateMass(8, 24, 1.24, 0, wallMm: 50), 6);
    }
}
