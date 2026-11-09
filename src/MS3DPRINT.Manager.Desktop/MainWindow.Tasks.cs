using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Tasks;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>« Tâches » page: independent tasks, views on the left (Aujourd’hui, 7 jours, listes, projets), quick add on top.</summary>
public partial class MainWindow
{
    private IReadOnlyList<WorkTask> _tasks = [];
    private IReadOnlyList<ProjectSummary> _taskProjects = [];
    private IReadOnlyList<ClientSummary> _taskClients = [];
    private string _taskView = TaskViews.All;
    private string? _tasksLoadedRoot;
    private bool _taskBusy;
    private TextBlock? _newTaskError;

    /// <summary>Dashboard « prochaine tâche »: the page opens on that project.</summary>
    private async Task OpenProjectTaskTrackingAsync(ProjectSummary project)
    {
        ShowWorkspace("Tâches");
        PageTitle.Text = "Tâches";
        PageSubtitle.Text = PageSubtitles["Tâches"];
        if (project.Profile is { } profile) _taskView = TaskViews.ProjectView(profile.Id);
        await LoadTasksAsync();
        TaskFilterBox.Text = "";
    }

    private async Task LoadTasksAsync()
    {
        RememberWorkspace("Tâches");
        var root = WorkspaceRoot;
        var load = StartWorkspaceLoad("tasks", root);
        try
        {
            var (tasks, projects, clients) = await Task.Run(() =>
            {
                TaskMigration.FromProjects(root);
                return (new TaskStore(new WorkspaceMetadataPaths(root)).LoadAll(), new WorkspaceProjectReader().Load(root), new WorkspaceClientReader().Load(root));
            });
            if (!IsWorkspaceLoadCurrent(load)) return;
            _tasksLoadedRoot = root;
            (_tasks, _taskProjects, _taskClients) = (tasks, projects.Where(p => p.Profile is not null).ToArray(), clients.Where(c => c.Profile is not null).ToArray());
            RenderTasks();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            TaskList.Children.Clear();
            TaskList.Children.Add(CreateReadErrorState("Tâches indisponibles", "Vérifiez l’accès au dossier de l’atelier, puis réessayez.", exception, LoadTasksAsync));
        }
    }

    private static DateOnly TaskToday => DateOnly.FromDateTime(DateTime.Today);

    private void RenderTasks()
    {
        var today = TaskToday;
        var lists = TaskViews.Lists(_tasks);
        var projects = _taskProjects.Where(p => TaskViews.ProjectView(p.Profile!.Id) == _taskView || _tasks.Any(t => t.ProjectId == p.Profile!.Id && TaskViews.Shows(TaskViews.All, t, today)))
            .OrderBy(p => p.Title(), StringComparer.CurrentCultureIgnoreCase).ToArray();
        // A list or project that no longer has tasks falls back to « Toutes ».
        if (_taskView.StartsWith("list:") && !lists.Contains(_taskView[5..], StringComparer.CurrentCultureIgnoreCase)
            || _taskView.StartsWith("project:") && !_taskProjects.Any(p => TaskViews.ProjectView(p.Profile!.Id) == _taskView))
            _taskView = TaskViews.All;

        TaskViewList.Children.Clear();
        void View(string view, string icon, string label)
        {
            var count = TaskViews.Count(view, _tasks, today);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            var text = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var number = new TextBlock { Text = view == TaskViews.Done || count == 0 ? "" : count.ToString(), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1); Grid.SetColumn(number, 2);
            grid.Children.Add(Icons.Glyph(icon, 16)); grid.Children.Add(text); grid.Children.Add(number);
            var button = new Button { Content = grid, Tag = view, Classes = { "task-view" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.Classes.Set("selected", view == _taskView);
            AutomationProperties.SetName(button, label);
            ToolTip.SetTip(button, label);
            button.Click += (_, _) => { _taskView = view; RenderTasks(); };
            TaskViewList.Children.Add(button);
        }
        void Heading(string text) => TaskViewList.Children.Add(new TextBlock { Text = text, Classes = { "caption" }, FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 16, 0, 4) });
        View(TaskViews.Today, "IconToday", "Aujourd’hui");
        View(TaskViews.Week, "IconProjectCalendar", "7 prochains jours");
        View(TaskViews.Inbox, "IconInbox", WorkTask.Inbox);
        View(TaskViews.All, "IconProjectTasks", "Toutes");
        View(TaskViews.Done, "IconCheck", "Terminées");
        if (lists.Count > 0) { Heading("LISTES"); foreach (var list in lists) View(TaskViews.ListView(list), "IconProjectList", list); }
        if (projects.Length > 0) { Heading("PROJETS"); foreach (var project in projects) View(TaskViews.ProjectView(project.Profile!.Id), "IconProjects", project.Title()); }

        TaskViewTitle.Text = TaskViewList.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, _taskView)) is { } selected ? AutomationProperties.GetName(selected) : "Toutes";
        NewTaskBox.PlaceholderText = "Ajouter une tâche" + (_taskView switch
        {
            TaskViews.Today => " pour aujourd’hui",
            _ when _taskView.StartsWith("list:") => " dans « " + _taskView[5..] + " »",
            _ when _taskView.StartsWith("project:") => " à ce projet",
            _ => ""
        }) + " · ex. Commander PLA demain !haute #Achats";
        TaskAddArea.IsVisible = _taskView != TaskViews.Done;

        TaskList.Children.Clear();
        var groups = TaskViews.Group(_taskView, _tasks, today, TaskFilterBox.Text);
        var count = TaskViews.Count(_taskView, _tasks, today);
        TasksSummary.Text = _taskView == TaskViews.Done ? $"{count} terminée{(count > 1 ? "s" : "")}" : count switch { 0 => "Rien à faire", 1 => "1 tâche", _ => $"{count} tâches" };
        if (groups.Count == 0)
        {
            TaskList.Children.Add(!string.IsNullOrWhiteSpace(TaskFilterBox.Text)
                ? CreateEmptyState("IconSearch", "Aucune tâche trouvée", "Essayez un autre mot, ou videz la recherche.")
                : _taskView == TaskViews.Done ? CreateEmptyState("IconCheck", "Aucune tâche terminée", "Les tâches cochées apparaissent ici.")
                : CreateEmptyState("IconCheck", "Rien à faire ici", "Tapez une tâche dans le champ ci-dessus, puis Entrée."));
            return;
        }
        foreach (var group in groups)
        {
            var heading = new TextBlock { Text = $"{group.Title} · {group.Tasks.Count}", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 6) };
            if (group.Late) heading.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") ?? Brushes.Orange;
            TaskList.Children.Add(heading);
            foreach (var task in group.Tasks) TaskList.Children.Add(BuildTaskRow(task));
        }
    }

    private Control BuildTaskRow(WorkTask task)
    {
        var root = WorkspaceRoot;
        var check = new CheckBox { IsChecked = task.IsCompleted, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(check, (task.IsCompleted ? "Rouvrir : " : "Terminer : ") + task.Title);
        check.IsCheckedChanged += async (_, _) =>
        {
            var done = check.IsChecked == true;
            if (await SaveTaskAsync(root, () => new TaskStore(new WorkspaceMetadataPaths(root)).Update(task with { CompletedAt = done ? DateTimeOffset.UtcNow : null })) is { } error)
            { RenderTasks(); Notify(error, error: true); }
        };
        var title = new TextBlock { Text = task.Title, TextWrapping = TextWrapping.Wrap };
        if (task.IsCompleted) { title.Classes.Add("faint"); title.TextDecorations = TextDecorations.Strikethrough; }
        var facts = new WrapPanel { ItemSpacing = 12, LineSpacing = 4 };
        if (task.DueDate is { } due)
        {
            var today = TaskToday;
            var label = due == today ? "Aujourd’hui" : due == today.AddDays(1) ? "Demain" : due == today.AddDays(-1) ? "Hier" : due.ToString("dd/MM/yyyy");
            var date = new TextBlock { Text = label, Classes = { "caption" } };
            if (!task.IsCompleted && due < today) date.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") ?? Brushes.Orange;
            facts.Children.Add(date);
        }
        if (task.ProjectId is { } projectId && !_taskView.StartsWith("project:") && _taskProjects.FirstOrDefault(p => p.Profile!.Id == projectId) is { } project)
            facts.Children.Add(new TextBlock { Text = project.Title(), Classes = { "caption" } });
        else if (task.ProjectId is null && task.ClientId is { } clientId && _taskClients.FirstOrDefault(c => c.Profile!.Id == clientId) is { } client)
            facts.Children.Add(new TextBlock { Text = client.Title(), Classes = { "caption" } });
        if (task.List is { } list && !_taskView.StartsWith("list:")) facts.Children.Add(new TextBlock { Text = "#" + list, Classes = { "caption" } });
        if (task.Steps is { Count: > 0 } steps) facts.Children.Add(new TextBlock { Text = $"{steps.Count(s => s.IsCompleted)}/{steps.Count}", Classes = { "caption" } });
        if (!string.IsNullOrWhiteSpace(task.Notes)) { var note = Icons.Glyph("IconEdit", 12); ToolTip.SetTip(note, task.Notes); facts.Children.Add(note); }
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { title } };
        if (facts.Children.Count > 0) text.Children.Add(facts);
        var open = new Button { Content = text, Classes = { "task-row-action" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(open, "Modifier : " + task.Title);
        open.Click += async (_, _) => await EditTaskAsync(task);
        var flag = ProjectPriorityVisual.Flag(task.Priority);
        flag.VerticalAlignment = VerticalAlignment.Center; flag.IsVisible = task.Priority != ProjectPriority.None;
        ToolTip.SetTip(flag, ProjectPriorityVisual.Text(task.Priority));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, MinHeight = 46 };
        Grid.SetColumn(open, 1); Grid.SetColumn(flag, 2);
        row.Children.Add(check); row.Children.Add(open); row.Children.Add(flag);
        return new Border { Child = row, Tag = task.Id, Classes = { "project-task-row" }, Padding = new Thickness(4, 0, 8, 0) };
    }

    /// <summary>Opens the task form; a null task starts a new one for the current view.</summary>
    private async Task EditTaskAsync(WorkTask? task)
    {
        if (_taskBusy) return;
        var root = WorkspaceRoot;
        var isNew = task is null;
        var current = task ?? NewTaskForView(WorkTask.New("-", NextTaskOrder()) with { Title = "" });
        var projects = new[] { new TaskLinkChoice(null, "Aucun projet") }.Concat(_taskProjects.Where(p => !p.IsArchived || p.Profile!.Id == current.ProjectId)
            .OrderBy(p => p.Title(), StringComparer.CurrentCultureIgnoreCase).Select(p => new TaskLinkChoice(p.Profile!.Id, p.Title() + " · " + p.Reference, p.Profile.ClientId))).ToArray();
        var clients = new[] { new TaskLinkChoice(null, "Aucun client") }.Concat(_taskClients
            .OrderBy(c => c.Title(), StringComparer.CurrentCultureIgnoreCase).Select(c => new TaskLinkChoice(c.Profile!.Id, c.Title()))).ToArray();
        var store = new TaskStore(new WorkspaceMetadataPaths(root));
        var dialog = new TaskWindow(current, TaskViews.Lists(_tasks), projects, clients,
            updated => SaveTaskAsync(root, () => isNew ? store.Create(updated) : store.Update(updated)),
            isNew ? null : () => SaveTaskAsync(root, () => store.Remove(current)));
        await dialog.ShowDialog<bool>(this);
    }

    /// <summary>A task typed in a view belongs to it: today’s date, that list, that project.</summary>
    private WorkTask NewTaskForView(WorkTask task)
    {
        if (_taskView == TaskViews.Today && task.DueDate is null) task = task with { DueDate = TaskToday };
        if (_taskView.StartsWith("list:") && task.List is null) task = task with { List = _taskView[5..] };
        if (_taskView.StartsWith("project:") && _taskProjects.FirstOrDefault(p => TaskViews.ProjectView(p.Profile!.Id) == _taskView) is { } project)
            task = task with { ProjectId = project.Profile!.Id, ClientId = project.Profile.ClientId };
        return task;
    }

    private double NextTaskOrder() => _tasks.Count == 0 ? 0 : _tasks.Max(t => t.Order) + 1;

    // Typing can arrive before the first load: rendering then would judge the open view on empty data.
    private void TaskFilterText_Changed(object? sender, TextChangedEventArgs e) { if (TasksWorkspacePanel.IsVisible && _tasksLoadedRoot == WorkspaceRoot) RenderTasks(); }
    private async void AddTask_Click(object? sender, RoutedEventArgs e) => await AddTaskAsync();
    private async void TaskDetails_Click(object? sender, RoutedEventArgs e) => await EditTaskAsync(null);
    private async void NewTaskBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await AddTaskAsync();
    }

    private async Task AddTaskAsync()
    {
        if (_newTaskError is null) { _newTaskError = FormKit.ErrorText(); TaskAddArea.Children.Add(_newTaskError); }
        _newTaskError.IsVisible = false;
        if (_taskBusy) return;
        if (string.IsNullOrWhiteSpace(NewTaskBox.Text)) { FormKit.ShowError(_newTaskError, NewTaskBox, "Saisissez le texte de la tâche."); return; }
        var quick = TaskQuickAdd.Parse(NewTaskBox.Text, TaskToday);
        if (quick.Title.Length > 500) { FormKit.ShowError(_newTaskError, NewTaskBox, "Le texte de la tâche est limité à 500 caractères."); return; }
        var task = NewTaskForView(WorkTask.New(quick.Title, NextTaskOrder()) with { DueDate = quick.DueDate, Priority = quick.Priority, List = quick.List });
        var root = WorkspaceRoot;
        var error = await SaveTaskAsync(root, () => new TaskStore(new WorkspaceMetadataPaths(root)).Create(task));
        if (error is not null) { FormKit.ShowError(_newTaskError, NewTaskBox, error); return; }
        NewTaskBox.Text = "";
        NewTaskBox.Focus();
    }

    private bool TaskContextCurrent(string root) => IsVisible && TasksWorkspacePanel.IsVisible && string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes one task off the UI thread, then reloads the list from disk. Returns the message to show, or null.</summary>
    private async Task<string?> SaveTaskAsync(string root, Func<WorkTask> write)
    {
        if (!TaskContextCurrent(root)) return "Le contexte a changé. Revenez aux tâches et réessayez.";
        if (_taskBusy) return "Un enregistrement est déjà en cours.";
        _taskBusy = true;
        TaskList.IsEnabled = false; TaskAddArea.IsEnabled = false;
        try
        {
            await Task.Run(write);
            var tasks = await Task.Run(() => new TaskStore(new WorkspaceMetadataPaths(root)).LoadAll());
            if (TaskContextCurrent(root))
            {
                _tasks = tasks;
                var scroll = TaskList.Parent as ScrollViewer; var offset = scroll?.Offset;
                RenderTasks();
                if (scroll is not null && offset is { } previous) Avalonia.Threading.Dispatcher.UIThread.Post(() => scroll.Offset = previous);
            }
            return null;
        }
        catch (ProfileConflictException)
        { return "Cette tâche a été modifiée ailleurs entre-temps. Fermez puis rouvrez-la pour repartir de la dernière version."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return e.Message; }
        finally
        {
            _taskBusy = false;
            TaskList.IsEnabled = true; TaskAddArea.IsEnabled = true;
        }
    }
}

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
        var body = new StackPanel { Spacing = 6 };
        foreach (var task in open.Take(8))
        {
            var line = task.Title + (task.DueDate is { } due ? " · " + due.ToString("dd/MM") : "");
            body.Children.Add(new TextBlock { Text = "• " + line, TextWrapping = TextWrapping.Wrap });
        }
        if (open.Length > 8) body.Children.Add(new TextBlock { Text = $"… et {open.Length - 8} autre(s)", Classes = { "caption" } });
        if (open.Length == 0) body.Children.Add(new TextBlock { Text = tasks.Count > 0 ? "Toutes les tâches sont terminées." : "Aucune tâche pour ce projet.", Classes = { "muted" } });
        var go = new Button { Content = Icons.Label("IconProjectTasks", open.Length > 0 ? "Gérer les tâches du projet" : "Ajouter une tâche"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        go.Click += async (_, _) => await OpenProjectTaskTrackingAsync(project);
        body.Children.Add(go);
        return EstimateSection("Tâches", $"{tasks.Count(t => t.IsCompleted)} / {tasks.Count} faites", body);
    }
}
