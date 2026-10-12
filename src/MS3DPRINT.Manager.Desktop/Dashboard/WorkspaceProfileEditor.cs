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
        string? notes,
        string? projectName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(profile);
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(workspaceRoot));
        return store.Update(profile with
        {
            Status = status,
            DueDate = dueDate,
            Description = Normalize(description),
            Notes = Normalize(notes),
            // the displayed name only: the folder keeps its name (no rename on disk)
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? profile.ProjectName : projectName.Trim()
        }, profile.UpdatedAt);
    }

    public ClientProfile UpdateClient(string workspaceRoot, ClientProfile profile, string? address, string? notes,
        string? firstName, string? lastName, string? role, string? phone, string? email,
        string? companyName = null, string? siret = null, string? individualFirstName = null, string? individualLastName = null, string? mobile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(profile);
        var store = new ClientProfileStore(new WorkspaceMetadataPaths(workspaceRoot));
        return store.Update(profile with
        {
            Address = Normalize(address), Notes = Normalize(notes),
            CompanyName = profile.Kind == ClientKind.Professional ? Normalize(companyName) ?? profile.CompanyName : null,
            Siret = profile.Kind == ClientKind.Professional ? Normalize(siret) : null,
            FirstName = profile.Kind == ClientKind.Individual ? Normalize(individualFirstName) ?? profile.FirstName : null,
            LastName = profile.Kind == ClientKind.Individual ? Normalize(individualLastName) ?? profile.LastName : null,
            PrimaryContact = new PrimaryContact(Normalize(firstName), Normalize(lastName), Normalize(role), Normalize(phone), Normalize(email), Normalize(mobile))
        }, profile.UpdatedAt);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
