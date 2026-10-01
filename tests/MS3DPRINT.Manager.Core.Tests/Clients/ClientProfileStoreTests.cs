using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Clients;

public sealed class ClientProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-client-profile-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceMetadataPaths _paths;

    public ClientProfileStoreTests() => _paths = new WorkspaceMetadataPaths(_root);

    [Fact]
    public void Create_PersistsOneProfileForANewStore()
    {
        var store = new ClientProfileStore(_paths);
        store.Create(CreateProfessionalProfile());

        var profile = new ClientProfileStore(_paths).LoadAll().Single();
        Assert.Equal("MPO", profile.ClientCode);
        Assert.Equal(ClientKind.Professional, profile.Kind);
    }

    [Fact]
    public void Update_ArchivesPriorJsonBeforeReplacingIt()
    {
        var store = new ClientProfileStore(_paths);
        var profile = CreateProfessionalProfile();
        store.Create(profile);

        store.Update(profile with { Notes = "Relancer lundi", UpdatedAt = profile.UpdatedAt.AddMinutes(1) });

        Assert.Single(Directory.GetFiles(_paths.HistoryDirectory, "*.json"));
        Assert.Equal("Relancer lundi", store.LoadAll().Single().Notes);
    }

    [Fact]
    public void Load_ReturnsLatestProfileAfterAnUpdate()
    {
        var store = new ClientProfileStore(_paths);
        var profile = CreateProfessionalProfile();
        store.Create(profile);
        store.Update(profile with { CompanyName = "MPO Industrie" });

        Assert.Equal("MPO Industrie", store.Load(profile.Id).CompanyName);
    }

    [Fact]
    public void Create_RejectsTwoProfilesForTheSameFolder()
    {
        var store = new ClientProfileStore(_paths);
        store.Create(CreateProfessionalProfile());

        Assert.Throws<InvalidOperationException>(() => store.Create(CreateProfessionalProfile() with { Id = Guid.NewGuid() }));
    }

    private static ClientProfile CreateProfessionalProfile() => new(
        Guid.NewGuid(), ClientKind.Professional, "MPO", "MPO", "MPO", null, null,
        "1 rue de l'Atelier", null, new PrimaryContact("Marie", "Durand", "Achats", "0600000000", "marie@example.test"),
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
