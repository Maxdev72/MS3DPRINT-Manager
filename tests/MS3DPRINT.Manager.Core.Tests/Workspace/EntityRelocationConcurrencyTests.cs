using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Workspace;

public sealed class EntityRelocationConcurrencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-relocation-concurrency-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Rename_BlocksBehindProfileMutationAndPreservesItsNewerFields()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "ACME")).FullName;
        File.WriteAllText(Path.Combine(source, "document.txt"), "document");
        var store = new ClientProfileStore(new(_root));
        var now = DateTimeOffset.UtcNow;
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "ACME", "ACM", "ACME", null, null, null, null, new(null, null, null, null, null), now, now);
        store.Create(profile);
        var client = new ClientCatalog(store).Load(_root).Single();
        var path = Path.Combine(_root, ".ms3dprint-manager", "clients", profile.Id + ".json");
        Exception? failure = null;
        var worker = new Thread(() => { try { new EntityManagementService(_root).RenameClient(client, "RENAMED"); } catch (Exception exception) { failure = exception; } });
        using (ProfileMutationLock.Acquire(path))
        {
            worker.Start();
            var operationRoot = Path.Combine(_root, ".ms3dprint-manager", "operations");
            Assert.True(SpinWait.SpinUntil(() => Directory.Exists(operationRoot) && Directory.GetDirectories(operationRoot).Length > 0, TimeSpan.FromSeconds(5)));
            store.Update(profile with { CompanyName = "NEWER COMPANY" }, profile.UpdatedAt);
        }
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<ProfileConflictException>(failure);
        Assert.Equal("NEWER COMPANY", store.Load(profile.Id).CompanyName);
        Assert.True(Directory.Exists(source));
        Assert.Equal("document", File.ReadAllText(Path.Combine(source, "document.txt")));
        Assert.False(Directory.Exists(Path.Combine(_root, "01_CLIENTS", "RENAMED")));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
