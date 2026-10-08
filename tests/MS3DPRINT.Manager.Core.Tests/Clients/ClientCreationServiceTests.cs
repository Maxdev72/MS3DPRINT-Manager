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

    [Fact]
    public void Create_ReservedTrashedNameLeavesNoPartialCreationAndOriginalRestores()
    {
        var service = new ClientCreationService(_root, new FolderTreeService(), _profiles, _codes);
        var original = service.Create(CreateProfile());
        var files = new ManagedFileService(_root);
        var path = Path.Combine(_root, "01_CLIENTS", "MPO");
        var entry = files.Trash(path, [Path.Combine(_root, ".ms3dprint-manager", "clients", original.Id + ".json")]);

        Assert.Throws<FolderConflictException>(() => service.Create(CreateProfile()));

        Assert.False(Directory.Exists(path));
        Assert.Empty(_profiles.LoadAll());
        files.Restore(entry.Id);
        Assert.Equal(original.Id, Assert.Single(_profiles.LoadAll()).Id);
    }

    [Fact]
    public void Create_RegistryWriteFailureRetainsCreatedDataOnlyInTrash()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".ms3dprint-manager", "client-codes.json"));
        var service = new ClientCreationService(_root, new FolderTreeService(), _profiles, _codes);

        Assert.ThrowsAny<IOException>(() => service.Create(CreateProfile()));

        Assert.False(Directory.Exists(Path.Combine(_root, "01_CLIENTS", "MPO")));
        Assert.Empty(_profiles.LoadAll());
        Assert.Single(new ManagedFileService(_root).ListTrash());
    }

    [Fact]
    public void Create_InvalidProfileLeavesNoActiveFolder()
    {
        var service = new ClientCreationService(_root, new FolderTreeService(), _profiles, _codes);
        Assert.Throws<ArgumentException>(() => service.Create(CreateProfile() with { CompanyName = null }));
        Assert.False(Directory.Exists(Path.Combine(_root, "01_CLIENTS", "MPO")));
        Assert.Empty(_profiles.LoadAll());
    }

    [Fact]
    public void Create_ProfileCollisionPreservesExistingProfile()
    {
        var original = CreateProfile();
        _profiles.Create(original);
        var service = new ClientCreationService(_root, new FolderTreeService(), _profiles, _codes);

        Assert.Throws<InvalidOperationException>(() => service.Create(original));

        Assert.False(Directory.Exists(Path.Combine(_root, "01_CLIENTS", "MPO")));
        Assert.Equal(original, Assert.Single(_profiles.LoadAll()));
        Assert.Null(_codes.GetCode("MPO"));
    }

    [Theory]
    [InlineData("../ESCAPE")]
    [InlineData("GROUP/MPO")]
    public void Create_RejectsFolderPathsBeforeWritingAnything(string folderName)
    {
        var service = new ClientCreationService(_root, new FolderTreeService(), _profiles, _codes);
        Assert.Throws<ArgumentException>(() => service.Create(CreateProfile() with { FolderName = folderName }));
        Assert.False(Directory.Exists(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
