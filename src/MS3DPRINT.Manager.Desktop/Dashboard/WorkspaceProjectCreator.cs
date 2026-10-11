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

    public ProjectReference Create(string clientPath, string clientCode, int year, string projectName)
        => new ProjectCreationService(new FolderTreeService()).Create(clientPath, clientCode, year, projectName);
}
