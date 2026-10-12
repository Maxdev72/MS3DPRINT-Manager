using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class ModelThumbnailsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-thumbs-" + Guid.NewGuid().ToString("N"));

    public ModelThumbnailsTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void CachePath_changes_when_the_file_changes_and_ignores_case()
    {
        var when = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        var first = ModelThumbnails.CachePath(@"C:\Models\Part.stl", 1000, when);

        Assert.Equal(first, ModelThumbnails.CachePath(@"c:\models\PART.stl", 1000, when));
        Assert.NotEqual(first, ModelThumbnails.CachePath(@"C:\Models\Part.stl", 1001, when));
        Assert.NotEqual(first, ModelThumbnails.CachePath(@"C:\Models\Part.stl", 1000, when.AddSeconds(1)));
        Assert.EndsWith(".png", first);
    }

    [Fact]
    public void IsEligible_only_accepts_existing_models_that_are_not_huge()
    {
        var stl = Path.Combine(_directory, "a.stl");
        var txt = Path.Combine(_directory, "a.txt");
        File.WriteAllText(stl, "solid x");
        File.WriteAllText(txt, "x");

        Assert.True(ModelThumbnails.IsEligible(stl));
        Assert.False(ModelThumbnails.IsEligible(txt));
        Assert.False(ModelThumbnails.IsEligible(Path.Combine(_directory, "missing.stl")));
        File.WriteAllText(Path.Combine(_directory, "empty.stl"), "");
        Assert.False(ModelThumbnails.IsEligible(Path.Combine(_directory, "empty.stl")));
    }
}
