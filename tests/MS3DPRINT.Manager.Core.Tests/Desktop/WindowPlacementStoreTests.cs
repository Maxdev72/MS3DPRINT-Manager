using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WindowPlacementStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-window-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }

    [Fact]
    public void Save_then_Load_round_trips_the_size_and_state()
    {
        var store = new WindowPlacementStore(Path.Combine(_directory, "window.json"));

        store.Save(new WindowPlacement(1320, 840, true));

        Assert.Equal(new WindowPlacement(1320, 840, true), store.Load());
    }

    [Fact]
    public void Load_returns_null_without_a_file_or_with_a_damaged_one()
    {
        var path = Path.Combine(_directory, "window.json");
        var store = new WindowPlacementStore(path);
        Assert.Null(store.Load());

        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{ not json");
        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_ignores_absurd_sizes()
    {
        var store = new WindowPlacementStore(Path.Combine(_directory, "window.json"));
        store.Save(new WindowPlacement(5, 5, false));

        Assert.Null(store.Load());
    }
}
