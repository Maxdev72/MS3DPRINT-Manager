using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class ProjectFileBrowserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-file-browser-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void List_ReturnsImmediateEntriesWithDirectoriesFirst()
    {
        var project = Path.Combine(_root, "project");
        Directory.CreateDirectory(Path.Combine(project, "03_CAO_3D", "01_MASTER"));
        File.WriteAllText(Path.Combine(project, "brief.pdf"), "x");

        var entries = new ProjectFileBrowser().List(project, project);

        Assert.Equal(["03_CAO_3D", "brief.pdf"], entries.Select(entry => entry.Name));
        Assert.True(entries[0].IsDirectory);
        Assert.False(entries[1].IsDirectory);
    }

    [Fact]
    public void List_RejectsDirectoryOutsideProject()
    {
        var project = Path.Combine(_root, "project");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(outside);

        Assert.Throws<UnauthorizedAccessException>(() => new ProjectFileBrowser().List(project, outside));
    }

    [Fact]
    public async Task FolderSize_ReturnsNestedFileTotal()
    {
        var folder = Path.Combine(_root, "project", "03_CAO_3D");
        Directory.CreateDirectory(Path.Combine(folder, "03_STL"));
        await File.WriteAllBytesAsync(Path.Combine(folder, "piece.stl"), new byte[512]);
        await File.WriteAllBytesAsync(Path.Combine(folder, "03_STL", "piece.3mf"), new byte[1024]);

        var size = await new FolderSizeService().GetSizeAsync(folder, CancellationToken.None);

        Assert.Equal(1536, size);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
