using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceDashboardReaderTests
{
    [Fact]
    public void Work_queue_promotes_high_priority_without_hiding_urgent_deadlines()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3dprint-priority-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            new WorkspaceClientCreator().CreateProfessional(root, "Atelier test", "TEST");
            var client = new WorkspaceClientReader().Load(root).First();
            foreach (var name in new[] { "Haute sans date", "Fabrication", "En retard", "Aujourd’hui basse", "Aujourd’hui haute", "Sans priorité", "Terminé haute", "Archivé haute" })
                new WorkspaceProjectCreator().CreateTracked(root, client, 2026, name);
            var store = new MS3DPRINT.Manager.Core.Projects.ProjectProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(root));
            var today = DateOnly.FromDateTime(DateTime.Today);
            foreach (var project in new WorkspaceProjectReader().Load(root))
            {
                var p = project.Profile!;
                store.Update(p with
                {
                    Priority = p.ProjectName.Contains("haute", StringComparison.OrdinalIgnoreCase) ? MS3DPRINT.Manager.Core.Projects.ProjectPriority.High : MS3DPRINT.Manager.Core.Projects.ProjectPriority.Low,
                    Status = p.ProjectName == "Fabrication" ? MS3DPRINT.Manager.Core.Projects.ProjectStatus.InProgress : p.ProjectName == "Terminé haute" ? MS3DPRINT.Manager.Core.Projects.ProjectStatus.Completed : MS3DPRINT.Manager.Core.Projects.ProjectStatus.Quote,
                    DueDate = p.ProjectName == "En retard" ? today.AddDays(-1) : p.ProjectName.StartsWith("Aujourd’hui") ? today : null
                });
            }
            new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).ArchiveProject(new WorkspaceProjectReader().Load(root).Single(p => p.Profile!.ProjectName == "Archivé haute"));
            var dashboard = new WorkspaceDashboardReader().Load(root);
            Assert.Equal(new[] { "En retard", "Aujourd’hui haute", "Aujourd’hui basse", "Haute sans date", "Fabrication" }, dashboard.WorkQueue.Select(p => p.Profile!.ProjectName));
            Assert.Equal(new[] { "En retard", "Aujourd’hui haute", "Aujourd’hui basse" }, dashboard.Deadlines.Select(p => p.Profile!.ProjectName));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Work_queue_keeps_active_projects_ahead_of_recently_completed_projects()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3dprint-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            new WorkspaceClientCreator().CreateProfessional(root, "Atelier test", "TEST");
            var client = new WorkspaceClientReader().Load(root).First();
            foreach (var name in new[] { "En retard", "À lancer", "En fabrication", "Terminé A", "Terminé B", "Terminé C", "Archivé actif" })
                new WorkspaceProjectCreator().CreateTracked(root, client, 2026, name);
            var store = new MS3DPRINT.Manager.Core.Projects.ProjectProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(root));
            foreach (var project in new WorkspaceProjectReader().Load(root))
            {
                var profile = project.Profile!;
                var status = profile.ProjectName.StartsWith("Terminé") ? MS3DPRINT.Manager.Core.Projects.ProjectStatus.Completed
                    : profile.ProjectName == "En fabrication" ? MS3DPRINT.Manager.Core.Projects.ProjectStatus.InProgress : MS3DPRINT.Manager.Core.Projects.ProjectStatus.Quote;
                store.Update(profile with { Status = status, DueDate = profile.ProjectName is "En retard" or "Archivé actif" ? DateOnly.FromDateTime(DateTime.Today.AddDays(-1)) : null });
            }
            new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).ArchiveProject(new WorkspaceProjectReader().Load(root).Single(p => p.Profile!.ProjectName == "Archivé actif"));
            var dashboard = new WorkspaceDashboardReader().Load(root);
            var property = typeof(WorkspaceDashboard).GetProperty("WorkQueue");
            Assert.NotNull(property);
            var queue = Assert.IsAssignableFrom<IReadOnlyList<MS3DPRINT.Manager.Core.Projects.ProjectSummary>>(property.GetValue(dashboard));
            Assert.Equal(new[] { "En retard", "En fabrication", "À lancer" }, queue.Select(p => p.Profile!.ProjectName));
            Assert.DoesNotContain(dashboard.Deadlines, p => p.IsArchived);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Load_returns_an_empty_dashboard_without_creating_workspace_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-dashboard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var dashboard = new WorkspaceDashboardReader().Load(root);

            Assert.Equal(0, dashboard.ClientCount);
            Assert.Equal(0, dashboard.ProjectCount);
            Assert.Empty(dashboard.RecentProjects);
            Assert.False(Directory.Exists(Path.Combine(root, ".ms3dprint-manager")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
