using System.Windows.Media.Media3D;
using System.Globalization;
using MS3DPRINT.Manager.App.Preview;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class ModelDimensionsTests
{
    [Fact]
    public void FromBounds_ReturnsAxisLengthsWithoutAssumingMillimeters()
    {
        var dimensions = ModelDimensions.FromBounds(new Rect3D(10, 20, 30, 40, 50, 60));

        Assert.Equal(40, dimensions.X);
        Assert.Equal(50, dimensions.Y);
        Assert.Equal(60, dimensions.Z);
    }

    [Fact]
    public void FromBounds_RejectsEmptyGeometry()
        => Assert.Throws<InvalidDataException>(() => ModelDimensions.FromBounds(Rect3D.Empty));

    [Fact]
    public void ToDisplayText_PreservesSmallNonzeroDimensions()
    {
        var text = new ModelDimensions(0.00004, 1.23456, 0).ToDisplayText(CultureInfo.InvariantCulture);

        Assert.Contains("X 4E-05", text);
        Assert.Contains("Y 1.235", text);
        Assert.Contains("unités du modèle", text);
    }
}
