using System.Text.Json;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class RecentFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-recents-" + Guid.NewGuid().ToString("N"));
    private string Create(string name)
    { var path = Path.Combine(_root, "02_MODELES_3D", name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "file"); return path; }

    [Fact]
    public void Remember_is_bounded_deduplicated_and_preserves_relative_paths_across_relocation()
    {
        var store = new RecentFileStore(_root); var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 25; index++) store.Remember(Create($"piece-{index}.stl"), now.AddSeconds(index));
        store.Remember(Create("piece-15.stl"), now.AddMinutes(1));
        var entries = store.Load(); Assert.Equal(20, entries.Count); Assert.EndsWith("piece-15.stl", entries[0].RelativePath);
        Assert.All(entries, e => Assert.False(Path.IsPathRooted(e.RelativePath)));
        Assert.Equal("file", File.ReadAllText(Create("piece-15.stl")));
        var relocated = _root + "-moved";
        Directory.Move(_root, relocated);
        try { Assert.Equal(entries, new RecentFileStore(relocated).Load()); }
        finally { Directory.Move(relocated, _root); }
    }

    [Theory]
    [InlineData("program.exe")]
    [InlineData("script.ps1")]
    public void Executable_files_are_not_added(string name)
    {
        var store = new RecentFileStore(_root);
        Assert.Throws<ArgumentException>(() => store.Remember(Create(name), DateTimeOffset.Now));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Missing_or_outside_files_do_not_change_history_and_workspaces_are_isolated()
    {
        var store = new RecentFileStore(_root); var path = Create("piece.stl"); store.Remember(path, DateTimeOffset.Now);
        Assert.Throws<ArgumentException>(() => store.Remember(Path.Combine(_root, "other.stl"), DateTimeOffset.Now));
        Assert.Throws<FileNotFoundException>(() => store.Remember(Path.Combine(_root, "02_MODELES_3D", "missing.stl"), DateTimeOffset.Now));
        Assert.Single(store.Load()); Assert.Empty(new RecentFileStore(Path.Combine(_root, "other-workspace")).Load());
        File.Delete(path); Assert.Single(store.Load()); // The UI can explicitly report an unavailable file.
    }

    [Fact]
    public void Corrupt_history_is_preserved_and_never_replaced_by_an_open_event()
    {
        var path = Create("piece.stl"); var history = Path.Combine(_root, ".ms3dprint-manager", "recent-files.json");
        Directory.CreateDirectory(Path.GetDirectoryName(history)!); File.WriteAllText(history, "corrupt");
        var store = new RecentFileStore(_root);
        Assert.Throws<JsonException>(() => store.Load());
        Assert.Throws<JsonException>(() => store.Remember(path, DateTimeOffset.Now));
        Assert.Equal("corrupt", File.ReadAllText(history));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
