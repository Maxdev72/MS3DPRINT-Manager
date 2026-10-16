using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class GroundGridTests
{
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
    public void The_requested_cell_size_is_kept()
        => Assert.Equal(10f, GroundGrid.Spacing(10, 40));

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void An_invalid_cell_size_falls_back_to_a_fifth_of_the_model_radius(float requested)
        => Assert.Equal(8f, GroundGrid.Spacing(requested, 40));

    [Fact]
    public void The_cell_size_never_reaches_zero_even_for_a_point_like_model()
        => Assert.True(GroundGrid.Spacing(0, 0) > 0);

    [Fact]
    public void Coarser_lines_are_brighter_than_the_cell_lines()
        => Assert.True(GroundGrid.Major.Length() > GroundGrid.Minor.Length());
}
