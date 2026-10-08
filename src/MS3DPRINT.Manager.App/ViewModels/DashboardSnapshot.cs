using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed record DashboardSnapshot(
    int Clients,
    int Projects,
    int Quotes,
    int InProgress,
    int Completed,
    IReadOnlyList<DashboardProjectRow> RecentProjects,
    IReadOnlyList<DashboardAlert> Alerts)
{
    public static DashboardSnapshot Create(IReadOnlyList<ClientSummary> clients, IReadOnlyList<ProjectSummary> projects, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(projects);

        var currentDay = today ?? DateOnly.FromDateTime(DateTime.Today);
        var recent = projects
            .OrderByDescending(project => project.Profile?.UpdatedAt ?? DateTimeOffset.MinValue)
            .Take(5)
            .Select(project => new DashboardProjectRow(project.Reference, project.ProjectName, project.ClientFolderName, project.StatusLabel))
            .ToArray();
        var alerts = projects
            .Where(project => project.Profile is null || (project.Profile.DueDate is { } dueDate && dueDate < currentDay && project.Status != ProjectStatus.Completed))
            .OrderBy(project => project.Profile?.DueDate)
            .Select(project => project.Profile is null
                ? new DashboardAlert($"{project.Reference} : fiche projet à compléter.")
                : new DashboardAlert($"{project.Reference} : échéance dépassée ({project.Profile.DueDate:dd/MM/yyyy})."))
            .ToArray();

        return new DashboardSnapshot(
            clients.Count,
            projects.Count,
            projects.Count(project => project.Status == ProjectStatus.Quote),
            projects.Count(project => project.Status == ProjectStatus.InProgress),
            projects.Count(project => project.Status == ProjectStatus.Completed),
            recent,
            alerts);
    }
}

public sealed record DashboardProjectRow(string Reference, string ProjectName, string ClientCode, string StatusLabel);

public sealed record DashboardAlert(string Message);
