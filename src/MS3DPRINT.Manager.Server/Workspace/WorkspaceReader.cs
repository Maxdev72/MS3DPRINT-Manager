using MS3DPRINT.Manager.Core.Tasks;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server.Workspace;

public sealed class WorkspaceReader(ServerStorage storage)
{
    public IReadOnlyList<ClientResponse> ReadClients() => new ClientProfileStore(new WorkspaceMetadataPaths(storage.WorkspaceRoot))
        .LoadAll().Select(profile => new ClientResponse(profile.Id, profile.Kind.ToString(), profile.ClientCode,
            profile.Kind == ClientKind.Professional ? profile.CompanyName ?? profile.FolderName
                : string.Join(" ", new[] { profile.FirstName, profile.LastName }.Where(value => !string.IsNullOrWhiteSpace(value))),
            profile.UpdatedAt, profile.CompanyName, profile.FirstName, profile.LastName, profile.Address, profile.Notes, profile.PrimaryContact)).ToArray();

    public IReadOnlyList<ProjectResponse> ReadProjects() => new ProjectProfileStore(new WorkspaceMetadataPaths(storage.WorkspaceRoot))
        .LoadAll().Select(profile => new ProjectResponse(profile.Id, profile.ClientId, profile.Reference, profile.ProjectName,
            profile.Status.ToString(), profile.DueDate, profile.Priority.ToString(), profile.UpdatedAt, ProjectTasks.Items(profile),
            profile.Description, profile.Notes, FullSheet: true)).ToArray();

    public IReadOnlyList<WorkTask> ReadTasks() => new TaskStore(new WorkspaceMetadataPaths(storage.WorkspaceRoot)).LoadAll();

    public IReadOnlyList<TaskListEntry> ReadTaskLists() => new TaskListStore(new WorkspaceMetadataPaths(storage.WorkspaceRoot)).LoadAll();

    /// <summary>Merges list names by date and returns every list the server holds afterwards.</summary>
    public IResult MergeTaskLists(TaskListEntry[]? input)
    {
        if (input is null || input.Length > 500) return Results.BadRequest();
        var store = new TaskListStore(new WorkspaceMetadataPaths(storage.WorkspaceRoot));
        store.Merge(input);
        return Results.Ok(store.LoadAll());
    }

    /// <summary>Keeps the latest copy and returns the one the server holds afterwards.</summary>
    public IResult MergeTask(Guid id, WorkTask? input)
    {
        if (input is null || input.Id != id) return Results.BadRequest();
        var store = new TaskStore(new WorkspaceMetadataPaths(storage.WorkspaceRoot));
        try { store.Merge(input); }
        catch (ArgumentException) { return Results.BadRequest(); }
        return Results.Ok(store.Find(id));
    }
}
