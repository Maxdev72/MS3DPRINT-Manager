using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Clients;

public sealed class ClientCreationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-client-create-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ClientProfileStore _profiles;
    private readonly ClientCodeRegistry _codes;

    public ClientCreationServiceTests()
    {
        var paths = new WorkspaceMetadataPaths(_root);
        _profiles = new ClientProfileStore(paths);
        _codes = new ClientCodeRegistry(paths.MetadataDirectory);
    }

    [Fact]
    public void Create_CreatesClientTreeProfileAndCode()
    {
        var profile = new ClientCreationService(_root, new FolderTreeService(), _profiles, _codes).Create(CreateProfile());

        Assert.True(Directory.Exists(Path.Combine(_root, "01_CLIENTS", "MPO", "00_CLIENT")));
        Assert.Equal(profile.Id, _profiles.LoadAll().Single().Id);
        Assert.Equal("MPO", _codes.GetCode("MPO"));
    }

    [Fact]
    public void Create_RefusesExistingFolderWithoutChangingIt()
    {
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO"));
        var service = new ClientCreationService(_root, new FolderTreeService(), _profiles, _codes);

        Assert.Throws<FolderConflictException>(() => service.Create(CreateProfile()));
    }

    private static ClientProfile CreateProfile() => new(Guid.NewGuid(), ClientKind.Professional, "MPO", "MPO", "MPO", null, null, null, null,
        new PrimaryContact("Marie", "Durand", null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
