using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Tasks;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>A project offered in the task filter and menus, or none.</summary>
public sealed record TaskProjectChoice(Guid? Id, string Label, Guid? ClientId = null)
{
    public override string ToString() => Label;
}

/// <summary>
/// « Tâches » page, a single sheet like the paper one: a line to add a task (today by default), then the tasks by date —
/// late, today, this week, later, and the last week's done ones. Each line edits in place: tick, title, flag, date, menu.
/// </summary>
public partial class MainWindow
{
    private IReadOnlyList<WorkTask> _tasks = [];
    private IReadOnlyList<ProjectSummary> _taskProjects = [];
    private Guid? _taskProjectFilter;
    private string? _tasksLoadedRoot;
    /// <summary>A task to show highlighted once (opened from the dashboard).</summary>
    private Guid? _selectedTaskId;
    private Guid? _editingTaskId;
    private readonly HashSet<Guid> _openTaskNotes = [];
    private readonly HashSet<string> _collapsedTaskGroups = ["Terminées"];
    private DateOnly? _newTaskDate;
    private Guid? _newTaskProject;
    private bool _syncingTaskFilter;
    private readonly SemaphoreSlim _taskGate = new(1, 1);
    private bool _taskBusy;
    private bool _taskKeysHooked;
    private TextBlock? _newTaskError;

    /// <summary>Dashboard « prochaine tâche » and the project sheet: the page opens filtered on that project.</summary>
    private async Task OpenProjectTaskTrackingAsync(ProjectSummary project)
    {
        ShowWorkspace("Tâches");
        PageTitle.Text = "Tâches";
        PageSubtitle.Text = PageSubtitles["Tâches"];
        _taskProjectFilter = project.Profile?.Id;
        _selectedTaskId = null;
        TaskFilterBox.Text = "";
        await LoadTasksAsync();
    }

    private async Task LoadTasksAsync()
    {
        RememberWorkspace("Tâches");
        HookTaskKeys();
        var root = WorkspaceRoot;
        var load = StartWorkspaceLoad("tasks", root);
        try
        {
            var (tasks, projects) = await Task.Run(() =>
            {
                TaskMigration.FromProjects(root);
                return (new TaskStore(new WorkspaceMetadataPaths(root)).LoadAll(), new WorkspaceProjectReader().Load(root));
            });
            if (!IsWorkspaceLoadCurrent(load)) return;
            _tasksLoadedRoot = root;
            _tasks = tasks;
            _taskProjects = projects.Where(p => p.Profile is not null).ToArray();
            _newTaskDate = TaskToday;
            _newTaskProject = _taskProjectFilter;
            FillTaskProjectFilter();
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

    private ProjectSummary? TaskProject(Guid? id) => id is null ? null : _taskProjects.FirstOrDefault(p => p.Profile!.Id == id);

    /// <summary>Active projects, plus the one a task or the filter already points to.</summary>
    private IReadOnlyList<TaskProjectChoice> TaskProjectChoices(string none, Guid? keep = null) =>
        [new(null, none), .. _taskProjects.Where(p => !p.IsArchived && p.Profile!.Status != ProjectStatus.Completed || p.Profile!.Id == keep)
            .OrderBy(p => p.Title(), StringComparer.CurrentCultureIgnoreCase).Select(p => new TaskProjectChoice(p.Profile!.Id, p.Reference + " · " + p.Title(), p.Profile.ClientId))];

    private void FillTaskProjectFilter()
    {
        var choices = TaskProjectChoices("Tous les projets", _taskProjectFilter);
        _syncingTaskFilter = true;
        try
        {
            TaskProjectFilter.ItemsSource = choices;
            TaskProjectFilter.SelectedItem = choices.FirstOrDefault(c => c.Id == _taskProjectFilter) ?? choices[0];
        }
        finally { _syncingTaskFilter = false; }
    }

    private void TaskProjectFilter_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingTaskFilter || TaskProjectFilter.SelectedItem is not TaskProjectChoice choice) return;
        _taskProjectFilter = choice.Id;
        _newTaskProject = choice.Id;
        RenderTasks();
    }

    // Typing can arrive before the first load: rendering then would judge the page on empty data.
    private void TaskFilterText_Changed(object? sender, TextChangedEventArgs e) { if (TasksWorkspacePanel.IsVisible && _tasksLoadedRoot == WorkspaceRoot) RenderTasks(); }

    private void RenderTasks()
    {
        var today = TaskToday;
        if (_taskProjectFilter is { } filter && TaskProject(filter) is null) _taskProjectFilter = null;
        TaskViewTitle.Text = TaskProject(_taskProjectFilter)?.Title() ?? "Tâches";
        var open = _tasks.Count(t => !t.IsDeleted && !t.IsCompleted && (_taskProjectFilter is null || t.ProjectId == _taskProjectFilter));
        TasksSummary.Text = open switch { 0 => "Rien à faire", 1 => "1 tâche", _ => $"{open} tâches" };
        RenderNewTaskChips(today);

        TaskList.Children.Clear();
        var groups = TaskViews.Sheet(_tasks, today, _taskProjectFilter, TaskFilterBox.Text);
        if (groups.Count == 0)
        {
            TaskList.Children.Add(!string.IsNullOrWhiteSpace(TaskFilterBox.Text)
                ? CreateEmptyState("IconSearch", "Aucune tâche trouvée", "Essayez un autre mot, ou videz la recherche.")
                : CreateEmptyState("IconCheck", "Rien à faire", "Tapez une tâche dans la ligne ci-dessus, puis Entrée."));
            return;
        }
        Control? highlighted = null;
        foreach (var group in groups)
        {
            // A search shows every match, even in a folded group.
            var collapsed = _collapsedTaskGroups.Contains(group.Title) && string.IsNullOrWhiteSpace(TaskFilterBox.Text);
            var header = new Button { Classes = { "task-group" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 4) };
            var text = new TextBlock { Text = group.Title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            if (group.Late) text.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") ?? Brushes.Orange;
            var count = new Border { Classes = { "task-chip" }, Padding = new Thickness(8, 0), Child = new TextBlock { Text = group.Tasks.Count.ToString(), Classes = { "caption" } } };
            header.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Icons.Glyph(collapsed ? "IconArrowRight" : "IconChevronDown", 14), text, count } };
            AutomationProperties.SetName(header, (collapsed ? "Déplier " : "Replier ") + group.Title);
            header.Click += (_, _) => { if (!_collapsedTaskGroups.Add(group.Title)) _collapsedTaskGroups.Remove(group.Title); RenderTasks(); };
            TaskList.Children.Add(header);
            if (collapsed) continue;
            foreach (var task in group.Tasks)
            {
                var row = BuildTaskRow(task, today);
                if (task.Id == _selectedTaskId) highlighted = row;
                TaskList.Children.Add(row);
                if (_openTaskNotes.Contains(task.Id)) TaskList.Children.Add(BuildTaskNotes(task));
            }
        }
        // Opened from the dashboard: that task shows once, highlighted, then the page behaves as usual.
        if (highlighted is not null) { highlighted.Classes.Add("highlight"); Dispatcher.UIThread.Post(() => highlighted.BringIntoView()); }
        _selectedTaskId = null;
    }

    // ───────────── Adding: one line, today by default, the project of the filter ─────────────

    private void RenderNewTaskChips(DateOnly today)
    {
        NewTaskDateButton.Content = Icons.Label("IconToday", _newTaskDate is { } date ? DueLabel(date, today) : "Sans date", 14);
        NewTaskDateButton.Flyout = DateFlyout(_newTaskDate, value => { _newTaskDate = value; RenderNewTaskChips(TaskToday); return Task.CompletedTask; });
        ToolTip.SetTip(NewTaskDateButton, "Date de la nouvelle tâche");
        var project = TaskProject(_newTaskProject);
        NewTaskProjectButton.Content = Icons.Label("IconProjects", project?.Reference ?? "Projet", 14);
        ToolTip.SetTip(NewTaskProjectButton, project is null ? "Rattacher la nouvelle tâche à un projet" : project.Title());
        NewTaskProjectButton.Flyout = ProjectMenu(_newTaskProject, id => { _newTaskProject = id; RenderNewTaskChips(TaskToday); return Task.CompletedTask; });
    }

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
        var title = NewTaskBox.Text?.Trim() ?? "";
        if (title.Length == 0) { FormKit.ShowError(_newTaskError, NewTaskBox, "Saisissez le texte de la tâche."); return; }
        if (title.Length > 500) { FormKit.ShowError(_newTaskError, NewTaskBox, "Le texte de la tâche est limité à 500 caractères."); return; }
        var project = TaskProject(_newTaskProject);
        var task = WorkTask.New(title, _tasks.Count == 0 ? 0 : _tasks.Max(t => t.Order) + 1) with
        {
            DueDate = _newTaskDate, ProjectId = project?.Profile!.Id, ClientId = project?.Profile!.ClientId
        };
        var error = await RunTaskWriteAsync(root => new TaskStore(new WorkspaceMetadataPaths(root)).Create(task));
        if (error is not null) { FormKit.ShowError(_newTaskError, NewTaskBox, error); return; }
        NewTaskBox.Text = "";
        _newTaskDate = TaskToday;
        RenderNewTaskChips(TaskToday);
        NewTaskBox.Focus();
    }

    // ───────────── One line per task ─────────────

    private Control BuildTaskRow(WorkTask task, DateOnly today)
    {
        var check = new CheckBox { IsChecked = task.IsCompleted, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(check, (task.IsCompleted ? "Rouvrir : " : "Terminer : ") + task.Title);
        check.IsCheckedChanged += async (_, _) =>
        {
            var done = check.IsChecked == true;
            if (done == task.IsCompleted) return;
            await SaveTaskChangeAsync(task.Id, t => t with { CompletedAt = done ? DateTimeOffset.UtcNow : null });
            if (done) Notify($"« {task.Title} » terminée.", action: ("Annuler", () => _ = SaveTaskChangeAsync(task.Id, t => t with { CompletedAt = null })));
        };

        Control title;
        if (_editingTaskId == task.Id)
        {
            var box = new TextBox { Text = task.Title, MaxLength = 500, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(box, "Texte de la tâche");
            var done = false;
            async Task Commit(bool keep)
            {
                if (done) return;
                done = true;
                _editingTaskId = null;
                var value = box.Text?.Trim() ?? "";
                if (keep && value.Length > 0 && value != task.Title) await SaveTaskChangeAsync(task.Id, t => t with { Title = value });
                else RenderTasks();
            }
            box.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; await Commit(true); }
                else if (e.Key == Key.Escape) { e.Handled = true; await Commit(false); }
            };
            box.LostFocus += async (_, _) => await Commit(true);
            Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); });
            title = box;
        }
        else
        {
            var text = new TextBlock { Text = task.Title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            if (task.IsCompleted)
            {
                text.Classes.Add("faint");
                text.TextDecorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Strikethrough } };
            }
            var edit = new Button { Content = text, Tag = task.Id, Classes = { "task-row-action" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(edit, "Modifier : " + task.Title);
            ToolTip.SetTip(edit, "Cliquer pour modifier le texte");
            edit.Click += (_, _) => { _editingTaskId = task.Id; RenderTasks(); };
            title = edit;
        }

        var cells = new List<Control>();
        if (!string.IsNullOrWhiteSpace(task.Notes) || _openTaskNotes.Contains(task.Id))
        {
            var open = _openTaskNotes.Contains(task.Id);
            var notes = new Button { Content = Icons.Glyph(open ? "IconChevronUp" : "IconChevronDown", 14), Classes = { "tool" }, VerticalAlignment = VerticalAlignment.Center };
            var tip = open ? "Masquer les notes" : "Afficher les notes";
            ToolTip.SetTip(notes, tip); AutomationProperties.SetName(notes, tip + " : " + task.Title);
            notes.Click += (_, _) => ToggleTaskNotes(task.Id);
            cells.Add(notes);
        }
        if (task.ProjectId is { } projectId && projectId != _taskProjectFilter && TaskProject(projectId) is { } project)
        {
            var pill = new Border { Classes = { "task-chip" }, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 160, Child = new TextBlock { Text = project.Reference, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis } };
            ToolTip.SetTip(pill, project.Title());
            cells.Add(pill);
        }
        var flagged = task.Priority != ProjectPriority.None;
        var flag = new Button { Content = Icons.Glyph("IconFlag", 16), Classes = { "tool" }, VerticalAlignment = VerticalAlignment.Center };
        if (flagged) flag.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") ?? Brushes.Orange;
        else flag.Classes.Add("task-quiet");
        var flagTip = flagged ? "Important · cliquer pour retirer" : "Marquer comme important";
        ToolTip.SetTip(flag, flagTip); AutomationProperties.SetName(flag, (flagged ? "Retirer Important : " : "Important : ") + task.Title);
        flag.Click += async (_, _) => await SaveTaskChangeAsync(task.Id, t => t with { Priority = t.Priority == ProjectPriority.None ? ProjectPriority.High : ProjectPriority.None });
        cells.Add(flag);

        var dateText = new TextBlock { Text = task.DueDate is { } due ? DueLabel(due, today) : "Date", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        if (!task.IsCompleted && task.DueDate < today) dateText.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") ?? Brushes.Orange;
        var date = new Button { Content = dateText, Classes = { "ghost" }, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 4) };
        if (task.DueDate is null) date.Classes.Add("task-quiet");
        date.Flyout = DateFlyout(task.DueDate, value => SaveTaskChangeAsync(task.Id, t => t with { DueDate = value }));
        ToolTip.SetTip(date, task.DueDate is { } d ? d.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("fr-FR")) + " · changer la date" : "Choisir une date");
        AutomationProperties.SetName(date, "Date : " + task.Title);
        cells.Add(date);

        var more = new Button { Content = Icons.Glyph("IconMore", 16), Classes = { "tool", "task-quiet" }, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(more, "Actions : " + task.Title); ToolTip.SetTip(more, "Notes, projet, supprimer");
        var notesItem = new MenuItem { Header = string.IsNullOrWhiteSpace(task.Notes) ? "Ajouter une note" : "Notes", Icon = Icons.Glyph("IconEdit", 14) };
        notesItem.Click += (_, _) => { _openTaskNotes.Add(task.Id); RenderTasks(); };
        var projectItem = new MenuItem { Header = "Projet", Icon = Icons.Glyph("IconProjects", 14) };
        foreach (var choice in TaskProjectChoices("Aucun projet", task.ProjectId))
        {
            var item = new MenuItem { Header = choice.Label, IsChecked = choice.Id == task.ProjectId, ToggleType = MenuItemToggleType.Radio };
            item.Click += async (_, _) => await SaveTaskChangeAsync(task.Id, t => t with { ProjectId = choice.Id, ClientId = choice.Id is null ? t.ClientId : choice.ClientId });
            projectItem.Items.Add(item);
        }
        var remove = new MenuItem { Header = "Supprimer…", Icon = Icons.Glyph("IconTrash", 14), Classes = { "menu-destructive" } };
        remove.Click += async (_, _) => await RemoveTaskAsync(task);
        more.Flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight, Items = { notesItem, projectItem, new Separator(), remove } };
        cells.Add(more);

        var row = new Grid { ColumnSpacing = 8, MinHeight = 44 };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.Children.Add(check);
        Grid.SetColumn(title, 1); row.Children.Add(title);
        foreach (var cell in cells)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(cell, row.ColumnDefinitions.Count - 1);
            row.Children.Add(cell);
        }
        return new Border { Child = row, Tag = task.Id, Classes = { "project-task-row", "task-row" }, Padding = new Thickness(8, 0, 4, 0) };
    }

    /// <summary>The notes, unfolded under the line; saved when leaving the box.</summary>
    private Control BuildTaskNotes(WorkTask task)
    {
        var notes = new TextBox { Text = task.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxLength = 10000, PlaceholderText = "Détails, mesures, matière…" };
        AutomationProperties.SetName(notes, "Notes : " + task.Title);
        notes.LostFocus += async (_, _) =>
        {
            var value = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim();
            if (value != task.Notes) await SaveTaskChangeAsync(task.Id, t => t with { Notes = value });
        };
        if (string.IsNullOrWhiteSpace(task.Notes)) Dispatcher.UIThread.Post(() => notes.Focus());
        return new Border { Child = notes, Padding = new Thickness(32, 4, 4, 8) };
    }

    private void ToggleTaskNotes(Guid id)
    {
        if (!_openTaskNotes.Remove(id)) _openTaskNotes.Add(id);
        RenderTasks();
    }

    /// <summary>Today, tomorrow, next week, no date, or any day of the calendar.</summary>
    private static Flyout DateFlyout(DateOnly? current, Func<DateOnly?, Task> apply)
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var today = TaskToday;
        var quick = new StackPanel { Spacing = 4 };
        foreach (var (label, value) in new (string, DateOnly?)[] { ("Aujourd’hui", today), ("Demain", today.AddDays(1)), ("Dans une semaine", today.AddDays(7)), ("Sans date", null) })
        {
            var button = new Button { Content = label, Classes = { "filter-chip" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Classes.Set("selected", current == value);
            button.Click += async (_, _) => { flyout.Hide(); await apply(value); };
            quick.Children.Add(button);
        }
        var calendar = new Avalonia.Controls.Calendar { FirstDayOfWeek = DayOfWeek.Monday, SelectedDate = current?.ToDateTime(TimeOnly.MinValue), DisplayDate = (current ?? today).ToDateTime(TimeOnly.MinValue) };
        calendar.SelectedDatesChanged += async (_, _) =>
        {
            if (calendar.SelectedDate is not { } picked || DateOnly.FromDateTime(picked) == current) return;
            flyout.Hide();
            await apply(DateOnly.FromDateTime(picked));
        };
        flyout.Content = new StackPanel { Spacing = 8, Children = { quick, calendar } };
        return flyout;
    }

    private MenuFlyout ProjectMenu(Guid? current, Func<Guid?, Task> apply)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var choice in TaskProjectChoices("Aucun projet", current))
        {
            var item = new MenuItem { Header = choice.Label, IsChecked = choice.Id == current, ToggleType = MenuItemToggleType.Radio };
            item.Click += async (_, _) => await apply(choice.Id);
            menu.Items.Add(item);
        }
        return menu;
    }

    private static string DueLabel(DateOnly due, DateOnly today) => (due.DayNumber - today.DayNumber) switch
    {
        0 => "Aujourd’hui", 1 => "Demain", -1 => "Hier",
        (> 1 and < 7) or (< -1 and > -7) => CultureInfo.GetCultureInfo("fr-FR").DateTimeFormat.GetAbbreviatedDayName(due.DayOfWeek) + " " + due.ToString("dd/MM"),
        _ => due.ToString("dd/MM/yyyy")
    };

    // ───────────── Keyboard: N to add, Ctrl+F to search ─────────────

    private void HookTaskKeys()
    {
        if (_taskKeysHooked) return;
        _taskKeysHooked = true;
        TasksWorkspacePanel.AddHandler(KeyDownEvent, TasksPanel_KeyDown, RoutingStrategies.Tunnel);
    }

    private void TasksPanel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_panel is not null || e.Source is not Visual source || source.FindAncestorOfType<TextBox>(includeSelf: true) is not null) return;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F) { TaskFilterBox.Focus(); e.Handled = true; }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.N) { NewTaskBox.Focus(); e.Handled = true; }
    }

    // ───────────── Writing ─────────────

    private async Task RemoveTaskAsync(WorkTask task)
    {
        if (!await new ConfirmRemovalWindow(task.Title, "Cette tâche et ses notes seront retirées. Le projet lié n’est pas concerné.").ShowDialog<bool>(this)) return;
        var error = await RunTaskWriteAsync(root =>
        {
            var store = new TaskStore(new WorkspaceMetadataPaths(root));
            if (store.Find(task.Id) is { IsDeleted: false } current) store.Remove(current);
        });
        if (error is not null) Notify(error, error: true);
    }

    private bool TaskContextCurrent(string root) => IsVisible && TasksWorkspacePanel.IsVisible && string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>Applies one field change to the latest saved copy, so two quick edits never overwrite each other.</summary>
    private async Task SaveTaskChangeAsync(Guid id, Func<WorkTask, WorkTask> change)
    {
        var error = await RunTaskWriteAsync(root =>
        {
            var store = new TaskStore(new WorkspaceMetadataPaths(root));
            var current = store.Find(id) ?? throw new InvalidOperationException("La tâche est introuvable. Actualisez la liste.");
            store.Update(change(current));
        });
        if (error is not null) { Notify(error, error: true); RenderTasks(); }
    }

    /// <summary>One write at a time, off the UI thread, then the tasks are read again and the sheet redrawn. Returns the message to show, or null.</summary>
    private async Task<string?> RunTaskWriteAsync(Action<string> write)
    {
        var root = WorkspaceRoot;
        if (!TaskContextCurrent(root)) return "Le contexte a changé. Revenez aux tâches et réessayez.";
        await _taskGate.WaitAsync();
        _taskBusy = true;
        try
        {
            var tasks = await Task.Run(() =>
            {
                write(root);
                return new TaskStore(new WorkspaceMetadataPaths(root)).LoadAll();
            });
            if (TaskContextCurrent(root)) { _tasks = tasks; RenderTasks(); }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return e.Message; }
        finally { _taskBusy = false; _taskGate.Release(); }
    }
}
