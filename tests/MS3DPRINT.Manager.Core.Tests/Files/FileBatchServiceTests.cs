using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class FileBatchServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-file-batch-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Move_continues_after_a_collision_without_overwriting_and_deduplicates_sources()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Source")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Target")).FullName;
        var a = Path.Combine(source, "a.txt"); var b = Path.Combine(source, "b.txt");
        File.WriteAllText(a, "original-a"); File.WriteAllText(b, "original-b"); File.WriteAllText(Path.Combine(target, "a.txt"), "existing");
        var result = new FileBatchService().Execute(_root, source, [a, b, b], FileBatchOperation.Move, target);
        Assert.Equal([b], result.Completed); Assert.Equal(a, Assert.Single(result.Failures).Path);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.Equal("original-a", File.ReadAllText(a)); Assert.False(File.Exists(b));
        Assert.Equal("original-b", File.ReadAllText(Path.Combine(target, "b.txt")));
    }

    [Fact]
    public void Trash_only_accepts_immediate_files_and_successes_remain_restorable()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Source")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(source, "Nested")).FullName;
        var a = Path.Combine(source, "a.txt"); var b = Path.Combine(nested, "b.txt"); var missing = Path.Combine(source, "missing.txt");
        File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        var result = new FileBatchService().Execute(_root, source, [nested, b, missing, a], FileBatchOperation.Trash);
        Assert.Equal([a], result.Completed); Assert.Equal(3, result.Failures.Count);
        Assert.True(Directory.Exists(nested)); Assert.True(File.Exists(b)); Assert.False(File.Exists(a));
        var trash = new ManagedFileService(_root); var entry = Assert.Single(trash.ListTrash());
        trash.Restore(entry.Id); Assert.Equal("a", File.ReadAllText(a));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
