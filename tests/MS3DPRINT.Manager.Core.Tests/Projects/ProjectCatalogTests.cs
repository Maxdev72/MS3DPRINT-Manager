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

    [Fact]
    public void Load_StillShowsProjectFoldersWhenAnUnrelatedProfileIsMalformed()
    {
        var paths = new WorkspaceMetadataPaths(_root);
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO", "MPO-2026-001_OUTILLAGE"));
        Directory.CreateDirectory(paths.ProjectsDirectory);
        File.WriteAllText(Path.Combine(paths.ProjectsDirectory, "broken.json"), "{");
        var catalog = new ProjectCatalog(new ProjectProfileStore(paths));

        var projects = catalog.Load(_root);

        Assert.Equal("MPO-2026-001", Assert.Single(projects).Reference);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(ProjectStatus.Quote, "DEVIS")]
    [InlineData(ProjectStatus.InProgress, "EN_COURS")]
    [InlineData(ProjectStatus.Completed, "TERMINE")]
    public void ProjectSummary_ProvidesFrenchStatusLabels(ProjectStatus status, string expected)
    {
        var project = new ProjectSummary("CLIENT", _root, _root, "CLI-2026-001", "CLI-2026-001_TEST", new ProjectProfile(
            Guid.NewGuid(), Guid.NewGuid(), "CLI", "CLI-2026-001", "CLI-2026-001_TEST", "TEST", status,
            DateTimeOffset.UtcNow, null, null, null, DateTimeOffset.UtcNow));

        Assert.Equal(expected, project.StatusLabel);
    }
}
