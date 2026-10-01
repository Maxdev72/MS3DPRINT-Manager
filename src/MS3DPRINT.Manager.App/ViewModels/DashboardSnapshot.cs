using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed record DashboardSnapshot(int Clients, int Projects, int Quotes, int InProgress, int Completed)
{
    public static DashboardSnapshot Create(IReadOnlyList<ClientSummary> clients, IReadOnlyList<ProjectSummary> projects)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(projects);

        return new DashboardSnapshot(
            clients.Count,
            projects.Count,
            projects.Count(project => project.Status == ProjectStatus.Quote),
            projects.Count(project => project.Status == ProjectStatus.InProgress),
            projects.Count(project => project.Status == ProjectStatus.Completed));
    }
}
