using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class ProfileSyncTests
{
    [Fact]
    public async Task Sheets_changed_on_the_pc_reach_the_server_and_server_changes_come_back()
    {
        using var host = new ServerFixture();
        host.CreateOwner();
        var local = Path.Combine(host.Root, "pc");
        var (client, project) = MigrationTests.Seed(local);
        CopyTree(local, host.Workspace);
        using var http = new HttpClient(host.Server.CreateHandler()) { BaseAddress = new Uri("https://localhost") };
        var remote = new RemoteWorkspaceClient(http, http.BaseAddress);
        await remote.LoginAsync("owner", host.Password);
        var sync = new RemoteProfileSync(local);
        Task<ProfileSyncResult> Run() => Task.Run(async () => await sync.RunAsync(await remote.GetClientsAsync(), await remote.GetProjectsAsync(),
            (id, c) => remote.UpdateClientAsync(id, c), (id, c) => remote.UpdateProjectAsync(id, c)));

        Assert.Equal(new ProfileSyncResult(0, 0, 0), await Run());

        // PC → server: status, a task and a client address.
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(local));
        await Task.Delay(20);
        ProjectTasks.Add(store, store.LoadAll().Single() with { Status = ProjectStatus.Completed }, "Poncer", null);
        var clients = new ClientProfileStore(new WorkspaceMetadataPaths(local));
        clients.Update(clients.Load(client.Id) with { Address = "1 rue du Port" });
        Assert.Equal(new ProfileSyncResult(0, 2, 0), await Run());
        var onServer = await remote.GetProjectAsync(project.Id);
        Assert.Equal(ProjectStatus.Completed, onServer.Status);
        Assert.Equal("Poncer", Assert.Single(onServer.Tasks).Title);
        Assert.Equal("1 rue du Port", (await remote.GetClientAsync(client.Id)).Address);
        Assert.Equal(new ProfileSyncResult(0, 0, 0), await Run());

        // Server (website, other device) → PC: the website form leaves the tasks alone.
        await remote.UpdateProjectAsync(project.Id, new(Guid.NewGuid(), onServer.UpdatedAt, ProjectStatus.InProgress, new DateOnly(2026, 12, 1), "Pièce", null, ProjectPriority.High));
        Assert.Equal(new ProfileSyncResult(1, 0, 0), await Run());
        var pulled = store.LoadAll().Single();
        Assert.Equal(ProjectStatus.InProgress, pulled.Status);
        Assert.Equal(new DateOnly(2026, 12, 1), pulled.DueDate);
        Assert.Equal(ProjectPriority.High, pulled.Priority);
        Assert.Equal("Poncer", Assert.Single(ProjectTasks.Items(pulled)).Title);
        Assert.Equal(new ProfileSyncResult(0, 0, 0), await Run());
    }

    [Fact]
    public async Task Clients_and_projects_created_on_the_pc_are_created_on_the_server_with_the_same_identity()
    {
        using var host = new ServerFixture();
        host.CreateOwner();
        var local = Path.Combine(host.Root, "pc");
        MigrationTests.Seed(local);
        CopyTree(local, host.Workspace);
        using var http = new HttpClient(host.Server.CreateHandler()) { BaseAddress = new Uri("https://localhost") };
        var remote = new RemoteWorkspaceClient(http, http.BaseAddress);
        await remote.LoginAsync("owner", host.Password);
        var sync = new RemoteProfileSync(local);
        Task<ProfileSyncResult> Run() => Task.Run(async () => await sync.RunAsync(await remote.GetClientsAsync(), await remote.GetProjectsAsync(),
            (id, c) => remote.UpdateClientAsync(id, c), (id, c) => remote.UpdateProjectAsync(id, c),
            c => remote.CreateClientAsync(c), c => remote.CreateProjectAsync(c)));

        var paths = new WorkspaceMetadataPaths(local);
        var now = DateTimeOffset.UtcNow;
        var client = new ClientCreationService(local, new FolderTreeService(), new ClientProfileStore(paths), new ClientCodeRegistry(Path.Combine(local, "data"))).Create(
            new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "ATELIER_NEUF", "NEU", "Atelier Neuf", null, null, "2 rue Neuve", null,
                new PrimaryContact(null, null, null, null, "a@b.fr", null), now, now, "12345678900011", Path.Combine("01_CLIENTS", "ATELIER_NEUF")));
        var summary = new ClientSummary(Path.Combine(local, "01_CLIENTS", "ATELIER_NEUF"), client.FolderName, "Atelier Neuf", client.ClientCode, client.Kind, client, 0);
        var store = new ProjectProfileStore(paths);
        var (_, project) = new ProjectCreationService(new FolderTreeService()).CreateWithProfile(summary, 2026, "Support", store, null, "Pièce");
        ProjectTasks.Add(store, store.LoadAll().Single(p => p.Id == project.Id) with { Status = ProjectStatus.InProgress }, "Imprimer", null);

        Assert.Equal(new ProfileSyncResult(0, 2, 0), await Run());
        var serverClient = await remote.GetClientAsync(client.Id);
        Assert.Equal(client.ClientCode, serverClient.ClientCode);
        Assert.Equal(client.FolderName, serverClient.FolderName);
        Assert.Equal("12345678900011", serverClient.Siret);
        var serverProject = await remote.GetProjectAsync(project.Id);
        Assert.Equal(project.Reference, serverProject.Reference);
        Assert.Equal(project.FolderName, serverProject.FolderName);
        Assert.Equal(ProjectStatus.InProgress, serverProject.Status);
        Assert.Equal("Imprimer", Assert.Single(serverProject.Tasks).Title);
        Assert.True(Directory.Exists(Path.Combine(host.Workspace, "01_CLIENTS", client.FolderName, project.FolderName)));

        Assert.Equal(new ProfileSyncResult(0, 0, 0), await Run());
        var kept = store.LoadAll().Single(p => p.Id == project.Id);
        Assert.Equal(ProjectStatus.InProgress, kept.Status);
        Assert.Equal("Imprimer", Assert.Single(ProjectTasks.Items(kept)).Title);
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
    }
}
