using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Projects;

public sealed class ProjectCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-project-catalog-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_RecognizesProjectReferenceFoldersWithoutProfiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO", "00_CLIENT"));
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO", "MPO-2026-001_OUTILLAGE"));
        var catalog = new ProjectCatalog(new ProjectProfileStore(new WorkspaceMetadataPaths(_root)));

        var project = catalog.Load(_root).Single();

        Assert.Equal("MPO-2026-001", project.Reference);
        Assert.True(project.IsProfileMissing);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
