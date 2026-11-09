using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tasks;

/// <summary>Moves the checklists kept inside project sheets into independent tasks linked to that project. Safe to run at every load.</summary>
public static class TaskMigration
{
    public static int FromProjects(string root)
    {
        var paths = new WorkspaceMetadataPaths(root);
        var projects = new ProjectProfileStore(paths);
        var tasks = new TaskStore(paths);
        var moved = 0;
        foreach (var profile in projects.LoadReadable().Where(p => p.Tasks is { Count: > 0 }))
        {
            // Same Id: a second run, or another PC doing the same move, never duplicates it.
            foreach (var task in Legacy(profile).Where(t => tasks.Find(t.Id) is null)) { tasks.Create(task); moved++; }
            projects.Update(profile with { Tasks = [] }, profile.UpdatedAt);
        }
        return moved;
    }

    /// <summary>A project checklist seen as independent tasks, without writing anything.</summary>
    public static IEnumerable<WorkTask> Legacy(ProjectProfile profile) => ProjectTasks.Items(profile).Select((old, index) =>
        new WorkTask(old.Id, old.Title, old.Notes, ProjectPriority.None, null, null, profile.Id, profile.ClientId,
            index, profile.CreatedAt, profile.UpdatedAt, old.IsCompleted ? profile.UpdatedAt : null));
}
