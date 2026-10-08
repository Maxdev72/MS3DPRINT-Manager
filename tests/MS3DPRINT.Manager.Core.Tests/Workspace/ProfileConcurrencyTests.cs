using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Filaments;
using System.Reflection;

namespace MS3DPRINT.Manager.Core.Tests.Workspace;

public sealed class ProfileConcurrencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-profile-concurrency-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("client")]
    [InlineData("project")]
    [InlineData("collection")]
    [InlineData("filament")]
    public void ConditionalUpdate_RejectsStaleVersionAndKeepsLatestDiskValues(string kind)
    {
        var (store, original, latest, stale, read) = Fixture(kind);
        var unconditional = store.GetType().GetMethod("Update", new[] { original.GetType() })!;
        unconditional.Invoke(store, new[] { latest });
        var current = read();
        var method = store.GetType().GetMethod("Update", new[] { original.GetType(), typeof(DateTimeOffset) });
        Assert.NotNull(method);
        var expected = (DateTimeOffset)original.GetType().GetProperty("UpdatedAt")!.GetValue(original)!;
        var error = Record.Exception(() => method.Invoke(store, new[] { stale, (object)expected }));
        Assert.IsAssignableFrom<InvalidOperationException>(Assert.IsType<TargetInvocationException>(error).InnerException);
        Assert.Equal(current, read());
    }

    [Fact]
    public void ClientDraft_ConflictingSavePreservesDraftAndOtherEditorsChanges()
    {
        var (storeObject, originalObject, _, _, _) = Fixture("client");
        var store = (ClientProfileStore)storeObject;
        var original = (ClientProfile)originalObject;
        var first = new ClientDetailViewModel(original, store) { Address = "Nouvelle adresse" };
        var stale = new ClientDetailViewModel(original, store) { Notes = "Brouillon conservé" };
        first.Save();
        Assert.ThrowsAny<InvalidOperationException>(() => stale.Save());
        Assert.Equal("Nouvelle adresse", store.Load(original.Id).Address);
        Assert.Null(store.Load(original.Id).Notes);
        Assert.Equal("Brouillon conservé", stale.Notes);
        Assert.True(stale.HasUnsavedChanges);
        first.Notes = "Seconde sauvegarde";
        first.Save();
        Assert.Equal("Seconde sauvegarde", store.Load(original.Id).Notes);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("project")]
    [InlineData("collection")]
    [InlineData("filament")]
    public async Task ConditionalUpdate_TwoStoreInstancesWithSameSnapshotAllowOnlyOneWriter(string kind)
    {
        var (store, original, firstDraft, secondDraft, _) = Fixture(kind);
        var method = store.GetType().GetMethod("Update", new[] { original.GetType(), typeof(DateTimeOffset) });
        Assert.NotNull(method);
        var otherStore = Activator.CreateInstance(store.GetType(), kind is "client" or "project"
            ? new object[] { new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(_root) } : new object[] { _root })!;
        var expected = (DateTimeOffset)original.GetType().GetProperty("UpdatedAt")!.GetValue(original)!;
        using var start = new Barrier(2);
        Exception? Write(object target, object draft)
        {
            start.SignalAndWait();
            return Record.Exception(() => method.Invoke(target, new[] { draft, (object)expected }));
        }
        var first = Task.Run(() => Write(store, firstDraft));
        var second = Task.Run(() => Write(otherStore, secondDraft));
        var results = await Task.WhenAll(first, second);
        Assert.Single(results.Where(error => error is null));
        Assert.IsAssignableFrom<InvalidOperationException>(Assert.IsType<TargetInvocationException>(Assert.Single(results.Where(error => error is not null))).InnerException);
    }

    [Fact]
    public void ProjectDraft_ConflictingSavePreservesDraftAndOtherEditorsChanges()
    {
        var (storeObject, originalObject, _, _, _) = Fixture("project");
        var store = (ProjectProfileStore)storeObject;
        var original = (ProjectProfile)originalObject;
        var first = new ProjectDetailViewModel(original, store) { Status = ProjectStatus.Completed };
        var stale = new ProjectDetailViewModel(original, store) { Description = "Brouillon conservé" };
        first.Save();
        Assert.ThrowsAny<InvalidOperationException>(() => stale.Save());
        Assert.Equal(ProjectStatus.Completed, store.LoadAll().Single().Status);
        Assert.Equal("Brouillon conservé", stale.Description);
        Assert.True(stale.HasUnsavedChanges);
        first.Notes = "Seconde sauvegarde";
        first.Save();
        Assert.Equal("Seconde sauvegarde", store.LoadAll().Single().Notes);
    }

    private (object Store, object Original, object Latest, object Stale, Func<object> Read) Fixture(string kind)
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        switch (kind)
        {
            case "client":
                var clients = new ClientProfileStore(new(_root));
                var client = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "ACME", "ACM", "Acme", null, null, null, null, new(null, null, null, null, null), now, now);
                clients.Create(client);
                return (clients, client, client with { Address = "latest", UpdatedAt = now.AddSeconds(1) }, client with { Notes = "stale", UpdatedAt = now.AddSeconds(2) }, () => clients.Load(client.Id));
            case "project":
                var projects = new ProjectProfileStore(new(_root));
                var project = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "ACM", "ACM-2026-001", "ACM-2026-001_TEST", "TEST", ProjectStatus.Quote, now, null, null, null, now);
                projects.Create(project);
                return (projects, project, project with { Status = ProjectStatus.Completed, UpdatedAt = now.AddSeconds(1) }, project with { Notes = "stale", UpdatedAt = now.AddSeconds(2) }, () => projects.LoadAll().Single());
            case "collection":
                Directory.CreateDirectory(Path.Combine(_root, "06_FOURNISSEURS", "ACME"));
                var collections = new CollectionProfileStore(_root);
                var collection = new CollectionProfile(Guid.NewGuid(), "06_FOURNISSEURS", "06_FOURNISSEURS/ACME", "Acme", null, null, null, null, null, null, null, now, now);
                collections.Create(collection);
                return (collections, collection, collection with { Address = "latest", UpdatedAt = now.AddSeconds(1) }, collection with { Notes = "stale", UpdatedAt = now.AddSeconds(2) }, () => collections.Load(collection.Id)!);
            default:
                var filaments = new FilamentStore(_root);
                var filament = new FilamentProfile(Guid.NewGuid(), "Acme", "Test", "PLA", 24.991m, false, now, now);
                filaments.Create(filament);
                return (filaments, filament, filament with { Brand = "latest", UpdatedAt = now.AddSeconds(1) }, filament with { Name = "stale", UpdatedAt = now.AddSeconds(2) }, () => filaments.LoadAll().Single());
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
