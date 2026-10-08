using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Workspace;

public sealed class EntityManagementServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-entities-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void TrashingProjectCopyAndRestoringIt_PreservesOriginalProfileBytes()
    {
        var (_, original) = CreateFixture();
        var otherClient = CreateClient("SECOND", "SEC");
        var copyPath = Directory.CreateDirectory(Path.Combine(otherClient.ClientPath, original.FolderName)).FullName;
        File.WriteAllText(Path.Combine(copyPath, "copy.txt"), "copy");
        var profilePath = Path.Combine(_root, ".ms3dprint-manager", "projects", original.Profile!.Id + ".json");
        var bytes = File.ReadAllBytes(profilePath);
        var copy = Projects().Load(_root).Single(project => project.ProjectPath == copyPath);
        var entry = new EntityManagementService(_root).TrashProject(copy);
        Assert.Equal(bytes, File.ReadAllBytes(profilePath));
        Assert.Equal(original.Profile.Id, Projects().Load(_root).Single().Profile!.Id);
        new ManagedFileService(_root).Restore(entry.Id);
        Assert.Equal(bytes, File.ReadAllBytes(profilePath));
        Assert.Null(Projects().Load(_root).Single(project => project.ProjectPath == copyPath).Profile);
    }

    [Fact]
    public void RenamingAndMovingProjectCopy_RequiresItsOwnProfileAndPreservesOriginal()
    {
        var (_, original) = CreateFixture();
        var otherClient = CreateClient("SECOND", "SEC");
        var copyPath = Directory.CreateDirectory(Path.Combine(otherClient.ClientPath, original.FolderName)).FullName;
        var profilePath = Path.Combine(_root, ".ms3dprint-manager", "projects", original.Profile!.Id + ".json");
        var bytes = File.ReadAllBytes(profilePath);
        var service = new EntityManagementService(_root);
        var copy = Projects().Load(_root).Single(p => p.ProjectPath == copyPath);
        Assert.Throws<ArgumentException>(() => service.RenameProject(copy, "COPY"));
        var thirdClient = CreateClient("THIRD", "THI");
        Assert.Throws<ArgumentException>(() => service.MoveProject(copy, thirdClient));
        Assert.True(Directory.Exists(copyPath));
        Assert.Equal(bytes, File.ReadAllBytes(profilePath));
        Assert.Single(Projects().Load(_root).Where(p => p.Profile is not null));
    }

    [Fact]
    public void RenamingAndMovingClient_KeepsProjectsAndIdentifiersDiscoverable()
    {
        var (client, project) = CreateFixture();
        var service = new EntityManagementService(_root);
        var renamed = service.RenameClient(client, "ACME_RENAMED");
        var updatedClient = Clients().Load(_root).Single();
        Assert.Equal(client.Profile!.Id, updatedClient.Profile!.Id);
        Assert.Equal("ACM", updatedClient.ClientCode);
        Assert.Equal(renamed, updatedClient.ClientPath);
        var group = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "GROUPE")).FullName;
        service.MoveClient(updatedClient, group);
        var movedClient = Clients().Load(_root).Single();
        var movedProject = Projects().Load(_root).Single();
        Assert.Equal(client.Profile.Id, movedClient.Profile!.Id);
        Assert.Equal(Path.Combine(group, "ACME_RENAMED"), movedClient.ClientPath);
        Assert.Equal(project.Profile!.Id, movedProject.Profile!.Id);
        Assert.Equal(project.Reference, movedProject.Reference);
        Assert.Equal("document", File.ReadAllText(Path.Combine(movedProject.ProjectPath, "test.txt")));
    }

    [Fact]
    public void MovingProjectToAnotherClient_KeepsItsReferenceAndChangesRelationship()
    {
        var (_, project) = CreateFixture();
        var destination = CreateClient("SECOND", "SEC");
        new EntityManagementService(_root).MoveProject(project, destination);
        var moved = Projects().Load(_root).Single();
        Assert.Equal(project.Reference, moved.Reference);
        Assert.Equal(project.Profile!.Id, moved.Profile!.Id);
        Assert.Equal(destination.Profile!.Id, moved.Profile.ClientId);
        Assert.Equal(destination.ClientPath, moved.ClientPath);
        Assert.Equal("SEC", moved.Profile.ClientCode);
    }

    [Fact]
    public void TrashingClient_AlsoRemovesItsProjectProfilesAndRestoresEverything()
    {
        var (client, project) = CreateFixture();
        var entry = new EntityManagementService(_root).TrashClient(client);
        Assert.Empty(Clients().Load(_root));
        Assert.Empty(new ProjectProfileStore(new(_root)).LoadAll());
        new ManagedFileService(_root).Restore(entry.Id);
        Assert.Equal(client.Profile!.Id, Clients().Load(_root).Single().Profile!.Id);
        Assert.Equal(project.Profile!.Id, Projects().Load(_root).Single().Profile!.Id);
    }

    [Fact]
    public void RenameCollision_DoesNotChangeMetadataOrDocuments()
    {
        var (client, _) = CreateFixture();
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "EXISTING"));
        Assert.ThrowsAny<Exception>(() => new EntityManagementService(_root).RenameClient(client, "EXISTING"));
        Assert.Equal("ACME", new ClientProfileStore(new(_root)).Load(client.Profile!.Id).FolderName);
        Assert.True(Directory.Exists(client.ClientPath));
    }

    [Fact]
    public void Recovery_InvalidProfilePathDoesNotMoveDocuments()
    {
        var (client, _) = CreateFixture();
        var target = Path.Combine(_root, "01_CLIENTS", "RENAMED");
        Directory.Move(client.ClientPath, target);
        var profile = Path.Combine(_root, ".ms3dprint-manager", "clients", client.Profile!.Id + ".json");
        var bytes = File.ReadAllBytes(profile);
        var operation = WriteOperation(client.ClientPath, target, [Path.GetRelativePath(_root, profile), "../outside.json"], [bytes, bytes]);
        Assert.ThrowsAny<IOException>(() => new EntityManagementService(_root).RecoverPendingOperations());
        Assert.False(Directory.Exists(client.ClientPath));
        Assert.True(Directory.Exists(target));
        Assert.Equal(bytes, File.ReadAllBytes(profile));
        Assert.True(File.Exists(Path.Combine(operation, "journal.json")));
    }

    [Fact]
    public void TrashClient_ConflictingPendingRecoveryRefusesBeforeTrashingAnything()
    {
        var (client, _) = CreateFixture();
        var target = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "CONFLICT")).FullName;
        var profile = Path.Combine(_root, ".ms3dprint-manager", "clients", client.Profile!.Id + ".json");
        var bytes = File.ReadAllBytes(profile);
        WriteOperation(client.ClientPath, target, [Path.GetRelativePath(_root, profile)], [bytes]);
        Assert.ThrowsAny<IOException>(() => new EntityManagementService(_root).TrashClient(client));
        Assert.True(Directory.Exists(client.ClientPath));
        Assert.True(Directory.Exists(target));
        Assert.Equal(bytes, File.ReadAllBytes(profile));
        Assert.Empty(new ManagedFileService(_root).ListTrash());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Recovery_MissingOrCorruptBackupDoesNotMoveDocuments(bool missing)
    {
        var (client, _) = CreateFixture();
        var target = Path.Combine(_root, "01_CLIENTS", "RENAMED");
        Directory.Move(client.ClientPath, target);
        var profile = Path.Combine(_root, ".ms3dprint-manager", "clients", client.Profile!.Id + ".json");
        var operation = WriteOperation(client.ClientPath, target, [Path.GetRelativePath(_root, profile)], [File.ReadAllBytes(profile)]);
        if (missing) File.Delete(Path.Combine(operation, "0.json"));
        else File.WriteAllText(Path.Combine(operation, "0.json"), "{");
        Assert.ThrowsAny<IOException>(() => new EntityManagementService(_root).RecoverPendingOperations());
        Assert.False(Directory.Exists(client.ClientPath));
        Assert.True(Directory.Exists(target));
        Assert.True(File.Exists(Path.Combine(operation, "journal.json")));
    }

    [Fact]
    public void Recovery_BackupMissingRequiredClientFieldsDoesNotMutateAnything()
    {
        var (client, _) = CreateFixture();
        var target = Path.Combine(_root, "01_CLIENTS", "RENAMED");
        Directory.Move(client.ClientPath, target);
        var profile = Path.Combine(_root, ".ms3dprint-manager", "clients", client.Profile!.Id + ".json");
        var bytes = File.ReadAllBytes(profile);
        var json = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        json["PrimaryContact"] = null;
        WriteOperation(client.ClientPath, target, [Path.GetRelativePath(_root, profile)], [System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())]);
        Assert.ThrowsAny<IOException>(() => new EntityManagementService(_root).RecoverPendingOperations());
        Assert.False(Directory.Exists(client.ClientPath));
        Assert.True(Directory.Exists(target));
        Assert.Equal(bytes, File.ReadAllBytes(profile));
    }

    [Fact]
    public void Recovery_FinalizedJournalCleanupIsBestEffortAndRetryable()
    {
        var (client, _) = CreateFixture();
        var profile = Path.Combine(_root, ".ms3dprint-manager", "clients", client.Profile!.Id + ".json");
        var operation = WriteOperation(client.ClientPath, Path.Combine(_root, "01_CLIENTS", "RENAMED"), [Path.GetRelativePath(_root, profile)], [File.ReadAllBytes(profile)], committed: true);
        using (var locked = new FileStream(Path.Combine(operation, "0.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            new EntityManagementService(_root).RecoverPendingOperations();
            Assert.True(File.Exists(Path.Combine(operation, "journal.json")));
            Assert.True(Directory.Exists(client.ClientPath));
        }
        new EntityManagementService(_root).RecoverPendingOperations();
        Assert.False(Directory.Exists(operation));
    }

    [Fact]
    public void Recovery_LockedLaterProfilePreflightsAllDestinations()
    {
        var (client, project) = CreateFixture();
        var target = Path.Combine(_root, "01_CLIENTS", "RENAMED");
        Directory.Move(client.ClientPath, target);
        var clientProfile = Path.Combine(_root, ".ms3dprint-manager", "clients", client.Profile!.Id + ".json");
        var projectProfile = Path.Combine(_root, ".ms3dprint-manager", "projects", project.Profile!.Id + ".json");
        var before = File.ReadAllBytes(clientProfile);
        WriteOperation(client.ClientPath, target, [Path.GetRelativePath(_root, clientProfile), Path.GetRelativePath(_root, projectProfile)], [before, File.ReadAllBytes(projectProfile)]);
        using var locked = new FileStream(projectProfile, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.ThrowsAny<IOException>(() => new EntityManagementService(_root).RecoverPendingOperations());
        Assert.False(Directory.Exists(client.ClientPath));
        Assert.True(Directory.Exists(target));
        Assert.Equal(before, File.ReadAllBytes(clientProfile));
    }

    [Fact]
    public void RenameLegacyCollection_CollisionDoesNotAdoptProfile()
    {
        var category = Path.Combine(_root, "02_MODELES_3D");
        var source = Directory.CreateDirectory(Path.Combine(category, "LEGACY")).FullName;
        Directory.CreateDirectory(Path.Combine(category, "EXISTING"));
        var item = new MS3DPRINT.Manager.Core.Collections.CollectionItemSummary("LEGACY", source, DateTimeOffset.UtcNow);
        Assert.ThrowsAny<IOException>(() => new EntityManagementService(_root).RenameCollection(item, "02_MODELES_3D", "EXISTING"));
        Assert.Empty(new MS3DPRINT.Manager.Core.Collections.CollectionProfileStore(_root).LoadAll());
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public void MovePath_ClientGroupingCannotBeMovedInsideAnotherClient()
    {
        var (client, _) = CreateFixture();
        var group = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "GROUP")).FullName;
        var service = new EntityManagementService(_root);
        service.MoveClient(client, group);
        var destination = CreateClient("SECOND", "SEC");
        Assert.ThrowsAny<ArgumentException>(() => service.MovePath(group, destination.ClientPath));
        Assert.True(Directory.Exists(Path.Combine(group, "ACME")));
    }

    [Fact]
    public void RenamePath_LegacyClientRequiresProfileBeforeMutation()
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "LEGACY")).FullName;
        Assert.ThrowsAny<ArgumentException>(() => new EntityManagementService(_root).RenamePath(path, "RENAMED"));
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void RenamePath_LegacyCollectionAdoptsProfileAndUpdatesItsPath()
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "LEGACY")).FullName;
        var moved = new EntityManagementService(_root).RenamePath(path, "RENAMED");
        var profile = Assert.Single(new MS3DPRINT.Manager.Core.Collections.CollectionProfileStore(_root).LoadAll());
        Assert.Equal("RENAMED", profile.Name);
        Assert.Equal(Path.GetRelativePath(_root, moved), profile.RelativePath);
    }

    [Fact]
    public void RenameClient_LockedProjectProfileRefusesBeforeMovingDocuments()
    {
        var (client, project) = CreateFixture();
        var profile = Path.Combine(_root, ".ms3dprint-manager", "projects", project.Profile!.Id + ".json");
        using var locked = new FileStream(profile, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.ThrowsAny<IOException>(() => new EntityManagementService(_root).RenameClient(client, "RENAMED"));
        Assert.True(Directory.Exists(client.ClientPath));
        Assert.Equal("ACME", new ClientProfileStore(new(_root)).Load(client.Profile!.Id).FolderName);
    }

    private string WriteOperation(string source, string target, string[] profiles, IReadOnlyList<byte[]> backups, bool committed = false)
    {
        var operation = Directory.CreateDirectory(Path.Combine(_root, ".ms3dprint-manager", "operations", Guid.NewGuid().ToString("N"))).FullName;
        for (var i = 0; i < backups.Count; i++) File.WriteAllBytes(Path.Combine(operation, i + ".json"), backups[i]);
        File.WriteAllText(Path.Combine(operation, "journal.json"), System.Text.Json.JsonSerializer.Serialize(new { Source = Path.GetRelativePath(_root, source), Target = Path.GetRelativePath(_root, target), Profiles = profiles, Committed = committed }));
        return operation;
    }

    private (ClientSummary Client, ProjectSummary Project) CreateFixture()
    {
        var client = CreateClient("ACME", "ACM");
        var path = Directory.CreateDirectory(Path.Combine(client.ClientPath, "ACM-2026-001_TEST")).FullName;
        File.WriteAllText(Path.Combine(path, "test.txt"), "document");
        var now = DateTimeOffset.UtcNow;
        new ProjectProfileStore(new(_root)).Create(new(Guid.NewGuid(), client.Profile!.Id, "ACM", "ACM-2026-001", "ACM-2026-001_TEST", "TEST", ProjectStatus.Quote, now, null, null, null, now));
        return (client, Projects().Load(_root).Single());
    }

    private ClientSummary CreateClient(string name, string code)
    {
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", name));
        var now = DateTimeOffset.UtcNow;
        var store = new ClientProfileStore(new(_root));
        store.Create(new(Guid.NewGuid(), ClientKind.Professional, name, code, name, null, null, null, null, new(null, null, null, null, null), now, now));
        return Clients().Load(_root).Single(client => client.FolderName == name);
    }

    private ClientCatalog Clients() => new(new ClientProfileStore(new(_root)));
    private ProjectCatalog Projects() => new(new ProjectProfileStore(new(_root)));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
