using System.Globalization;
using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class ModelDimensionsTests
{
    [Fact]
    public void FromSize_ReturnsAxisLengthsWithoutAssumingMillimeters()
    {
        var dimensions = ModelDimensions.FromSize(new Vector3(40, 50, 60));

        Assert.Equal(40, dimensions.X);
        Assert.Equal(50, dimensions.Y);
        Assert.Equal(60, dimensions.Z);
    }

    [Fact]
    public void FromSize_RejectsNonFiniteGeometry()
        => Assert.Throws<InvalidDataException>(() => ModelDimensions.FromSize(new Vector3(float.NegativeInfinity, 1, 1)));

    [Fact]
    public void ToDisplayText_PreservesSmallNonzeroDimensions()
    {
        var text = new ModelDimensions(0.00004, 1.23456, 0).ToDisplayText(CultureInfo.InvariantCulture);

        Assert.Contains("X 4E-05", text);
        Assert.Contains("Y 1.235", text);
        Assert.Contains("unités du modèle", text);
    }
}
