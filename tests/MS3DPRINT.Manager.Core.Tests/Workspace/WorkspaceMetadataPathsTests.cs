using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Workspace;

public sealed class WorkspaceMetadataPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-workspace-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Constructor_UsesMetadataFolderBelowWorkspace()
    {
        var paths = new WorkspaceMetadataPaths(_root);

        Assert.Equal(Path.Combine(_root, ".ms3dprint-manager"), paths.MetadataDirectory);
        Assert.Equal(Path.Combine(paths.MetadataDirectory, "clients"), paths.ClientsDirectory);
        Assert.Equal(Path.Combine(paths.MetadataDirectory, "projects"), paths.ProjectsDirectory);
        Assert.Equal(Path.Combine(paths.MetadataDirectory, "history"), paths.HistoryDirectory);
    }

    [Fact]
    public void EnsureMetadataDirectories_DoesNotCreateBusinessFolders()
    {
        var paths = new WorkspaceMetadataPaths(_root);

        paths.EnsureMetadataDirectories();

        Assert.True(Directory.Exists(paths.ClientsDirectory));
        Assert.True(Directory.Exists(paths.ProjectsDirectory));
        Assert.True(Directory.Exists(paths.HistoryDirectory));
        Assert.False(Directory.Exists(Path.Combine(_root, "01_CLIENTS")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
