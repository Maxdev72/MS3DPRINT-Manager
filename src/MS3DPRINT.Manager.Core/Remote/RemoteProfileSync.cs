using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Remote;

public sealed record ProfileSyncResult(int Pulled, int Pushed, int Failed)
{
    public bool Changed => Pulled + Pushed > 0;
}

/// <summary>
/// Keeps the PC sheets and the server's in step for the items both know (same Id): project status, deadline,
/// priority, description, notes and tasks; client details. The most recently changed side wins.
/// Folders, files and items that exist on one side only are left to the synchronization assistant.
/// </summary>
public sealed class RemoteProfileSync(string workspaceRoot)
{
    private readonly WorkspaceMetadataPaths _paths = new(workspaceRoot);
    // A push the server refused is not retried every 10 s while neither side changes.
    private readonly HashSet<(Guid, DateTimeOffset, DateTimeOffset)> _refused = [];

    public async Task<ProfileSyncResult> RunAsync(IReadOnlyList<ClientResponse> serverClients, IReadOnlyList<ProjectResponse> serverProjects,
        Func<Guid, UpdateClientCommand, Task<ClientDetailResponse>> updateClient,
        Func<Guid, UpdateProjectCommand, Task<ProjectDetailResponse>> updateProject)
    {
        int pulled = 0, pushed = 0, failed = 0;
        var clients = new ClientProfileStore(_paths);
        var localClients = clients.LoadReadable().ToDictionary(c => c.Id);
        foreach (var server in serverClients)
        {
            if (server.Contact is null || !localClients.TryGetValue(server.Id, out var local) || SameClient(local, server)) continue;
            // shortcut: last writer wins by clock; two devices editing the same sheet within seconds keep the later one.
            if (server.UpdatedAt >= local.UpdatedAt)
            {
                if (Save(() => clients.Update(Apply(local, server), local.UpdatedAt))) pulled++; else failed++;
                continue;
            }
            if (_refused.Contains((local.Id, local.UpdatedAt, server.UpdatedAt))) continue;
            try
            {
                var detail = await updateClient(local.Id, new(Guid.NewGuid(), server.UpdatedAt, local.Kind, local.CompanyName, local.FirstName,
                    local.LastName, local.Address, local.Notes, local.PrimaryContact)).ConfigureAwait(false);
                Save(() => clients.Update(local with
                {
                    Kind = detail.Kind, CompanyName = detail.CompanyName, FirstName = detail.FirstName, LastName = detail.LastName,
                    Address = detail.Address, Notes = detail.Notes, PrimaryContact = detail.PrimaryContact, UpdatedAt = detail.UpdatedAt
                }, local.UpdatedAt));
                pushed++;
            }
            catch (RemoteWorkspaceException e) when (e.Kind is RemoteFailure.Rejected or RemoteFailure.Conflict)
            { if (e.Kind == RemoteFailure.Rejected) _refused.Add((local.Id, local.UpdatedAt, server.UpdatedAt)); failed++; }
        }

        var projects = new ProjectProfileStore(_paths);
        var localProjects = projects.LoadReadable().ToDictionary(p => p.Id);
        foreach (var server in serverProjects)
        {
            // An older server omits description and notes: comparing would erase them on the PC.
            if (!server.FullSheet || !localProjects.TryGetValue(server.Id, out var local)
                || !Enum.TryParse<ProjectStatus>(server.Status, out var status) || !Enum.TryParse<ProjectPriority>(server.Priority, out var priority)) continue;
            if (SameProject(local, server, status, priority)) continue;
            if (server.UpdatedAt >= local.UpdatedAt)
            {
                var pulledProfile = local with
                {
                    Status = status, DueDate = server.DueDate, Priority = priority, Description = server.Description,
                    Notes = server.Notes, Tasks = server.Tasks, UpdatedAt = server.UpdatedAt
                };
                if (Save(() => projects.Update(pulledProfile, local.UpdatedAt))) pulled++; else failed++;
                continue;
            }
            if (_refused.Contains((local.Id, local.UpdatedAt, server.UpdatedAt))) continue;
            try
            {
                var detail = await updateProject(local.Id, new(Guid.NewGuid(), server.UpdatedAt, local.Status, local.DueDate, local.Description,
                    local.Notes, local.Priority, ProjectTasks.Items(local))).ConfigureAwait(false);
                Save(() => projects.Update(local with
                {
                    Status = detail.Status, DueDate = detail.DueDate, Priority = detail.Priority, Description = detail.Description,
                    Notes = detail.Notes, Tasks = detail.Tasks, UpdatedAt = detail.UpdatedAt
                }, local.UpdatedAt));
                pushed++;
            }
            catch (RemoteWorkspaceException e) when (e.Kind is RemoteFailure.Rejected or RemoteFailure.Conflict)
            { if (e.Kind == RemoteFailure.Rejected) _refused.Add((local.Id, local.UpdatedAt, server.UpdatedAt)); failed++; }
        }
        return new(pulled, pushed, failed);
    }

    /// <summary>A sheet changed on this PC meanwhile is left for the next pass.</summary>
    private static bool Save(Action write)
    {
        try { write(); return true; }
        catch (ProfileConflictException) { return false; }
    }

    private static ClientProfile Apply(ClientProfile local, ClientResponse server) => local with
    {
        Kind = Enum.TryParse<ClientKind>(server.Kind, out var kind) ? kind : local.Kind, CompanyName = server.CompanyName, FirstName = server.FirstName,
        LastName = server.LastName, Address = server.Address, Notes = server.Notes, PrimaryContact = server.Contact!, UpdatedAt = server.UpdatedAt
    };

    private static bool SameClient(ClientProfile local, ClientResponse server)
        => local.Kind.ToString() == server.Kind && local.CompanyName == server.CompanyName && local.FirstName == server.FirstName
           && local.LastName == server.LastName && local.Address == server.Address && local.Notes == server.Notes && local.PrimaryContact == server.Contact;

    private static bool SameProject(ProjectProfile local, ProjectResponse server, ProjectStatus status, ProjectPriority priority)
        => local.Status == status && local.DueDate == server.DueDate && local.Priority == priority && local.Description == server.Description
           && local.Notes == server.Notes && ProjectTasks.Items(local).SequenceEqual(server.Tasks);
}
