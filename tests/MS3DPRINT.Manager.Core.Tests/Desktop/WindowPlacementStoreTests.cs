using Avalonia;
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

    [Fact]
    public void Save_then_Load_round_trips_the_position()
    {
        var store = new WindowPlacementStore(Path.Combine(_directory, "window.json"));

        store.Save(new WindowPlacement(1200, 760, false, -1920, 120));

        Assert.Equal(new WindowPlacement(1200, 760, false, -1920, 120), store.Load());
    }

    [Fact]
    public void Load_accepts_a_file_written_before_positions_were_kept()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "window.json");
        File.WriteAllText(path, "{\"Width\":1200,\"Height\":760,\"Maximized\":true}");

        var placement = new WindowPlacementStore(path).Load();

        Assert.Equal(new WindowPlacement(1200, 760, true), placement);
        Assert.Null(placement!.X);
    }

    [Fact]
    public void IsReachable_needs_the_title_bar_on_a_screen()
    {
        PixelRect[] screens = [new(0, 0, 1920, 1040), new(1920, 0, 1920, 1040)];

        Assert.True(WindowPlacementStore.IsReachable(new PixelRect(100, 100, 1200, 760), screens));
        Assert.True(WindowPlacementStore.IsReachable(new PixelRect(1800, 50, 1200, 760), screens));    // straddles two monitors
        Assert.False(WindowPlacementStore.IsReachable(new PixelRect(4500, 100, 1200, 760), screens));  // monitor unplugged
        Assert.False(WindowPlacementStore.IsReachable(new PixelRect(100, 2000, 1200, 760), screens));  // below every screen
        Assert.False(WindowPlacementStore.IsReachable(new PixelRect(-1190, 100, 1200, 760), screens)); // only 10 px visible
    }

    [Fact]
    public void CentreIn_centres_the_window_in_the_work_area()
    {
        Assert.Equal(new PixelPoint(360, 140), WindowPlacementStore.CentreIn(new PixelRect(0, 0, 1920, 1040), new PixelSize(1200, 760)));
        Assert.Equal(new PixelPoint(1920, 0), WindowPlacementStore.CentreIn(new PixelRect(1920, 0, 800, 600), new PixelSize(1200, 900)));
    }
}
