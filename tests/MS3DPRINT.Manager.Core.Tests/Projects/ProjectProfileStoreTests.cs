using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Projects;

public sealed class ProjectProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-project-profile-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_PersistsAQuoteProfile()
    {
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        store.Create(CreateProfile());

        var profile = store.LoadAll().Single();
        Assert.Equal(ProjectStatus.Quote, profile.Status);
        Assert.Equal("MPO-2026-001", profile.Reference);
    }

    [Fact]
    public void Update_ArchivesThePriorProfile()
    {
        var paths = new WorkspaceMetadataPaths(_root);
        var store = new ProjectProfileStore(paths);
        var profile = CreateProfile();
        store.Create(profile);

        store.Update(profile with { Status = ProjectStatus.InProgress, UpdatedAt = profile.UpdatedAt.AddMinutes(1) });

        Assert.Equal(ProjectStatus.InProgress, store.LoadAll().Single().Status);
        Assert.Single(Directory.GetFiles(paths.HistoryDirectory, "*.json"));
    }

    private static ProjectProfile CreateProfile() => new(Guid.NewGuid(), Guid.NewGuid(), "MPO", "MPO-2026-001", "MPO-2026-001_OUTILLAGE", "OUTILLAGE", ProjectStatus.Quote, DateTimeOffset.UtcNow, null, "Prototype", null, DateTimeOffset.UtcNow);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
