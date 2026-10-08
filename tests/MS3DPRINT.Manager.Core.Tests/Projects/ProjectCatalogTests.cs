using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Core.Clients;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Load_CopyUnderAnotherClientNeverInheritsOriginalProfile(bool indexed)
    {
        var paths = new WorkspaceMetadataPaths(_root);
        var now = DateTimeOffset.UtcNow;
        var client = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "A", "ACM", "A", null, null, null, null, new(null, null, null, null, null), now, now);
        new ClientProfileStore(paths).Create(client);
        var original = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "A", "ACM-2026-001_TEST")).FullName;
        var copy = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "B", "ACM-2026-001_TEST")).FullName;
        var profile = new ProjectProfile(Guid.NewGuid(), client.Id, "ACM", "ACM-2026-001", "ACM-2026-001_TEST", "TEST", ProjectStatus.Quote, now, null, null, null, now,
            indexed ? Path.GetRelativePath(_root, original) : null);
        new ProjectProfileStore(paths).Create(profile);

        var projects = new ProjectCatalog(new ProjectProfileStore(paths)).Load(_root);

        Assert.Equal(profile.Id, projects.Single(p => p.ProjectPath == original).Profile!.Id);
        Assert.Null(projects.Single(p => p.ProjectPath == copy).Profile);
    }

    [Fact]
    public void Load_UnderscoreClientCodeIsRecognizedAndCounted()
    {
        var reference = ProjectReferenceGenerator.Create("ACME_FR", 2026, [], "TEST");
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "ACME", reference.FolderName));
        var paths = new WorkspaceMetadataPaths(_root);
        var project = Assert.Single(new ProjectCatalog(new ProjectProfileStore(paths)).Load(_root));
        Assert.Equal("ACME_FR-2026-001", project.Reference);
        Assert.Equal("TEST", project.ProjectName);
        Assert.Equal(1, Assert.Single(new ClientCatalog(new ClientProfileStore(paths)).Load(_root)).ProjectCount);
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
