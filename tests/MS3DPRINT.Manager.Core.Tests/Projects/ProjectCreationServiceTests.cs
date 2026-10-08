using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.Projects;

public sealed class ProjectCreationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_RecomputesSequenceInsideCreationWhenPreviewsAreStale()
    {
        var clientPath = Path.Combine(_root, "CLIENT");
        Directory.CreateDirectory(clientPath);
        var stalePreviewOne = ProjectReferenceGenerator.Create("MPO", 2026, [], "Premier");
        var stalePreviewTwo = ProjectReferenceGenerator.Create("MPO", 2026, [], "Deuxieme");
        Assert.Equal(1, stalePreviewOne.Sequence);
        Assert.Equal(1, stalePreviewTwo.Sequence);
        var service = new ProjectCreationService(new FolderTreeService());

        var first = service.Create(clientPath, "MPO", 2026, "Premier");
        var second = service.Create(clientPath, "MPO", 2026, "Deuxieme");

        Assert.Equal("MPO-2026-001_PREMIER", first.FolderName);
        Assert.Equal("MPO-2026-002_DEUXIEME", second.FolderName);
        Assert.True(Directory.Exists(Path.Combine(clientPath, first.FolderName)));
        Assert.True(Directory.Exists(Path.Combine(clientPath, second.FolderName)));
    }

    [Fact]
    public void Create_NeverReusesReferencesAfterMoveOrTrashAndAllowsRestore()
    {
        var client = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO")).FullName;
        var service = new ProjectCreationService(new FolderTreeService());
        var first = service.Create(client, "MPO", 2026, "Premier");
        var moved = Path.Combine(client, "Group", "Renomme");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(Path.Combine(client, first.FolderName), moved);
        var second = service.Create(client, "MPO", 2026, "Deuxieme");
        Assert.Equal(2, second.Sequence);
        var files = new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(_root);
        var trash = files.Trash(Path.Combine(client, second.FolderName));
        var third = new ProjectCreationService(new FolderTreeService()).Create(client, "MPO", 2026, "Troisieme");
        Assert.Equal(3, third.Sequence);
        files.Restore(trash.Id);
        Assert.True(Directory.Exists(Path.Combine(client, second.FolderName)));
    }

    [Fact]
    public void Create_SeedsLegacyReferencesFromNestedFoldersAndTrash()
    {
        var client = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO")).FullName;
        Directory.CreateDirectory(Path.Combine(client, "Group", "MPO-2026-008_LEGACY"));
        var old = Directory.CreateDirectory(Path.Combine(client, "MPO-2026-010_ANCIEN")).FullName;
        new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(_root).Trash(old);
        var created = new ProjectCreationService(new FolderTreeService()).Create(client, "MPO", 2026, "Suivant");
        Assert.Equal(11, created.Sequence);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void CreateWithProfile_CreatesQuoteProfileAfterTheProjectTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-project-profile-create-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clientPath = Path.Combine(root, "01_CLIENTS", "Group", "MPO");
            Directory.CreateDirectory(clientPath);
            var clientProfile = new MS3DPRINT.Manager.Core.Clients.ClientProfile(Guid.NewGuid(), MS3DPRINT.Manager.Core.Clients.ClientKind.Professional, "MPO", "MPO", "MPO", null, null, null, null, new MS3DPRINT.Manager.Core.Clients.PrimaryContact(null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            clientProfile = clientProfile with { RelativePath = Path.GetRelativePath(root, clientPath) };
            new MS3DPRINT.Manager.Core.Clients.ClientProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(root)).Create(clientProfile);
            var client = new MS3DPRINT.Manager.Core.Clients.ClientSummary(clientPath, "MPO", "MPO", "MPO", MS3DPRINT.Manager.Core.Clients.ClientKind.Professional, clientProfile, 0);
            var profiles = new ProjectProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(root));
            var service = new ProjectCreationService(new FolderTreeService());

            var result = service.CreateWithProfile(client, 2026, "Outillage", profiles, null, "Prototype");

            Assert.Equal(ProjectStatus.Quote, result.Profile.Status);
            Assert.True(Directory.Exists(Path.Combine(clientPath, result.Reference.FolderName)));
            Assert.Equal("01_CLIENTS/Group/MPO/MPO-2026-001_OUTILLAGE", result.Profile.RelativePath?.Replace('\\', '/'));
            Assert.Equal(Path.Combine(clientPath, result.Reference.FolderName), Assert.Single(new ProjectCatalog(profiles).Load(root)).ProjectPath);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CreateWithProfile_ProfileWriteFailureLeavesNoActiveProject()
    {
        var clientPath = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO")).FullName;
        var paths = new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(_root);
        Directory.CreateDirectory(paths.MetadataDirectory);
        File.WriteAllText(paths.ProjectsDirectory, "blocked");
        var now = DateTimeOffset.UtcNow;
        var profile = new MS3DPRINT.Manager.Core.Clients.ClientProfile(Guid.NewGuid(), MS3DPRINT.Manager.Core.Clients.ClientKind.Professional,
            "MPO", "MPO", "MPO", null, null, null, null, new MS3DPRINT.Manager.Core.Clients.PrimaryContact(null, null, null, null, null), now, now);
        var client = new MS3DPRINT.Manager.Core.Clients.ClientSummary(clientPath, "MPO", "MPO", "MPO", profile.Kind, profile, 0);

        Assert.ThrowsAny<IOException>(() => new ProjectCreationService(new FolderTreeService()).CreateWithProfile(client, 2026, "Prototype", new ProjectProfileStore(paths), null, null));

        Assert.Empty(Directory.EnumerateDirectories(clientPath));
        Assert.Single(new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(_root).ListTrash());
    }
}
