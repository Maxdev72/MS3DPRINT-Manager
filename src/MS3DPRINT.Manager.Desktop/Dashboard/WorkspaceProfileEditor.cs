using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Persists the editable information exposed by the dark desktop workspace.</summary>
public sealed class WorkspaceProfileEditor
{
    public ProjectProfile UpdateProject(
        string workspaceRoot,
        ProjectProfile profile,
        ProjectStatus status,
        DateOnly? dueDate,
        string? description,
        string? notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(profile);
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(workspaceRoot));
        return store.Update(profile with
        {
            Status = status,
            DueDate = dueDate,
            Description = Normalize(description),
            Notes = Normalize(notes)
        }, profile.UpdatedAt);
    }

    public ClientProfile UpdateClient(string workspaceRoot, ClientProfile profile, string? address, string? notes,
        string? firstName, string? lastName, string? role, string? phone, string? email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(profile);
        var store = new ClientProfileStore(new WorkspaceMetadataPaths(workspaceRoot));
        return store.Update(profile with
        {
            Address = Normalize(address), Notes = Normalize(notes),
            PrimaryContact = new PrimaryContact(Normalize(firstName), Normalize(lastName), Normalize(role), Normalize(phone), Normalize(email))
        }, profile.UpdatedAt);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
