using MS3DPRINT.Manager.App;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class WindowPlacementStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MS3DPRINT-window-placement-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoad_PreservesTheLastNormalWindowSize()
    {
        var store = new WindowPlacementStore(_directory);

        store.Save(975, 640);

        Assert.Equal(new WindowPlacement(975, 640), store.Load());
    }

    [Fact]
    public void Load_ReturnsNullWhenTheSavedPlacementIsUnreadable()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "window-placement.json"), "{");

        Assert.Null(new WindowPlacementStore(_directory).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
