using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Tasks;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Tasks;

public sealed class TaskCoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-tasks-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Monday = new(2026, 11, 9);

    public TaskCoreTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("Commander PLA noir demain !haute #Achats", "Commander PLA noir", "2026-11-10", ProjectPriority.High, "Achats")]
    [InlineData("Rappeler Dupont vendredi", "Rappeler Dupont", "2026-11-13", ProjectPriority.None, null)]
    [InlineData("Nettoyer plateau lundi !b", "Nettoyer plateau", "2026-11-09", ProjectPriority.Low, null)]
    [InlineData("Payer facture 15/01 #Administratif", "Payer facture", "2027-01-15", ProjectPriority.None, "Administratif")]
    [InlineData("Imprimer support aujourd'hui !moyenne", "Imprimer support", "2026-11-09", ProjectPriority.Medium, null)]
    [InlineData("Idée de produit", "Idée de produit", null, ProjectPriority.None, null)]
    [InlineData("demain", "demain", null, ProjectPriority.None, null)]
    public void Quick_add_reads_dates_priority_and_list(string text, string title, string? due, ProjectPriority priority, string? list)
    {
        var parsed = TaskQuickAdd.Parse(text, Monday);
        Assert.Equal(new QuickTask(title, due is null ? null : DateOnly.Parse(due), priority, list), parsed);
    }

    [Fact]
    public void Store_refuses_a_stale_edit_and_keeps_a_tombstone_after_removal()
    {
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        var task = store.Create(WorkTask.New("Graisser les axes", 1));
        var edited = store.Update(task with { Priority = ProjectPriority.High });
        Assert.True(edited.UpdatedAt > task.UpdatedAt);
        Assert.Throws<ProfileConflictException>(() => store.Update(task with { Title = "Ancienne copie" }));
        store.Remove(edited);
        Assert.Empty(store.LoadActive());
        Assert.True(Assert.Single(store.LoadAll()).IsDeleted);
    }

    [Fact]
    public void Merge_keeps_the_latest_copy_with_its_own_date()
    {
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        var task = store.Create(WorkTask.New("Livrer", 1));
        Assert.False(store.Merge(task with { Title = "Plus ancien", UpdatedAt = task.UpdatedAt.AddMinutes(-1) }));
        var later = task with { Title = "Plus récent", UpdatedAt = task.UpdatedAt.AddMinutes(1) };
        Assert.True(store.Merge(later));
        Assert.Equal(later, store.Find(task.Id));
    }

    [Fact]
    public void Project_checklists_become_linked_tasks_once()
    {
        var paths = new WorkspaceMetadataPaths(_root); paths.EnsureMetadataDirectories();
        var projects = new ProjectProfileStore(paths);
        var now = DateTimeOffset.UtcNow;
        var done = new ProjectTask(Guid.NewGuid(), "Modéliser", IsCompleted: true);
        var open = new ProjectTask(Guid.NewGuid(), "Livrer", "Avant vendredi");
        var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "AC", "AC-2026-001", "AC-2026-001_SUPPORT", "Support", ProjectStatus.InProgress, now, null, null, null, now, Tasks: [done, open]);
        projects.Create(profile);

        Assert.Equal(2, TaskMigration.FromProjects(_root));
        Assert.Equal(0, TaskMigration.FromProjects(_root));
        var tasks = new TaskStore(paths).LoadActive().OrderBy(t => t.Order).ToArray();
        Assert.Equal(["Modéliser", "Livrer"], tasks.Select(t => t.Title));
        Assert.True(tasks[0].IsCompleted);
        Assert.Equal("Avant vendredi", tasks[1].Notes);
        Assert.All(tasks, t => { Assert.Equal(profile.Id, t.ProjectId); Assert.Equal(profile.ClientId, t.ClientId); });
        Assert.Empty(ProjectTasks.Items(projects.LoadAll().Single()));
        Assert.Equal(open.Id, TaskViews.Next(tasks, profile.Id)!.Id);
    }

    [Fact]
    public void Views_group_open_tasks_by_due_date_and_priority()
    {
        var project = Guid.NewGuid();
        WorkTask T(string title, int? days, ProjectPriority p = ProjectPriority.None, string? list = null, Guid? projectId = null, bool done = false)
            => WorkTask.New(title, 0) with { DueDate = days is { } d ? Monday.AddDays(d) : null, Priority = p, List = list, ProjectId = projectId, CompletedAt = done ? new DateTimeOffset(Monday.ToDateTime(new TimeOnly(12, 0))) : null };
        var tasks = new[]
        {
            T("En retard", -2), T("Aujourd'hui basse", 0, ProjectPriority.Low), T("Aujourd'hui haute", 0, ProjectPriority.High),
            T("Demain", 1, list: "Achats"), T("Semaine", 5, projectId: project), T("Plus tard", 30), T("Sans date", null),
            T("Faite", 0, done: true), WorkTask.New("Retirée", 0) with { DeletedAt = DateTimeOffset.UtcNow }
        };
        var groups = TaskViews.Group(TaskViews.All, tasks, Monday);
        Assert.Equal(["En retard", "Aujourd’hui", "Demain", "7 prochains jours", "Plus tard", "Sans date", "Terminées"], groups.Select(g => g.Title));
        Assert.Equal("Faite", groups[^1].Tasks.Single().Title);
        Assert.Equal(["Aujourd'hui haute", "Aujourd'hui basse"], groups[1].Tasks.Select(t => t.Title));
        Assert.Equal(3, TaskViews.Count(TaskViews.Today, tasks, Monday));
        Assert.Equal(5, TaskViews.Count(TaskViews.Week, tasks, Monday));
        Assert.Equal(["En retard", "Aujourd'hui basse", "Aujourd'hui haute", "Plus tard", "Sans date", "Faite"], tasks.Where(t => TaskViews.Shows(TaskViews.Inbox, t, Monday)).Select(t => t.Title));
        Assert.Equal(5, TaskViews.Count(TaskViews.Inbox, tasks, Monday));
        Assert.Equal("Demain", Assert.Single(tasks, t => TaskViews.Shows(TaskViews.ListView("achats"), t, Monday)).Title);
        Assert.Equal("Semaine", Assert.Single(tasks, t => TaskViews.Shows(TaskViews.ProjectView(project), t, Monday)).Title);
        Assert.Equal("Faite", Assert.Single(TaskViews.Group(TaskViews.Done, tasks, Monday)).Tasks.Single().Title);
        Assert.Equal(["Achats"], TaskViews.Lists(tasks));
        Assert.Equal("Sans date", Assert.Single(TaskViews.Group(TaskViews.All, tasks, Monday, "sans")).Tasks.Single().Title);
    }
}
