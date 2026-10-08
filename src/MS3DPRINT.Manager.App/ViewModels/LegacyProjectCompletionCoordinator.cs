using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class LegacyProjectCompletionCoordinator(string root, ClientCatalog clients, ProjectCatalog projects)
{
    public ProjectSummary? Complete(ProjectSummary project, Func<ClientSummary, bool> completeClient, Func<ProjectSummary, ClientSummary, bool> completeProject)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(completeClient);
        ArgumentNullException.ThrowIfNull(completeProject);
        var current = projects.Load(root).FirstOrDefault(candidate => Same(candidate.ProjectPath, project.ProjectPath));
        if (current?.Profile is not null) return current;
        var parent = clients.Load(root).FirstOrDefault(client => Same(client.ClientPath, project.ClientPath))
            ?? new ClientSummary(project.ClientPath, Path.GetFileName(project.ClientPath), Path.GetFileName(project.ClientPath), project.Reference.Split('-', 2)[0], null, null, 1);
        if (parent.Profile is null)
        {
            if (!completeClient(parent)) return null;
            parent = clients.Load(root).FirstOrDefault(client => Same(client.ClientPath, project.ClientPath) && client.Profile is not null)
                ?? throw new InvalidOperationException("La fiche client n’a pas été enregistrée. Les fichiers du projet restent consultables.");
        }
        if (!completeProject(project, parent)) return null;
        return projects.Load(root).FirstOrDefault(candidate => Same(candidate.ProjectPath, project.ProjectPath) && candidate.Profile is not null)
            ?? throw new InvalidOperationException("La fiche projet n’a pas été enregistrée. Les fichiers restent consultables.");
    }
    private static bool Same(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
