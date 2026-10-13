using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class GroundGridTests
{
    private static MeshModel Cube(float size, Vector3 at)
    {
        Vector3[] corners =
        [
            at, at + new Vector3(size, 0, 0), at + new Vector3(0, size, 0), at + new Vector3(0, 0, size),
            at + new Vector3(size, size, size)
        ];
        return new MeshModel(corners, [0, 1, 2, 1, 2, 4, 0, 3, 1]);
    }

    [Theory]
    [InlineData(100, 10)]
    [InlineData(35, 5)]
    [InlineData(250, 50)]
    [InlineData(1200, 200)]
    [InlineData(8, 1)]
    [InlineData(0, 10)]
    [InlineData(double.NaN, 10)]
    public void Cell_size_is_a_round_number_giving_about_ten_cells(double extentMillimetres, double expected)
        => Assert.Equal(expected, GroundGrid.NiceSpacingMillimetres(extentMillimetres), 6);

    [Fact]
    public void Grid_lies_just_under_the_base_of_the_model_and_comes_in_line_pairs()
    {
        var mesh = Cube(40, new Vector3(5, 5, 12));
        var (positions, colors) = GroundGrid.Build(mesh, 10, new Vector3(0.06f, 0.1f, 0.14f));

        Assert.Equal(positions.Length, colors.Length);
        Assert.Equal(0, positions.Length / 3 % 2);
        for (var i = 2; i < positions.Length; i += 3)
        {
            Assert.True(positions[i] <= 12f);
            Assert.True(positions[i] > 11.9f);
        }
        Assert.All(colors, channel => Assert.InRange(channel, 0f, 1f));
    }

    [Fact]
    public void Lines_fade_into_the_background_far_from_the_model()
    {
        var background = new Vector3(0.06f, 0.1f, 0.14f);
        var (positions, colors) = GroundGrid.Build(Cube(40, Vector3.Zero), 10, background);

        var farthest = 0;
        var nearest = 0;
        float Distance(int vertex) => new Vector2(positions[vertex * 3] - 20, positions[vertex * 3 + 1] - 20).Length();
        for (var vertex = 1; vertex < positions.Length / 3; vertex++)
        {
            if (Distance(vertex) > Distance(farthest)) farthest = vertex;
            if (Distance(vertex) < Distance(nearest)) nearest = vertex;
        }
        var far = new Vector3(colors[farthest * 3], colors[farthest * 3 + 1], colors[farthest * 3 + 2]);
        var near = new Vector3(colors[nearest * 3], colors[nearest * 3 + 1], colors[nearest * 3 + 2]);

        Assert.True(Vector3.Distance(far, background) < 0.01f);
        Assert.True(Vector3.Distance(near, background) > 0.05f);
    }

    [Fact]
    public void Invalid_spacing_falls_back_to_a_size_derived_from_the_model()
    {
        var (positions, _) = GroundGrid.Build(Cube(40, Vector3.Zero), 0, Vector3.Zero);
        Assert.NotEmpty(positions);
        Assert.All(positions, value => Assert.True(float.IsFinite(value)));
    }
}
