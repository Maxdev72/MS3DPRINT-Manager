using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Tasks;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>A link offered in the task detail: a project or a client, or none.</summary>
public sealed record TaskLinkChoice(Guid? Id, string Label, Guid? ClientId = null)
{
    public override string ToString() => Label;
}

/// <summary>« Tâches » page: views on the left, the list in the middle, the selected task on the right, saved as it is edited.</summary>
public partial class MainWindow
{
    private IReadOnlyList<WorkTask> _tasks = [];
    private IReadOnlyList<string> _taskLists = [];
    private IReadOnlyList<ProjectSummary> _taskProjects = [];
    private IReadOnlyList<ClientSummary> _taskClients = [];
    private string _taskView = TaskViews.Today;
    private string? _tasksLoadedRoot;
    private Guid? _selectedTaskId;
    private readonly HashSet<string> _collapsedTaskGroups = ["Terminées"];
    private readonly List<Guid> _visibleTaskIds = [];
    private readonly SemaphoreSlim _taskGate = new(1, 1);
    private bool _taskBusy;
    private bool _taskKeysHooked;
    private bool _addingTaskList;
    private string? _renamingTaskList;
    private TextBlock? _newTaskError;

    /// <summary>Dashboard « prochaine tâche » and the project sheet: the page opens on that project.</summary>
    private async Task OpenProjectTaskTrackingAsync(ProjectSummary project)
    {
        ShowWorkspace("Tâches");
        PageTitle.Text = "Tâches";
        PageSubtitle.Text = PageSubtitles["Tâches"];
        if (project.Profile is { } profile) _taskView = TaskViews.ProjectView(profile.Id);
        _selectedTaskId = null;
        await LoadTasksAsync();
        TaskFilterBox.Text = "";
    }

    private async Task LoadTasksAsync()
    {
        RememberWorkspace("Tâches");
        HookTaskKeys();
        var root = WorkspaceRoot;
        var load = StartWorkspaceLoad("tasks", root);
        try
        {
            var (tasks, lists, projects, clients) = await Task.Run(() =>
            {
                TaskMigration.FromProjects(root);
                var paths = new WorkspaceMetadataPaths(root);
                return (new TaskStore(paths).LoadAll(), new TaskListStore(paths).ActiveNames(), new WorkspaceProjectReader().Load(root), new WorkspaceClientReader().Load(root));
            });
            if (!IsWorkspaceLoadCurrent(load)) return;
            _tasksLoadedRoot = root;
            (_tasks, _taskLists) = (tasks, lists);
            (_taskProjects, _taskClients) = (projects.Where(p => p.Profile is not null).ToArray(), clients.Where(c => c.Profile is not null).ToArray());
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
    private IReadOnlyList<string> AllTaskLists => TaskViews.Lists(_tasks).Concat(_taskLists).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
    private WorkTask? SelectedTask => _selectedTaskId is { } id ? _tasks.FirstOrDefault(t => t.Id == id && !t.IsDeleted) : null;

    private void RenderTasks(bool detail = true)
    {
        var today = TaskToday;
        var lists = AllTaskLists;
        if (_taskView.StartsWith("list:") && !lists.Contains(_taskView[5..], StringComparer.CurrentCultureIgnoreCase)
            || _taskView.StartsWith("project:") && !_taskProjects.Any(p => TaskViews.ProjectView(p.Profile!.Id) == _taskView))
            _taskView = TaskViews.Today;
        RenderTaskViews(lists, today);
        RenderTaskList(today);
        if (detail) RenderTaskDetail();
    }

    // ───────────── Left column: views, lists, projects ─────────────

    private void RenderTaskViews(IReadOnlyList<string> lists, DateOnly today)
    {
        TaskViewList.Children.Clear();
        Button View(string view, string icon, string label)
        {
            var count = TaskViews.Count(view, _tasks, today);
            var late = view == TaskViews.Today && _tasks.Any(t => !t.IsDeleted && !t.IsCompleted && t.DueDate < today);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            var text = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var number = new TextBlock { Text = view == TaskViews.Done || count == 0 ? "" : count.ToString(), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
            if (late) number.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") ?? Brushes.Orange;
            Grid.SetColumn(text, 1); Grid.SetColumn(number, 2);
            grid.Children.Add(Icons.Glyph(icon, 16)); grid.Children.Add(text); grid.Children.Add(number);
            var button = new Button { Content = grid, Tag = view, Classes = { "task-view" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.Classes.Set("selected", view == _taskView);
            AutomationProperties.SetName(button, label);
            ToolTip.SetTip(button, label);
            button.Click += (_, _) => { _taskView = view; RenderTasks(); };
            TaskViewList.Children.Add(button);
            return button;
        }
        void Heading(string text, Button? action = null)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 18, 0, 4) };
            grid.Children.Add(new TextBlock { Text = text, Classes = { "caption" }, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            if (action is not null) { Grid.SetColumn(action, 1); grid.Children.Add(action); }
            TaskViewList.Children.Add(grid);
        }
        View(TaskViews.Today, "IconToday", "Ma journée");
        View(TaskViews.Week, "IconProjectCalendar", "7 prochains jours");
        View(TaskViews.Inbox, "IconInbox", WorkTask.Inbox);
        View(TaskViews.All, "IconProjectTasks", "Toutes");
        View(TaskViews.Done, "IconCheck", "Terminées");

        var addList = new Button { Content = Icons.Glyph("IconPlus", 14), Classes = { "tool" } };
        AutomationProperties.SetName(addList, "Nouvelle liste"); ToolTip.SetTip(addList, "Nouvelle liste");
        addList.Click += (_, _) => { _addingTaskList = true; _renamingTaskList = null; RenderTasks(detail: false); };
        Heading("LISTES", addList);
        foreach (var list in lists)
        {
            if (string.Equals(_renamingTaskList, list, StringComparison.CurrentCultureIgnoreCase)) { TaskViewList.Children.Add(TaskListNameBox(list)); continue; }
            var button = View(TaskViews.ListView(list), "IconProjectList", list);
            var rename = new MenuItem { Header = "Renommer" };
            rename.Click += (_, _) => { _renamingTaskList = list; _addingTaskList = false; RenderTasks(detail: false); };
            var remove = new MenuItem { Header = "Retirer la liste" };
            remove.Click += async (_, _) => await RemoveTaskListAsync(list);
            button.ContextMenu = new ContextMenu { ItemsSource = new[] { rename, remove } };
            ToolTip.SetTip(button, list + " · clic droit pour renommer ou retirer");
        }
        if (_addingTaskList) TaskViewList.Children.Add(TaskListNameBox(null));
        else if (lists.Count == 0) TaskViewList.Children.Add(new TextBlock { Text = "Tapez #Nom dans une tâche ou cliquez sur +.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 0, 0, 0) });

        var projects = _taskProjects.Where(p => TaskViews.ProjectView(p.Profile!.Id) == _taskView || _tasks.Any(t => t.ProjectId == p.Profile!.Id && !t.IsDeleted && !t.IsCompleted))
            .OrderBy(p => p.Title(), StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (projects.Length > 0) { Heading("PROJETS"); foreach (var project in projects) View(TaskViews.ProjectView(project.Profile!.Id), "IconProjects", project.Title()); }
    }

    /// <summary>Inline name box: a new list, or the new name of <paramref name="current"/>. Enter saves, Échap cancels.</summary>
    private TextBox TaskListNameBox(string? current)
    {
        var box = new TextBox { Text = current, PlaceholderText = "Nom de la liste", MaxLength = 60, Margin = new Thickness(4, 2) };
        AutomationProperties.SetName(box, current is null ? "Nom de la nouvelle liste" : "Nouveau nom de la liste");
        var done = false;
        async Task Commit()
        {
            if (done) return; done = true;
            var name = box.Text?.Trim() ?? "";
            (_addingTaskList, _renamingTaskList) = (false, null);
            if (!TaskListEntry.IsValidName(name) || string.Equals(name, current, StringComparison.CurrentCulture)) { RenderTasks(detail: false); return; }
            await RunTaskWriteAsync(root =>
            {
                var paths = new WorkspaceMetadataPaths(root);
                var lists = new TaskListStore(paths);
                lists.Add(name);
                if (current is null) return;
                // A rename moves every task of the old list, then retires the old name.
                var store = new TaskStore(paths);
                foreach (var task in store.LoadActive().Where(t => string.Equals(t.List, current, StringComparison.CurrentCultureIgnoreCase))) store.Update(task with { List = name });
                if (!string.Equals(name, current, StringComparison.CurrentCultureIgnoreCase)) lists.Remove(current);
            }, rerender: false);
            _taskView = TaskViews.ListView(name);
            RenderTasks();
        }
        box.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await Commit(); }
            else if (e.Key == Key.Escape) { e.Handled = true; done = true; (_addingTaskList, _renamingTaskList) = (false, null); RenderTasks(detail: false); }
        };
        box.LostFocus += async (_, _) => await Commit();
        Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); });
        return box;
    }

    private async Task RemoveTaskListAsync(string list)
    {
        var count = _tasks.Count(t => !t.IsDeleted && string.Equals(t.List, list, StringComparison.CurrentCultureIgnoreCase));
        var detail = count == 0 ? "Cette liste est vide. Aucune tâche n’est concernée."
            : $"Ses {count} tâche(s) ne sont pas retirées : elles passent dans la {WorkTask.Inbox}.";
        if (!await new ConfirmRemovalWindow(list, detail).ShowDialog<bool>(this)) return;
        await RunTaskWriteAsync(root =>
        {
            var paths = new WorkspaceMetadataPaths(root);
            var store = new TaskStore(paths);
            foreach (var task in store.LoadActive().Where(t => string.Equals(t.List, list, StringComparison.CurrentCultureIgnoreCase))) store.Update(task with { List = null });
            new TaskListStore(paths).Remove(list);
        });
    }

    // ───────────── Middle column: quick add and the list ─────────────

    private void RenderTaskList(DateOnly today)
    {
        TaskViewTitle.Text = TaskViewList.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, _taskView)) is { } selected ? AutomationProperties.GetName(selected) : "Ma journée";
        NewTaskBox.PlaceholderText = "Ajouter une tâche" + (_taskView switch
        {
            TaskViews.Today => " pour aujourd’hui",
            _ when _taskView.StartsWith("list:") => " dans « " + _taskView[5..] + " »",
            _ when _taskView.StartsWith("project:") => " à ce projet",
            _ => ""
        }) + " · ex. PLA demain !haute #Achats";
        TaskAddArea.IsVisible = _taskView != TaskViews.Done;
        var count = TaskViews.Count(_taskView, _tasks, today);
        TasksSummary.Text = _taskView == TaskViews.Done ? $"{count} terminée{(count > 1 ? "s" : "")}" : count switch { 0 => "Rien à faire", 1 => "1 tâche", _ => $"{count} tâches" };

        TaskList.Children.Clear();
        _visibleTaskIds.Clear();
        if (_taskView is TaskViews.Today or TaskViews.Week) AddProjectHints(today);
        var groups = TaskViews.Group(_taskView, _tasks, today, TaskFilterBox.Text);
        if (groups.Count == 0)
        {
            TaskList.Children.Add(!string.IsNullOrWhiteSpace(TaskFilterBox.Text)
                ? CreateEmptyState("IconSearch", "Aucune tâche trouvée", "Essayez un autre mot, ou videz la recherche.")
                : _taskView == TaskViews.Done ? CreateEmptyState("IconCheck", "Aucune tâche terminée", "Les tâches cochées apparaissent ici.")
                : _taskView == TaskViews.Today ? CreateEmptyState("IconCheck", "Journée dégagée", "Rien n’est prévu pour aujourd’hui. Tapez une tâche ci-dessus, puis Entrée.")
                : CreateEmptyState("IconCheck", "Rien à faire ici", "Tapez une tâche dans le champ ci-dessus, puis Entrée."));
            return;
        }
        foreach (var group in groups)
        {
            var collapsed = _taskView != TaskViews.Done && _collapsedTaskGroups.Contains(group.Title);
            var label = group.Title == "Terminées" && _taskView is TaskViews.Today or TaskViews.Week ? "Terminées aujourd’hui" : group.Title;
            var header = new Button { Classes = { "task-group" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 4) };
            var text = new TextBlock { Text = $"{label} · {group.Tasks.Count}", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            if (group.Late) text.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") ?? Brushes.Orange;
            header.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Icons.Glyph(collapsed ? "IconArrowRight" : "IconChevronDown", 14), text } };
            AutomationProperties.SetName(header, (collapsed ? "Déplier " : "Replier ") + label);
            header.Click += (_, _) => { if (!_collapsedTaskGroups.Add(group.Title)) _collapsedTaskGroups.Remove(group.Title); RenderTasks(detail: false); };
            TaskList.Children.Add(header);
            if (collapsed) continue;
            foreach (var task in group.Tasks) { TaskList.Children.Add(BuildTaskRow(task, today)); _visibleTaskIds.Add(task.Id); }
        }
    }

    /// <summary>Ma journée / 7 jours: projects due now, and projects due soon that have nothing planned yet.</summary>
    private void AddProjectHints(DateOnly today)
    {
        var active = _taskProjects.Where(p => !p.IsArchived && p.Profile!.Status != ProjectStatus.Completed && p.Profile.DueDate is not null).ToArray();
        var due = _taskView == TaskViews.Today ? active.Where(p => p.Profile!.DueDate <= today).OrderBy(p => p.Profile!.DueDate).ToArray() : [];
        var bare = active.Where(p => p.Profile!.DueDate > today && p.Profile.DueDate <= today.AddDays(7)
                && !_tasks.Any(t => t.ProjectId == p.Profile.Id && !t.IsDeleted && !t.IsCompleted))
            .OrderBy(p => p.Profile!.DueDate).Take(3).ToArray();
        if (due.Length == 0 && bare.Length == 0) return;
        var box = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var project in due)
        {
            var late = project.Profile!.DueDate < today;
            box.Children.Add(TaskHint(late ? "IconAlert" : "IconProjects", $"Projet à rendre {(late ? "en retard depuis le " + project.Profile.DueDate!.Value.ToString("dd/MM") : "aujourd’hui")} : {project.Title()}", "Ouvrir", project, late, openProject: true));
        }
        foreach (var project in bare)
            box.Children.Add(TaskHint("IconInfo", $"Suggestion : « {project.Title()} » est à rendre le {project.Profile!.DueDate!.Value:dd/MM} et n’a aucune tâche.", "Planifier", project, false, openProject: false));
        TaskList.Children.Add(box);
    }

    private Control TaskHint(string icon, string text, string action, ProjectSummary project, bool warning, bool openProject)
    {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        if (warning) label.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") ?? Brushes.Orange;
        var go = new Button { Content = action, VerticalAlignment = VerticalAlignment.Center, Tag = project };
        AutomationProperties.SetName(go, action + " " + project.Title());
        if (openProject) go.Click += ProjectEntry_Click;
        else go.Click += (_, _) => { _taskView = TaskViews.ProjectView(project.Profile!.Id); RenderTasks(); NewTaskBox.Focus(); };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        var glyph = Icons.Glyph(icon, 16); glyph.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1); Grid.SetColumn(go, 2);
        grid.Children.Add(glyph); grid.Children.Add(label); grid.Children.Add(go);
        return new Border { Child = grid, Classes = { "task-hint" }, Padding = new Thickness(12, 8) };
    }

    private Control BuildTaskRow(WorkTask task, DateOnly today)
    {
        var check = new CheckBox { IsChecked = task.IsCompleted, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(check, (task.IsCompleted ? "Rouvrir : " : "Terminer : ") + task.Title);
        check.IsCheckedChanged += async (_, _) =>
        {
            var done = check.IsChecked == true;
            if (done == task.IsCompleted) return;
            await SaveTaskChangeAsync(task.Id, t => t with { CompletedAt = done ? DateTimeOffset.UtcNow : null });
        };
        var title = new TextBlock { Text = task.Title, TextWrapping = TextWrapping.Wrap };
        if (task.IsCompleted)
        {
            title.Classes.Add("faint");
            title.TextDecorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Strikethrough } };
        }
        var facts = new WrapPanel { ItemSpacing = 12, LineSpacing = 4 };
        if (task.DueDate is { } due)
        {
            var date = new TextBlock { Text = DueLabel(due, today), Classes = { "caption" } };
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
        var open = new Button { Content = text, Tag = task.Id, Classes = { "task-row-action" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(open, "Modifier : " + task.Title);
        open.Click += (_, _) => SelectTask(task.Id);
        var flag = ProjectPriorityVisual.Flag(task.Priority);
        flag.VerticalAlignment = VerticalAlignment.Center; flag.IsVisible = task.Priority != ProjectPriority.None;
        ToolTip.SetTip(flag, ProjectPriorityVisual.Text(task.Priority));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, MinHeight = 46 };
        Grid.SetColumn(open, 1); Grid.SetColumn(flag, 2);
        row.Children.Add(check); row.Children.Add(open); row.Children.Add(flag);
        var border = new Border { Child = row, Tag = task.Id, Classes = { "project-task-row", "task-row" }, Padding = new Thickness(6, 0, 10, 0) };
        border.Classes.Set("selected", task.Id == _selectedTaskId);
        return border;
    }

    private static string DueLabel(DateOnly due, DateOnly today) => (due.DayNumber - today.DayNumber) switch
    {
        0 => "Aujourd’hui", 1 => "Demain", -1 => "Hier",
        > 1 and < 7 => CultureInfo.GetCultureInfo("fr-FR").DateTimeFormat.GetDayName(due.DayOfWeek),
        _ => due.ToString("dd/MM/yyyy")
    };

    private void SelectTask(Guid? id, bool focusRow = false)
    {
        _selectedTaskId = id;
        RenderTasks();
        if (focusRow && id is { } selected)
            Dispatcher.UIThread.Post(() => TaskList.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Tag, selected))?.Focus());
    }

    // ───────────── Quick add with a live preview ─────────────

    private void NewTaskBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        TaskQuickPreview.Children.Clear();
        if (_newTaskError is not null) _newTaskError.IsVisible = false;
        if (string.IsNullOrWhiteSpace(NewTaskBox.Text)) return;
        var today = TaskToday;
        var quick = TaskQuickAdd.Parse(NewTaskBox.Text, today);
        var task = NewTaskForView(WorkTask.New("-", 0) with { DueDate = quick.DueDate, Priority = quick.Priority, List = quick.List });
        void Chip(string icon, string text, bool warning = false)
        {
            var label = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var glyph = Icons.Glyph(icon, 12); glyph.VerticalAlignment = VerticalAlignment.Center;
            var chip = new Border { Classes = { "task-chip" }, Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { glyph, label } } };
            if (warning) label.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") ?? Brushes.Orange;
            TaskQuickPreview.Children.Add(chip);
        }
        if (task.DueDate is { } due) Chip("IconToday", DueLabel(due, today) + (due.DayNumber - today.DayNumber is > 1 and < 7 ? " " + due.ToString("dd/MM") : ""));
        if (task.Priority != ProjectPriority.None) Chip("IconFlag", ProjectPriorityVisual.Text(task.Priority));
        if (task.List is { } list) Chip("IconProjectList", "#" + list + (AllTaskLists.Contains(list, StringComparer.CurrentCultureIgnoreCase) ? "" : " (nouvelle liste)"));
        if (quick.Project is { } projectHint)
        {
            if (ResolveTaskProject(projectHint) is { } project) Chip("IconProjects", project.Title());
            else Chip("IconAlert", "Projet « " + projectHint + " » introuvable", warning: true);
        }
        else if (task.ProjectId is { } viewProject && _taskProjects.FirstOrDefault(p => p.Profile!.Id == viewProject) is { } current) Chip("IconProjects", current.Title());
        if (quick.Client is { } clientHint)
        {
            if (ResolveTaskClient(clientHint) is { } client) Chip("IconClients", client.Title());
            else Chip("IconAlert", "Client « " + clientHint + " » introuvable", warning: true);
        }
    }

    private static string Fold(string value) => new string(value.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

    /// <summary>@vélo → the active project whose name or reference contains the typed text, accents ignored.</summary>
    private ProjectSummary? ResolveTaskProject(string hint) => _taskProjects.Where(p => !p.IsArchived)
        .FirstOrDefault(p => Fold(p.Title()).Contains(Fold(hint)) || Fold(p.Reference).Contains(Fold(hint)));

    private ClientSummary? ResolveTaskClient(string hint) => _taskClients
        .FirstOrDefault(c => Fold(c.Title()).Contains(Fold(hint)) || Fold(c.ClientCode ?? "").Contains(Fold(hint)));

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
    private void TaskFilterText_Changed(object? sender, TextChangedEventArgs e) { if (TasksWorkspacePanel.IsVisible && _tasksLoadedRoot == WorkspaceRoot) RenderTasks(detail: false); }
    private async void AddTask_Click(object? sender, RoutedEventArgs e) => await AddTaskAsync();
    private async void NewTaskBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await AddTaskAsync(); }
        else if (e.Key == Key.Escape) { e.Handled = true; NewTaskBox.Text = ""; }
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
        if (quick.Project is { } projectHint)
        {
            if (ResolveTaskProject(projectHint) is not { } project) { FormKit.ShowError(_newTaskError, NewTaskBox, $"Aucun projet actif ne correspond à « {projectHint} ». Corrigez le nom après @."); return; }
            task = task with { ProjectId = project.Profile!.Id, ClientId = project.Profile.ClientId };
        }
        else if (quick.Client is { } clientHint)
        {
            if (ResolveTaskClient(clientHint) is not { } client) { FormKit.ShowError(_newTaskError, NewTaskBox, $"Aucun client ne correspond à « {clientHint} ». Corrigez le nom après +."); return; }
            task = task with { ClientId = client.Profile!.Id };
        }
        var error = await RunTaskWriteAsync(root =>
        {
            var paths = new WorkspaceMetadataPaths(root);
            new TaskStore(paths).Create(task);
            if (task.List is { } list) new TaskListStore(paths).Add(list);
        });
        if (error is not null) { FormKit.ShowError(_newTaskError, NewTaskBox, error); return; }
        NewTaskBox.Text = "";
        NewTaskBox.Focus();
    }

    // ───────────── Keyboard ─────────────

    private void HookTaskKeys()
    {
        if (_taskKeysHooked) return;
        _taskKeysHooked = true;
        // Tunnel: the page sees the keys before the focused button turns Space into a click.
        TasksWorkspacePanel.AddHandler(KeyDownEvent, TasksPanel_KeyDown, RoutingStrategies.Tunnel);
        TasksWorkspacePanel.SizeChanged += (_, e) => ApplyTaskLayout(e.NewSize.Width);
        ApplyTaskLayout(TasksWorkspacePanel.Bounds.Width);
    }

    /// <summary>Below ~1000 px the detail floats over the list instead of taking a third column, so the list keeps a usable width.</summary>
    private void ApplyTaskLayout(double width)
    {
        var overlay = width > 0 && width < 1000;
        Grid.SetColumn(TaskDetailHost, overlay ? 1 : 2);
        Grid.SetColumnSpan(TaskDetailHost, overlay ? 2 : 1);
        TaskDetailHost.HorizontalAlignment = overlay ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        TaskDetailHost.ZIndex = overlay ? 5 : 0;
        TaskDetailHost.Classes.Set("floating", overlay);
    }

    private async void TasksPanel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_panel is not null || e.Source is not Visual source || source.FindAncestorOfType<TextBox>(includeSelf: true) is not null) return;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F) { TaskFilterBox.Focus(); e.Handled = true; return; }
        if (e.KeyModifiers != KeyModifiers.None) return;
        var index = _selectedTaskId is { } id ? _visibleTaskIds.IndexOf(id) : -1;
        switch (e.Key)
        {
            case Key.N: NewTaskBox.Focus(); e.Handled = true; break;
            case Key.Down when _visibleTaskIds.Count > 0: SelectTask(_visibleTaskIds[Math.Min(index + 1, _visibleTaskIds.Count - 1)], focusRow: true); e.Handled = true; break;
            case Key.Up when _visibleTaskIds.Count > 0: SelectTask(_visibleTaskIds[Math.Max(index - 1, 0)], focusRow: true); e.Handled = true; break;
            case Key.Escape when _selectedTaskId is not null: SelectTask(null); e.Handled = true; break;
            case Key.Space when SelectedTask is { } task:
                e.Handled = true;
                await SaveTaskChangeAsync(task.Id, t => t with { CompletedAt = t.IsCompleted ? null : DateTimeOffset.UtcNow }, refreshDetail: true);
                SelectTask(task.Id, focusRow: true);
                break;
            case Key.Delete when SelectedTask is { } task: e.Handled = true; await RemoveTaskAsync(task); break;
        }
    }

    // ───────────── Right column: the selected task, saved as it is edited ─────────────

    private void RenderTaskDetail()
    {
        TaskDetail.Children.Clear();
        if (SelectedTask is not { } task) { TaskDetailHost.IsVisible = false; return; }
        TaskDetailHost.IsVisible = true;
        var id = task.Id;
        Task Save(Func<WorkTask, WorkTask> change, bool refresh = false) => SaveTaskChangeAsync(id, change, refresh);

        var check = new CheckBox { IsChecked = task.IsCompleted, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(check, task.IsCompleted ? "Rouvrir la tâche" : "Terminer la tâche");
        check.IsCheckedChanged += async (_, _) => await Save(t => t with { CompletedAt = check.IsChecked == true ? DateTimeOffset.UtcNow : null }, refresh: true);
        var title = new TextBox { Text = task.Title, MaxLength = 500, FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };
        AutomationProperties.SetName(title, "Tâche");
        async Task SaveTitle()
        {
            var value = title.Text?.Trim() ?? "";
            if (value.Length == 0) { title.Text = SelectedTask?.Title; return; }
            if (value != SelectedTask?.Title) await Save(t => t with { Title = value });
        }
        title.LostFocus += async (_, _) => await SaveTitle();
        title.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SaveTitle(); } };
        var close = new Button { Content = Icons.Glyph("IconClose", 16), Classes = { "tool" }, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(close, "Fermer le détail"); ToolTip.SetTip(close, "Fermer · Échap");
        close.Click += (_, _) => SelectTask(null);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(title, 1); Grid.SetColumn(close, 2);
        head.Children.Add(check); head.Children.Add(title); head.Children.Add(close);
        TaskDetail.Children.Add(head);

        // Due date: one click for the usual choices, the calendar for the rest.
        var today = TaskToday;
        var quick = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (label, value) in new (string, DateOnly?)[] { ("Aujourd’hui", today), ("Demain", today.AddDays(1)), ("+1 semaine", today.AddDays(7)), ("Aucune", null) })
        {
            var button = new Button { Content = label, Classes = { "filter-chip" } };
            button.Classes.Set("selected", task.DueDate == value);
            button.Click += async (_, _) => await Save(t => t with { DueDate = value }, refresh: true);
            quick.Children.Add(button);
        }
        var due = new DateBox(task.DueDate, "Aucune");
        due.SelectedDateChanged += async (_, _) => { if (due.Date != SelectedTask?.DueDate) await Save(t => t with { DueDate = due.Date }, refresh: true); };
        TaskDetail.Children.Add(FormKit.Field("Échéance", new StackPanel { Spacing = 8, Children = { quick, due } }));

        var priority = new ProjectPriorityPicker(task.Priority);
        priority.Changed += async value => await Save(t => t with { Priority = value });
        TaskDetail.Children.Add(FormKit.Field("Priorité", priority));

        var list = new AutoCompleteBox { Text = task.List, ItemsSource = AllTaskLists, FilterMode = AutoCompleteFilterMode.ContainsOrdinal, MinimumPrefixLength = 0, PlaceholderText = WorkTask.Inbox, MaxLength = 60 };
        list.LostFocus += async (_, _) =>
        {
            var value = string.IsNullOrWhiteSpace(list.Text) || list.Text.Trim() == WorkTask.Inbox ? null : list.Text.Trim();
            if (value == SelectedTask?.List) return;
            await Save(t => t with { List = value });
            if (value is not null) await RunTaskWriteAsync(root => new TaskListStore(new WorkspaceMetadataPaths(root)).Add(value), rerender: false);
        };
        TaskDetail.Children.Add(FormKit.Field("Liste", list));

        var projects = new[] { new TaskLinkChoice(null, "Aucun projet") }.Concat(_taskProjects.Where(p => !p.IsArchived || p.Profile!.Id == task.ProjectId)
            .OrderBy(p => p.Title(), StringComparer.CurrentCultureIgnoreCase).Select(p => new TaskLinkChoice(p.Profile!.Id, p.Title() + " · " + p.Reference, p.Profile.ClientId))).ToArray();
        var clients = new[] { new TaskLinkChoice(null, "Aucun client") }.Concat(_taskClients
            .OrderBy(c => c.Title(), StringComparer.CurrentCultureIgnoreCase).Select(c => new TaskLinkChoice(c.Profile!.Id, c.Title()))).ToArray();
        var project = new ComboBox { ItemsSource = projects, SelectedItem = projects.FirstOrDefault(p => p.Id == task.ProjectId) ?? projects[0] };
        var client = new ComboBox { ItemsSource = clients, SelectedItem = clients.FirstOrDefault(c => c.Id == task.ClientId) ?? clients[0], IsEnabled = task.ProjectId is null };
        project.SelectionChanged += async (_, _) =>
        {
            if (project.SelectedItem is not TaskLinkChoice chosen || chosen.Id == SelectedTask?.ProjectId) return;
            // A project carries its client; without project the client stays as it was.
            await Save(t => t with { ProjectId = chosen.Id, ClientId = chosen.Id is null ? t.ClientId : chosen.ClientId }, refresh: true);
        };
        client.SelectionChanged += async (_, _) =>
        {
            if (client.SelectedItem is not TaskLinkChoice chosen || chosen.Id == SelectedTask?.ClientId || SelectedTask?.ProjectId is not null) return;
            await Save(t => t with { ClientId = chosen.Id });
        };
        TaskDetail.Children.Add(FormKit.Field("Projet", project));
        TaskDetail.Children.Add(FormKit.Field("Client", client));

        var steps = new StackPanel { Spacing = 4 };
        foreach (var step in task.Steps ?? [])
        {
            var done = new CheckBox { IsChecked = step.IsCompleted, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(done, (step.IsCompleted ? "Rouvrir la sous-tâche : " : "Terminer la sous-tâche : ") + step.Title);
            done.IsCheckedChanged += async (_, _) => await Save(t => t with { Steps = (t.Steps ?? []).Select(s => s.Id == step.Id ? s with { IsCompleted = done.IsChecked == true } : s).ToArray() });
            var text = new TextBox { Text = step.Title, MaxLength = 500 };
            AutomationProperties.SetName(text, "Sous-tâche");
            text.LostFocus += async (_, _) =>
            {
                var value = text.Text?.Trim() ?? "";
                if (value.Length == 0) { text.Text = step.Title; return; }
                if (value != step.Title) await Save(t => t with { Steps = (t.Steps ?? []).Select(s => s.Id == step.Id ? s with { Title = value } : s).ToArray() });
            };
            var drop = new Button { Content = Icons.Glyph("IconClose", 14), Classes = { "tool" }, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(drop, "Retirer la sous-tâche : " + step.Title);
            drop.Click += async (_, _) =>
            {
                if (!await new ConfirmRemovalWindow(step.Title, "Cette sous-tâche sera retirée de la tâche.").ShowDialog<bool>(this)) return;
                await Save(t => t with { Steps = NullIfEmpty((t.Steps ?? []).Where(s => s.Id != step.Id)) }, refresh: true);
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
            Grid.SetColumn(text, 1); Grid.SetColumn(drop, 2);
            row.Children.Add(done); row.Children.Add(text); row.Children.Add(drop);
            steps.Children.Add(row);
        }
        var newStep = new TextBox { PlaceholderText = "Ajouter une sous-tâche…", MaxLength = 500 };
        AutomationProperties.SetName(newStep, "Nouvelle sous-tâche");
        newStep.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(newStep.Text)) return;
            e.Handled = true;
            var value = newStep.Text.Trim();
            if ((SelectedTask?.Steps?.Count ?? 0) >= 100) return;
            await Save(t => t with { Steps = [.. t.Steps ?? [], new TaskStep(Guid.NewGuid(), value)] }, refresh: true);
            Dispatcher.UIThread.Post(() => TaskDetail.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => AutomationProperties.GetName(b) == "Nouvelle sous-tâche")?.Focus());
        };
        steps.Children.Add(newStep);
        TaskDetail.Children.Add(FormKit.Field("Sous-tâches", steps));

        var notes = new TextBox { Text = task.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxLength = 10000, PlaceholderText = "Détails, mesures, matière…" };
        notes.LostFocus += async (_, _) =>
        {
            var value = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim();
            if (value != SelectedTask?.Notes) await Save(t => t with { Notes = value });
        };
        TaskDetail.Children.Add(FormKit.Field("Notes", notes));

        var remove = new Button { Content = Icons.Label("IconTrash", "Retirer la tâche"), Classes = { "menu-destructive" }, HorizontalAlignment = HorizontalAlignment.Left };
        remove.Click += async (_, _) => { if (SelectedTask is { } current) await RemoveTaskAsync(current); };
        var created = new TextBlock { Text = $"Créée le {task.CreatedAt.ToLocalTime():dd/MM/yyyy}" + (task.CompletedAt is { } at ? $" · terminée le {at.ToLocalTime():dd/MM/yyyy}" : "") + " · enregistrement automatique", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        TaskDetail.Children.Add(new StackPanel { Spacing = 10, Margin = new Thickness(0, 6, 0, 0), Children = { remove, created } });
    }

    private static IReadOnlyList<TaskStep>? NullIfEmpty(IEnumerable<TaskStep> steps) => steps.ToArray() is { Length: > 0 } items ? items : null;

    private async Task RemoveTaskAsync(WorkTask task)
    {
        if (!await new ConfirmRemovalWindow(task.Title, "Cette tâche, ses notes et ses sous-tâches seront retirées. Le projet et le client liés ne sont pas concernés.").ShowDialog<bool>(this)) return;
        var index = _visibleTaskIds.IndexOf(task.Id);
        var error = await RunTaskWriteAsync(root =>
        {
            var store = new TaskStore(new WorkspaceMetadataPaths(root));
            if (store.Find(task.Id) is { IsDeleted: false } current) store.Remove(current);
        }, rerender: false);
        if (error is not null) { Notify(error, error: true); return; }
        _visibleTaskIds.Remove(task.Id);
        // Keyboard flow: the next task takes the selection.
        _selectedTaskId = index >= 0 && _visibleTaskIds.Count > 0 ? _visibleTaskIds[Math.Min(index, _visibleTaskIds.Count - 1)] : null;
        RenderTasks();
    }

    // ───────────── Writing ─────────────

    private bool TaskContextCurrent(string root) => IsVisible && TasksWorkspacePanel.IsVisible && string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>Applies one field change to the latest saved copy, so two quick edits never overwrite each other.</summary>
    private async Task SaveTaskChangeAsync(Guid id, Func<WorkTask, WorkTask> change, bool refreshDetail = false)
    {
        var error = await RunTaskWriteAsync(root =>
        {
            var store = new TaskStore(new WorkspaceMetadataPaths(root));
            var current = store.Find(id) ?? throw new InvalidOperationException("La tâche est introuvable. Actualisez la liste.");
            store.Update(change(current));
        }, rerender: false);
        if (error is not null) { Notify(error, error: true); RenderTasks(); return; }
        if (TasksWorkspacePanel.IsVisible) RenderTasks(detail: refreshDetail);
    }

    /// <summary>One write at a time, off the UI thread, then the tasks and lists are read again. Returns the message to show, or null.</summary>
    private async Task<string?> RunTaskWriteAsync(Action<string> write, bool rerender = true)
    {
        var root = WorkspaceRoot;
        if (!TaskContextCurrent(root)) return "Le contexte a changé. Revenez aux tâches et réessayez.";
        await _taskGate.WaitAsync();
        _taskBusy = true;
        try
        {
            var (tasks, lists) = await Task.Run(() =>
            {
                write(root);
                var paths = new WorkspaceMetadataPaths(root);
                return (new TaskStore(paths).LoadAll(), new TaskListStore(paths).ActiveNames());
            });
            if (TaskContextCurrent(root))
            {
                (_tasks, _taskLists) = (tasks, lists);
                if (rerender) RenderTasks();
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return e.Message; }
        finally { _taskBusy = false; _taskGate.Release(); }
    }
}
