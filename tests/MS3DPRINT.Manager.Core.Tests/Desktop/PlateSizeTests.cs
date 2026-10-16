using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class PlateSizeTests
{
    [Theory]
    [InlineData("256 × 256 × 256 mm", 256, 256, 256)]
    [InlineData("250x210x220", 250, 210, 220)]
    [InlineData("180*180*180", 180, 180, 180)]
    [InlineData("256 X 256 X 256", 256, 256, 256)]
    [InlineData("25,6 x 25 x 24 cm", 256, 250, 240)]
    [InlineData("220.5 x 220 x 250 mm", 220.5, 220, 250)]
    [InlineData("256 × 256 × 256 mm (10 in)", 256, 256, 256)]
    public void Parse_reads_the_usual_ways_of_writing_a_build_volume(string text, double width, double depth, double height)
    {
        var plate = PlateSize.Parse(text);

        Assert.NotNull(plate);
        Assert.Equal(width, plate.Value.WidthMm, 6);
        Assert.Equal(depth, plate.Value.DepthMm, 6);
        Assert.Equal(height, plate.Value.HeightMm, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("grand plateau")]
    [InlineData("256 × 256")]
    [InlineData("Ø 200 × 200 × 250")]
    [InlineData("diam 200 x 200 x 250")]
    [InlineData("0 × 100 × 100")]
    [InlineData("99999 × 100 × 100")]
    public void Parse_gives_nothing_for_text_that_is_not_a_box(string? text) => Assert.Null(PlateSize.Parse(text));

    [Fact]
    public void The_default_plate_is_256_millimetres_in_every_direction()
    {
        Assert.Equal(new PlateSize(256, 256, 256), PlateSize.Default);
        Assert.Equal("256 × 256 × 256 mm", PlateSize.Default.Label);
    }

    [Fact]
    public void A_part_fits_when_no_side_is_larger_than_the_plate()
    {
        var plate = new PlateSize(100, 80, 60);

        Assert.True(plate.Fits(new Vector3(100, 80, 60)));
        Assert.True(plate.Fits(new Vector3(10, 10, 10)));
        Assert.False(plate.Fits(new Vector3(101, 10, 10)));
        Assert.False(plate.Fits(new Vector3(10, 10, 61)));
    }

    [Fact]
    public void Overflow_gives_how_far_each_side_sticks_out()
    {
        var plate = new PlateSize(100, 80, 60);

        Assert.Equal(new Vector3(20, 0, 5), plate.Overflow(new Vector3(120, 50, 65)));
        Assert.Equal(Vector3.Zero, plate.Overflow(new Vector3(100, 80, 60)));
    }
}
