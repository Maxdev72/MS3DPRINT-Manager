using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

public sealed record WorkspaceDashboard(
    int ClientCount,
    int ProjectCount,
    int QuoteCount,
    int InProgressCount,
    int CompletedCount,
    IReadOnlyList<ProjectSummary> RecentProjects);

/// <summary>Reads the existing workspace without changing its files or metadata.</summary>
public sealed class WorkspaceDashboardReader
{
    public WorkspaceDashboard Load(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var paths = new WorkspaceMetadataPaths(workspaceRoot);
        var clients = new ClientCatalog(new ClientProfileStore(paths)).Load(paths.RootPath);
        var projects = new ProjectCatalog(new ProjectProfileStore(paths)).Load(paths.RootPath);
        var recent = projects
            .OrderByDescending(project => project.Profile?.UpdatedAt ?? DateTimeOffset.MinValue)
            .Take(5)
            .ToArray();

        return new WorkspaceDashboard(
            clients.Count,
            projects.Count,
            projects.Count(project => project.Status == ProjectStatus.Quote),
            projects.Count(project => project.Status == ProjectStatus.InProgress),
            projects.Count(project => project.Status == ProjectStatus.Completed),
            recent);
    }
}
