using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Tasks;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    /// <summary>Project sheet: its open tasks at a glance, managed from the « Tâches » page.</summary>
    private Control? BuildProjectTasksSection(ProjectSummary project)
    {
        if (project.Profile is not { } profile) return null;
        IReadOnlyList<WorkTask> tasks;
        // shortcut: reads the task files on the UI thread; fine for a workshop's few hundred tasks, move to the sheet load if it grows.
        try { tasks = new TaskStore(new WorkspaceMetadataPaths(WorkspaceRoot)).LoadActive().Where(t => t.ProjectId == profile.Id).ToArray(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        var open = tasks.Where(t => !t.IsCompleted).OrderBy(t => t.DueDate is null).ThenBy(t => t.DueDate).ThenByDescending(t => (int)t.Priority).ThenBy(t => t.Order).ToArray();
        var body = new StackPanel { Spacing = 8 };
        foreach (var task in open.Take(8))
        {
            var line = task.Title + (task.DueDate is { } due ? " · " + due.ToString("dd/MM") : "");
            body.Children.Add(new TextBlock { Text = "• " + line, TextWrapping = TextWrapping.Wrap });
        }
        if (open.Length > 8) body.Children.Add(new TextBlock { Text = $"… et {open.Length - 8} autre(s)", Classes = { "caption" } });
        if (open.Length == 0) body.Children.Add(new TextBlock { Text = tasks.Count > 0 ? "Toutes les tâches sont terminées." : "Aucune tâche pour ce projet.", Classes = { "muted" } });
        var go = new Button { Content = Icons.Label("IconProjectTasks", open.Length > 0 ? "Gérer les tâches du projet" : "Ajouter une tâche"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        go.Click += async (_, _) => await OpenProjectTaskTrackingAsync(project);
        body.Children.Add(go);
        return EstimateSection("Tâches", $"{tasks.Count(t => t.IsCompleted)} / {tasks.Count} faites", body);
    }
}
