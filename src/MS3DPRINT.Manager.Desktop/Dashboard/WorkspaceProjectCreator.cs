using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Creates a project through the existing atomic workspace service.</summary>
public sealed class WorkspaceProjectCreator
{
    public ProjectProfile CompleteExisting(string workspaceRoot, ProjectSummary project, ClientProfile client, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName)) throw new ArgumentException("Le nom du projet est obligatoire.", nameof(projectName));
        if (!Directory.Exists(project.ProjectPath)) throw new DirectoryNotFoundException("Le dossier projet historique est introuvable.");
        var now = DateTimeOffset.UtcNow;
        var profile = new ProjectProfile(Guid.NewGuid(), client.Id, client.ClientCode, project.Reference, project.FolderName, projectName.Trim(),
            ProjectStatus.Quote, now, null, null, null, now, Path.GetRelativePath(Path.GetFullPath(workspaceRoot), project.ProjectPath));
        new ProjectProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(workspaceRoot)).Create(profile);
        return profile;
    }

    /// <summary>
    /// Creates the project folders and, when the client has a fiche, the project fiche too, keeping the name as it was typed
    /// (the folder name loses accents, spaces and case; the fiche keeps them). A client without a fiche only gets the folders.
    /// </summary>
    public ProjectReference CreateTracked(string workspaceRoot, ClientSummary client, int year, string projectName)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (client.Profile is null) return Create(client.ClientPath, client.ClientCode, year, projectName);
        var profiles = new ProjectProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(workspaceRoot));
        return new ProjectCreationService(new FolderTreeService()).CreateWithProfile(client, year, projectName, profiles, null, null).Reference;
    }

    public ProjectReference Create(string clientPath, string clientCode, int year, string projectName)
        => new ProjectCreationService(new FolderTreeService()).Create(clientPath, clientCode, year, projectName);
}
