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
/// Items are created in Manager, then sent: a client or project the server does not know yet is created there
/// with the same Id, code, reference and folder name. Items that exist on the server only are left alone.
/// </summary>
public sealed class RemoteProfileSync(string workspaceRoot)
{
    private readonly WorkspaceMetadataPaths _paths = new(workspaceRoot);
    // A push the server refused is not retried every 10 s while neither side changes.
    private readonly HashSet<(Guid, DateTimeOffset, DateTimeOffset)> _refused = [];

    public async Task<ProfileSyncResult> RunAsync(IReadOnlyList<ClientResponse> serverClients, IReadOnlyList<ProjectResponse> serverProjects,
        Func<Guid, UpdateClientCommand, Task<ClientDetailResponse>> updateClient,
        Func<Guid, UpdateProjectCommand, Task<ProjectDetailResponse>> updateProject,
        Func<CreateClientCommand, Task<ClientDetailResponse>>? createClient = null,
        Func<CreateProjectCommand, Task<ProjectDetailResponse>>? createProject = null)
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
        var onServer = serverClients.Select(c => c.Id).ToHashSet();
        // shortcut: a client deleted by hand on the server comes back from the PC; the server has no client deletion yet.
        foreach (var local in localClients.Values.Where(c => !onServer.Contains(c.Id)))
        {
            if (createClient is null || _refused.Contains((local.Id, local.UpdatedAt, default))) continue;
            try
            {
                await createClient(new(Guid.NewGuid(), local.Kind, local.ClientCode, local.CompanyName, local.FirstName, local.LastName,
                    local.Address, local.Notes, local.PrimaryContact, local.Id, local.FolderName, local.Siret)).ConfigureAwait(false);
                onServer.Add(local.Id);
                pushed++;
            }
            catch (RemoteWorkspaceException e) when (e.Kind is RemoteFailure.Rejected or RemoteFailure.Conflict)
            { _refused.Add((local.Id, local.UpdatedAt, default)); failed++; }
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
        var projectsOnServer = serverProjects.Select(p => p.Id).ToHashSet();
        foreach (var local in localProjects.Values.Where(p => !projectsOnServer.Contains(p.Id) && onServer.Contains(p.ClientId)))
        {
            if (createProject is null || _refused.Contains((local.Id, local.UpdatedAt, default))) continue;
            try
            {
                var created = await createProject(new(Guid.NewGuid(), local.ClientId, local.ProjectName, local.CreatedAt.Year, local.DueDate,
                    local.Description, local.Priority, local.Id, local.Reference, local.FolderName)).ConfigureAwait(false);
                // The server starts a project at "À faire" without notes or tasks: send the rest of the sheet at once,
                // otherwise the next pass would see the server as newer and wipe them on the PC.
                var detail = await updateProject(local.Id, new(Guid.NewGuid(), created.UpdatedAt, local.Status, local.DueDate, local.Description,
                    local.Notes, local.Priority, ProjectTasks.Items(local))).ConfigureAwait(false);
                Save(() => projects.Update(local with { UpdatedAt = detail.UpdatedAt }, local.UpdatedAt));
                pushed++;
            }
            catch (RemoteWorkspaceException e) when (e.Kind is RemoteFailure.Rejected or RemoteFailure.Conflict)
            { _refused.Add((local.Id, local.UpdatedAt, default)); failed++; }
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
