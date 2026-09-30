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
            var clientPath = Path.Combine(root, "01_CLIENTS", "MPO");
            Directory.CreateDirectory(clientPath);
            var clientProfile = new MS3DPRINT.Manager.Core.Clients.ClientProfile(Guid.NewGuid(), MS3DPRINT.Manager.Core.Clients.ClientKind.Professional, "MPO", "MPO", "MPO", null, null, null, null, new MS3DPRINT.Manager.Core.Clients.PrimaryContact(null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var client = new MS3DPRINT.Manager.Core.Clients.ClientSummary(clientPath, "MPO", "MPO", "MPO", MS3DPRINT.Manager.Core.Clients.ClientKind.Professional, clientProfile, 0);
            var profiles = new ProjectProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(root));
            var service = new ProjectCreationService(new FolderTreeService());

            var result = service.CreateWithProfile(client, 2026, "Outillage", profiles, null, "Prototype");

            Assert.Equal(ProjectStatus.Quote, result.Profile.Status);
            Assert.True(Directory.Exists(Path.Combine(clientPath, result.Reference.FolderName)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
