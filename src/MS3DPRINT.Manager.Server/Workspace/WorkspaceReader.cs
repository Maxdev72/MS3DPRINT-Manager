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
            profile.UpdatedAt)).ToArray();

    public IReadOnlyList<ProjectResponse> ReadProjects() => new ProjectProfileStore(new WorkspaceMetadataPaths(storage.WorkspaceRoot))
        .LoadAll().Select(profile => new ProjectResponse(profile.Id, profile.ClientId, profile.Reference, profile.ProjectName,
            profile.Status.ToString(), profile.DueDate, profile.Priority.ToString(), profile.UpdatedAt, ProjectTasks.Items(profile))).ToArray();
}
