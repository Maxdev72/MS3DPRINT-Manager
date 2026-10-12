using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class ModelUnitsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-units-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }

    [Fact]
    public void FormatSize_ShowsMillimetresBesideAnotherUnit()
    {
        Assert.Equal("10 × 20 × 5 mm", ModelUnits.FormatSize(new Vector3(10, 20, 5), 1, "mm"));
        Assert.Equal("1 × 2 × 0,5 in (25,4 × 50,8 × 12,7 mm)", ModelUnits.FormatSize(new Vector3(1, 2, 0.5f), 25.4, "in"));
    }

    [Theory]
    [InlineData(1, 1, 1, 1, true)]       // a 1 mm cube is far too small for a print
    [InlineData(54, 54, 14, 1, false)]
    [InlineData(2000, 10, 10, 1, true)]  // two metres in millimetres
    [InlineData(0.5f, 0.5f, 0.2f, 1000, false)]
    public void Warning_FlagsImplausibleSizes(float x, float y, float z, double scale, bool expected)
        => Assert.Equal(expected, ModelUnits.Warning(new Vector3(x, y, z), scale) is not null);

    [Fact]
    public void Store_RemembersTheUnitPerFileIgnoringCase()
    {
        var store = new ModelUnitStore(Path.Combine(_directory, "units.json"));

        store.Set(@"C:\Models\Part.stl", "in");
        store.Set(@"C:\Models\Other.stl", "custom:3.5");

        Assert.Equal("in", store.Get(@"c:\models\PART.stl"));
        Assert.Equal("custom:3.5", store.Get(@"C:\Models\Other.stl"));
        Assert.Null(store.Get(@"C:\Models\Unknown.stl"));
    }

    [Fact]
    public void Store_SurvivesAMissingOrCorruptFile()
    {
        var path = Path.Combine(_directory, "units.json");
        var store = new ModelUnitStore(path);
        Assert.Null(store.Get("a.stl"));

        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{ not json");

        Assert.Null(store.Get("a.stl"));
        store.Set("a.stl", "cm");
        Assert.Equal("cm", store.Get("a.stl"));
    }
}
