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

    /// <summary>
    /// The project picker of the page (adding, filter, each task): a search box, the recent projects when nothing is typed,
    /// up to 8 matches otherwise, and the « none » choice. Enter takes the first match.
    /// </summary>
    private Flyout ProjectPicker(Guid? current, string none, bool withOpenTasks, Func<Guid?, Task> apply)
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var search = new TextBox { PlaceholderText = "Chercher un projet, un client…" };
        AutomationProperties.SetName(search, "Chercher un projet");
        var heading = new TextBlock { Classes = { "caption" } };
        var results = new StackPanel { Spacing = 4 };
        Button Choice(Guid? id, string label)
        {
            var button = new Button { Content = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis }, Classes = { "filter-chip" },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Classes.Set("selected", id == current);
            AutomationProperties.SetName(button, "Projet : " + label);
            ToolTip.SetTip(button, label);
            button.Click += async (_, _) => { flyout.Hide(); await apply(id); };
            return button;
        }
        void Fill()
        {
            results.Children.Clear();
            var typed = !string.IsNullOrWhiteSpace(search.Text);
            var found = TaskProjectPicker.Rank(_taskProjects, _tasks, search.Text, withOpenTasks, current);
            heading.Text = typed ? (found.Count == 0 ? "Aucun projet trouvé" : found.Count == TaskProjectPicker.Matches ? "Premiers résultats · précisez pour affiner" : "Résultats")
                : found.Count == 0 ? "Tapez pour chercher un projet" : "Récents";
            foreach (var project in found) results.Children.Add(Choice(project.Profile!.Id, $"{project.Title()} · {project.ClientTitle()} · {project.Reference}"));
            results.Children.Add(Choice(null, none));
        }
        search.TextChanged += (_, _) => Fill();
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || results.Children.FirstOrDefault() is not Button first) return;
            e.Handled = true;
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        flyout.Opened += (_, _) => { search.Text = ""; Fill(); Dispatcher.UIThread.Post(() => search.Focus()); };
        Fill();
        // A fixed width: long project names are cut with « … » instead of widening the popup past the screen.
        flyout.Content = new StackPanel { Spacing = 8, Width = 420, Children = { search, heading, results } };
        return flyout;
    }

    private void RenderTaskProjectFilter()
    {
        var project = TaskProject(_taskProjectFilter);
        TaskProjectFilterButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { new TextBlock { Text = project?.Title() ?? "Tous les projets", MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, Icons.Glyph("IconChevronDown", 14) }
        };
        ToolTip.SetTip(TaskProjectFilterButton, project is null ? "Afficher les tâches d’un seul projet" : project.Title() + " · changer de projet");
        TaskProjectFilterButton.Flyout = ProjectPicker(_taskProjectFilter, "Tous les projets", withOpenTasks: true, id =>
        {
            _taskProjectFilter = id;
            _newTaskProject = id;
            RenderTasks();
            return Task.CompletedTask;
        });
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
        RenderTaskProjectFilter();
        RenderNewTaskChips(today);

        TaskBackButton.IsVisible = _taskProjectFilter is not null;
        TaskList.Children.Clear();
        var searching = !string.IsNullOrWhiteSpace(TaskFilterBox.Text);
        var (blocks, done) = TaskViews.ByProject(_tasks, today, _taskProjectFilter, TaskFilterBox.Text);
        if (blocks.Count == 0 && done.Count == 0)
        {
            TaskList.Children.Add(searching
                ? CreateEmptyState("IconSearch", "Aucune tâche trouvée", "Essayez un autre mot, ou videz la recherche.")
                : CreateEmptyState("IconCheck", "Rien à faire", "Tapez une tâche dans la ligne ci-dessus, puis Entrée."));
            return;
        }
        Control? highlighted = null;
        void Rows(IEnumerable<WorkTask> tasks)
        {
            foreach (var task in tasks)
            {
                var row = BuildTaskRow(task, today);
                if (task.Id == _selectedTaskId) highlighted = row;
                TaskList.Children.Add(row);
                if (_openTaskNotes.Contains(task.Id)) TaskList.Children.Add(BuildTaskNotes(task));
            }
        }
        foreach (var block in blocks)
        {
            var key = block.ProjectId?.ToString() ?? "Sans projet";
            // A search shows every match, even in a folded project.
            var collapsed = _collapsedTaskGroups.Contains(key) && !searching;
            // Showing one project, its name is already the page title.
            if (_taskProjectFilter is null) TaskList.Children.Add(BuildTaskProjectHeading(block, key, collapsed, today));
            if (!collapsed) Rows(block.Tasks);
        }
        if (done.Count > 0)
        {
            var collapsed = _collapsedTaskGroups.Contains("Terminées") && !searching;
            var header = new Button { Classes = { "task-group" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 24, 0, 4) };
            var count = new Border { Classes = { "task-chip" }, Padding = new Thickness(8, 0), Child = new TextBlock { Text = done.Count.ToString(), Classes = { "caption" } } };
            header.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Icons.Glyph(collapsed ? "IconArrowRight" : "IconChevronDown", 14), new TextBlock { Text = "Terminées", FontWeight = FontWeight.SemiBold }, count } };
            AutomationProperties.SetName(header, (collapsed ? "Déplier " : "Replier ") + "Terminées");
            header.Click += (_, _) => { if (!_collapsedTaskGroups.Add("Terminées")) _collapsedTaskGroups.Remove("Terminées"); RenderTasks(); };
            TaskList.Children.Add(header);
            if (!collapsed) Rows(done);
        }
        // Opened from the dashboard: that task shows once, highlighted, then the page behaves as usual.
        if (highlighted is not null) { highlighted.Classes.Add("highlight"); Dispatcher.UIThread.Post(() => highlighted.BringIntoView()); }
        _selectedTaskId = null;
    }

    /// <summary>
    /// A project's heading, the strongest line of the page: its name, the client and reference under it, how many tasks are left
    /// and whether one is late or when the next is due. The chevron folds it; the name shows that project alone.
    /// </summary>
    private Control BuildTaskProjectHeading(TaskProjectBlock block, string key, bool collapsed, DateOnly today)
    {
        var project = TaskProject(block.ProjectId);
        var label = project is null ? "Sans projet" : project.Title();
        var fold = new Button { Content = Icons.Glyph(collapsed ? "IconArrowRight" : "IconChevronDown", 16), Classes = { "tool" }, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(fold, (collapsed ? "Déplier " : "Replier ") + label);
        fold.Click += (_, _) => { if (!_collapsedTaskGroups.Add(key)) _collapsedTaskGroups.Remove(key); RenderTasks(); };

        var name = new TextBlock { Text = label, FontSize = Ui.Text.Section, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { name } };
        if (project is not null) text.Children.Add(new TextBlock { Text = $"{project.ClientTitle()} · {project.Reference}", Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
        // Without a project the name is plain text: the button padding is added so both names line up.
        Control title = text;
        if (project is null) text.Margin = new Thickness(16, 0, 0, 0);
        if (project is not null)
        {
            var open = new Button { Content = text, Classes = { "task-row-action" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(open, "Projet " + project.ClientTitle() + " · " + project.Title());
            ToolTip.SetTip(open, "Afficher seulement ce projet");
            open.Click += (_, _) => { _taskProjectFilter = project.Profile!.Id; _newTaskProject = _taskProjectFilter; RenderTasks(); };
            title = open;
        }

        var facts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Border Chip(string value, bool warning = false)
        {
            var caption = new TextBlock { Text = value, Classes = { "caption" } };
            if (warning) caption.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") ?? Brushes.Orange;
            return new Border { Classes = { "task-chip" }, Padding = new Thickness(8, 0), Child = caption };
        }
        facts.Children.Add(Chip(block.Open switch { 0 => "Tout est fait", 1 => "1 à faire", _ => $"{block.Open} à faire" }));
        if (block.Late) facts.Children.Add(Chip("En retard", warning: true));
        else if (block.Next is { } next) facts.Children.Add(Chip("Prochaine : " + DueLabel(next, today)));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(title, 1); Grid.SetColumn(facts, 2);
        grid.Children.Add(fold); grid.Children.Add(title); grid.Children.Add(facts);
        var card = new Border { Child = grid, Classes = { "task-project" }, Margin = new Thickness(0, 16, 0, 4) };
        card.Classes.Set("late", block.Late);
        return card;
    }

    private void TaskBack_Click(object? sender, RoutedEventArgs e) => ShowAllTaskProjects();

    private void ShowAllTaskProjects()
    {
        _taskProjectFilter = null;
        _newTaskProject = null;
        RenderTasks();
    }

    // ───────────── Adding: one line, today by default, the project of the filter ─────────────

    private void RenderNewTaskChips(DateOnly today)
    {
        NewTaskDateButton.Content = Icons.Label("IconToday", _newTaskDate is { } date ? DueLabel(date, today) : "Sans date", 14);
        NewTaskDateButton.Flyout = DateFlyout(_newTaskDate, value => { _newTaskDate = value; RenderNewTaskChips(TaskToday); return Task.CompletedTask; });
        ToolTip.SetTip(NewTaskDateButton, "Date de la nouvelle tâche");
        var project = TaskProject(_newTaskProject);
        NewTaskProjectButton.Content = Icons.Label("IconProjects", project is null ? "Projet" : project.Title().Length > 28 ? project.Title()[..27] + "…" : project.Title(), 14);
        ToolTip.SetTip(NewTaskProjectButton, project is null ? "Rattacher la nouvelle tâche à un projet" : $"{project.Title()} · {project.ClientTitle()}");
        NewTaskProjectButton.Flyout = ProjectPicker(_newTaskProject, "Aucun projet", withOpenTasks: false, id => { _newTaskProject = id; RenderNewTaskChips(TaskToday); return Task.CompletedTask; });
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
        var projectItem = new MenuItem { Header = "Projet…", Icon = Icons.Glyph("IconProjects", 14) };
        // The picker opens from the line once the menu has closed.
        projectItem.Click += (_, _) => Dispatcher.UIThread.Post(() => ProjectPicker(task.ProjectId, "Aucun projet", withOpenTasks: false, id =>
            SaveTaskChangeAsync(task.Id, t => t with { ProjectId = id, ClientId = id is null ? t.ClientId : TaskProject(id)?.Profile!.ClientId ?? t.ClientId })).ShowAt(more));
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
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Escape && _taskProjectFilter is not null) { ShowAllTaskProjects(); e.Handled = true; }
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
