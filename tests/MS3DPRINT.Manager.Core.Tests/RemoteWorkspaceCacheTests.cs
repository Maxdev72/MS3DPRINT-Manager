using System.Text.Json;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class RemoteWorkspaceCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "remote-copy-" + Guid.NewGuid().ToString("N"));
    private string CachePath => Path.Combine(_directory, "copy.json");

    [Fact]
    public void Copy_survives_reopening_with_pending_command_but_no_authentication_material()
    {
        var pending = Guid.NewGuid();
        new RemoteWorkspaceCache(CachePath).Save(Copy() with { PendingCommand = pending });
        var restored = new RemoteWorkspaceCache(CachePath).Load()!;
        Assert.Equal("https://example.com/", restored.Server);
        Assert.Equal("OWNER", restored.Account);
        Assert.Equal(pending, restored.PendingCommand);
        Assert.Equal("Client fictif", Assert.Single(restored.Clients).DisplayName);
        Assert.Equal("Projet fictif", Assert.Single(restored.Projects).ProjectName);
        using var json = JsonDocument.Parse(File.ReadAllText(CachePath));
        Assert.Equal(new[] { "Account", "Clients", "PendingCommand", "Projects", "ReadAt", "Server" },
            json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("https://example.com/?token=secret")]
    public void Invalid_destination_cannot_replace_last_good_copy(string server)
    {
        var store = new RemoteWorkspaceCache(CachePath); store.Save(Copy());
        var before = File.ReadAllBytes(CachePath);
        Assert.Throws<InvalidDataException>(() => store.Save(Copy() with { Server = server }));
        Assert.Equal(before, File.ReadAllBytes(CachePath));
    }

    [Fact]
    public void Broken_relationship_and_duplicate_identity_are_refused()
    {
        var store = new RemoteWorkspaceCache(CachePath); var copy = Copy();
        Assert.Throws<InvalidDataException>(() => store.Save(copy with { Clients = [] }));
        Assert.Throws<InvalidDataException>(() => store.Save(copy with { Clients = [copy.Clients[0], copy.Clients[0]] }));
        Assert.False(File.Exists(CachePath));
    }

    [Fact]
    public void Corrupt_copy_is_reported_instead_of_presented_as_empty()
    {
        Directory.CreateDirectory(_directory); File.WriteAllText(CachePath, "{broken");
        Assert.Throws<InvalidDataException>(() => new RemoteWorkspaceCache(CachePath).Load());
    }

    [Fact]
    public void Missing_copy_is_distinct_from_saved_empty_workspace_and_clear_forgets_it()
    {
        var store = new RemoteWorkspaceCache(CachePath); Assert.Null(store.Load());
        store.Save(Copy() with { Clients = [], Projects = [] });
        Assert.Empty(store.Load()!.Clients);
        store.Clear(); Assert.Null(store.Load());
    }

    private static RemoteWorkspaceCopy Copy()
    {
        var client = Guid.Parse("01000000-0000-0000-0000-000000000001");
        var project = Guid.Parse("02000000-0000-0000-0000-000000000001");
        var date = DateTimeOffset.Parse("2026-11-03T12:00:00Z");
        return new("https://example.com/", "OWNER", date,
            [new(client, "Individual", "CF", "Client fictif", date)],
            [new(project, client, "CF-2026-001", "Projet fictif", "InProgress", null, "None", date, [])], null);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
