using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Tasks;

public sealed record TaskGroup(string Title, IReadOnlyList<WorkTask> Tasks, bool Late = false);

/// <summary>The tasks of one project (null: without project) with what its heading shows: how many to do, late, next date.</summary>
public sealed record TaskProjectBlock(Guid? ProjectId, IReadOnlyList<WorkTask> Tasks, int Open, bool Late, DateOnly? Next);

/// <summary>What each view of the « Tâches » page shows, and in which sections. Pure, so the page and the tests agree.</summary>
public static class TaskViews
{
    public const string Today = "today", Week = "week", Inbox = "inbox", All = "all", Done = "done";
    public static string ListView(string name) => "list:" + name;
    public static string ProjectView(Guid id) => "project:" + id;

    public static bool Shows(string view, WorkTask task, DateOnly today)
    {
        if (task.IsDeleted) return false;
        if (view == Done) return task.IsCompleted;
        // A checked task stays where it was; in the date views only for the day it was checked.
        var checkedToday = task.CompletedAt is { } at && DateOnly.FromDateTime(at.ToLocalTime().DateTime) == today;
        return view switch
        {
            Today => task.IsCompleted ? checkedToday : task.DueDate is { } d && d <= today,
            Week => task.IsCompleted ? checkedToday : task.DueDate is { } w && w <= today.AddDays(7),
            Inbox => task.List is null && task.ProjectId is null,
            All => true,
            _ when view.StartsWith("list:", StringComparison.Ordinal) => string.Equals(task.List, view[5..], StringComparison.CurrentCultureIgnoreCase),
            _ when view.StartsWith("project:", StringComparison.Ordinal) => task.ProjectId?.ToString() == view[8..],
            _ => false
        };
    }

    /// <summary>Open tasks by due date, the most urgent first; finished ones by completion, newest first.</summary>
    public static IReadOnlyList<TaskGroup> Group(string view, IEnumerable<WorkTask> tasks, DateOnly today, string? search = null)
    {
        var all = tasks.Where(t => Shows(view, t, today) && Matches(t, search)).ToArray();
        var shown = view == Done ? all : all.Where(t => !t.IsCompleted).ToArray();
        if (view == Done) return shown.Length == 0 ? [] : [new("Terminées", shown.OrderByDescending(t => t.CompletedAt).ToArray())];
        static IReadOnlyList<WorkTask> Sort(IEnumerable<WorkTask> items) => items.OrderByDescending(t => (int)t.Priority).ThenBy(t => t.DueDate).ThenBy(t => t.Order).ToArray();
        var groups = new List<TaskGroup>();
        void Add(string title, Func<WorkTask, bool> filter, bool late = false)
        {
            var items = shown.Where(filter).ToArray();
            if (items.Length > 0) groups.Add(new(title, Sort(items), late));
        }
        Add("En retard", t => t.DueDate < today, late: true);
        Add("Aujourd’hui", t => t.DueDate == today);
        Add("Demain", t => t.DueDate == today.AddDays(1));
        Add("7 prochains jours", t => t.DueDate > today.AddDays(1) && t.DueDate <= today.AddDays(7));
        Add("Plus tard", t => t.DueDate > today.AddDays(7));
        Add("Sans date", t => t.DueDate is null);
        var done = all.Where(t => t.IsCompleted).OrderByDescending(t => t.CompletedAt).ToArray();
        if (view != Done && done.Length > 0) groups.Add(new("Terminées", done));
        return groups;
    }

    /// <summary>
    /// The « Tâches » page by project: one block per project (then the tasks without project), its tasks by date — late, then
    /// soonest, undated last, flagged first on the same day. A task ticked today keeps its place, struck through. Blocks come
    /// by urgency: a late one first, then the nearest date; « Terminées » holds the last 7 days (every done one when searching).
    /// </summary>
    public static (IReadOnlyList<TaskProjectBlock> Projects, IReadOnlyList<WorkTask> Done) ByProject(IEnumerable<WorkTask> tasks, DateOnly today, Guid? projectId = null, string? search = null)
    {
        var shown = tasks.Where(t => !t.IsDeleted && (projectId is null || t.ProjectId == projectId) && Matches(t, search)).ToArray();
        bool DoneToday(WorkTask t) => t.CompletedAt is { } at && DateOnly.FromDateTime(at.ToLocalTime().DateTime) == today;
        var blocks = shown.Where(t => !t.IsCompleted || DoneToday(t)).GroupBy(t => t.ProjectId)
            .Select(g =>
            {
                var items = g.OrderBy(t => t.DueDate is null).ThenBy(t => t.DueDate).ThenByDescending(t => t.Priority != ProjectPriority.None).ThenBy(t => t.Order).ToArray();
                var open = items.Where(t => !t.IsCompleted).ToArray();
                return new TaskProjectBlock(g.Key, items, open.Length, open.Any(t => t.DueDate < today), open.Where(t => t.DueDate is not null).Min(t => t.DueDate));
            })
            .OrderBy(b => b.ProjectId is null).ThenByDescending(b => b.Late).ThenBy(b => b.Next is null).ThenBy(b => b.Next).ThenByDescending(b => b.Open).ToArray();
        var since = today.AddDays(-7);
        var done = shown.Where(t => t.CompletedAt is { } at && !DoneToday(t) && (!string.IsNullOrWhiteSpace(search) || DateOnly.FromDateTime(at.ToLocalTime().DateTime) > since))
            .OrderByDescending(t => t.CompletedAt).ToArray();
        return (blocks, done);
    }

    public static IReadOnlyList<string> Lists(IEnumerable<WorkTask> tasks) => tasks.Where(t => !t.IsDeleted && t.List is not null)
        .Select(t => t.List!).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();

    /// <summary>Tasks still to do in a view; for « Terminées », the checked ones.</summary>
    public static int Count(string view, IEnumerable<WorkTask> tasks, DateOnly today) => tasks.Count(t => Shows(view, t, today) && (view == Done) == t.IsCompleted);

    /// <summary>The first open task of a project, for the dashboard « prochaine tâche ».</summary>
    public static WorkTask? Next(IEnumerable<WorkTask> tasks, Guid projectId) => tasks
        .Where(t => !t.IsDeleted && !t.IsCompleted && t.ProjectId == projectId)
        .OrderBy(t => t.DueDate is null).ThenBy(t => t.DueDate).ThenByDescending(t => (int)t.Priority).ThenBy(t => t.Order).FirstOrDefault();

    private static bool Matches(WorkTask task, string? search) => string.IsNullOrWhiteSpace(search)
        || $"{task.Title} {task.Notes} {task.List} {string.Join(' ', (task.Steps ?? []).Select(s => s.Title))}".Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
