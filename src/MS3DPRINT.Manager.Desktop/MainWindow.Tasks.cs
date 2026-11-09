using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>« Tâches » page: every task of the active projects in one list, grouped by project, the closest due date first.</summary>
public partial class MainWindow
{
    private IReadOnlyList<ProjectSummary> _taskProjects = [];
    private bool _projectTaskBusy;
    private TextBlock? _newTaskError;

    private sealed record TaskProjectChoice(ProjectSummary Project)
    {
        public override string ToString() => Project.Title() + " · " + Project.ClientTitle();
    }

    /// <summary>Dashboard « prochaine tâche »: the page opens on that project, ready to add to it.</summary>
    private async Task OpenProjectTaskTrackingAsync(ProjectSummary project)
    {
        ShowWorkspace("Tâches");
        PageTitle.Text = "Tâches";
        PageSubtitle.Text = PageSubtitles["Tâches"];
        TaskFilterBox.Text = project.Reference;
        await LoadTasksAsync(project.Reference);
    }

    private async Task LoadTasksAsync(string? select = null)
    {
        RememberWorkspace("Tâches");
        var root = WorkspaceRoot;
        var load = StartWorkspaceLoad("tasks", root);
        TaskList.Children.Clear();
        try
        {
            var projects = await Task.Run(() => new WorkspaceProjectReader().Load(root));
            if (!IsWorkspaceLoadCurrent(load)) return;
            _taskProjects = projects.Where(p => p.Profile is not null && !p.IsArchived).ToArray();
            var previous = select ?? (NewTaskProject.SelectedItem as TaskProjectChoice)?.Project.Reference;
            var choices = _taskProjects.OrderBy(p => p.Title(), StringComparer.CurrentCultureIgnoreCase).Select(p => new TaskProjectChoice(p)).ToArray();
            NewTaskProject.ItemsSource = choices;
            NewTaskProject.SelectedItem = choices.FirstOrDefault(c => c.Project.Reference == previous);
            RenderTasks();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            _taskProjects = [];
            TaskList.Children.Clear();
            TaskList.Children.Add(CreateReadErrorState("Tâches indisponibles", "Vérifiez l’accès au dossier de l’atelier, puis réessayez.", exception, () => LoadTasksAsync()));
        }
    }

    private void RenderTasks()
    {
        TaskList.Children.Clear();
        var text = TaskFilterBox.Text?.Trim() ?? "";
        var showDone = ShowCompletedTasks.IsChecked == true;
        bool Visible(ProjectTask t) => showDone || !t.IsCompleted;
        bool Matches(ProjectSummary p) => text.Length == 0
            || $"{p.Reference} {p.Title()} {p.ClientTitle()}".Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || ProjectTasks.Items(p.Profile).Any(t => $"{t.Title} {t.Notes}".Contains(text, StringComparison.CurrentCultureIgnoreCase));
        var projects = _taskProjects.Where(p => ProjectTasks.Items(p.Profile).Any(Visible) && Matches(p))
            .OrderBy(p => p.Profile!.DueDate is null).ThenBy(p => p.Profile!.DueDate)
            .ThenBy(p => p.Priority == ProjectPriority.None ? 0 : -(int)p.Priority)
            .ThenBy(p => p.Title(), StringComparer.CurrentCultureIgnoreCase).ToArray();
        var open = _taskProjects.Sum(p => ProjectTasks.Items(p.Profile).Count(t => !t.IsCompleted));
        TasksSummary.Text = open switch { 0 => "Aucune tâche à faire", 1 => "1 tâche à faire", _ => $"{open} tâches à faire" };
        if (projects.Length == 0)
        {
            TaskList.Children.Add(text.Length > 0
                ? CreateEmptyState("IconSearch", "Aucune tâche trouvée", "Essayez un autre mot, ou videz la recherche.")
                : CreateEmptyState("IconCheck", "Rien à faire pour l’instant", "Ajoutez une tâche ci-dessus en choisissant son projet."));
            return;
        }
        foreach (var project in projects)
        {
            var section = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 0, 20) };
            section.Children.Add(BuildTaskProjectHeader(project));
            foreach (var task in ProjectTasks.Items(project.Profile).Where(Visible)) section.Children.Add(BuildProjectTaskRow(project, task));
            TaskList.Children.Add(section);
        }
    }

    /// <summary>Project name to open it, then client, reference and due date on one quiet line.</summary>
    private Control BuildTaskProjectHeader(ProjectSummary project)
    {
        var tasks = ProjectTasks.Items(project.Profile);
        var (deadline, variant) = ProjectDeadlineLabel(project);
        var title = new TextBlock { Text = project.Title(), FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var facts = new TextBlock { Text = $"{project.ClientTitle()} · {project.Reference} · {tasks.Count(t => t.IsCompleted)} / {tasks.Count} faites", Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis };
        var due = new TextBlock { Text = deadline, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (IBrush?)Avalonia.Application.Current?.FindResource(variant is "danger" or "warning" ? "WarningBrush" : "MutedTextBrush") };
        var text = new StackPanel { Spacing = 2, Children = { title, facts } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12 };
        var arrow = Icons.Glyph("IconArrowRight", 16);
        Grid.SetColumn(due, 1); Grid.SetColumn(arrow, 2);
        grid.Children.Add(text); grid.Children.Add(due); grid.Children.Add(arrow);
        var button = new Button { Content = grid, Tag = project, Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetName(button, "Ouvrir le projet " + project.Title());
        button.Click += ProjectEntry_Click;
        return button;
    }

    private void TaskFilterText_Changed(object? sender, TextChangedEventArgs e) { if (TasksWorkspacePanel.IsVisible) RenderTasks(); }
    private void ShowCompletedTasks_Changed(object? sender, RoutedEventArgs e) { if (TasksWorkspacePanel.IsVisible) RenderTasks(); }
    private async void AddTask_Click(object? sender, RoutedEventArgs e) => await AddTaskAsync();
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
        if (_projectTaskBusy) return;
        if (string.IsNullOrWhiteSpace(NewTaskBox.Text)) { FormKit.ShowError(_newTaskError, NewTaskBox, "Saisissez le texte de la tâche."); return; }
        if (NewTaskProject.SelectedItem is not TaskProjectChoice { Project: var project }) { FormKit.ShowError(_newTaskError, NewTaskProject, "Choisissez le projet de la tâche."); return; }
        var value = NewTaskBox.Text.Trim();
        var error = await SaveProjectTaskAsync(WorkspaceRoot, project, (store, profile) => ProjectTasks.Add(store, profile, value, null), () => NewTaskBox.Text = "");
        if (error is not null) { FormKit.ShowError(_newTaskError, NewTaskBox, error); return; }
        NewTaskBox.Focus();
    }

    private Control BuildProjectTaskRow(ProjectSummary project, ProjectTask task)
    {
        var root = WorkspaceRoot;
        var check = new CheckBox { IsChecked = task.IsCompleted, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(check, (task.IsCompleted ? "Rouvrir : " : "Terminer : ") + task.Title);
        var text = new StackPanel { Spacing = 4 };
        var title = new TextBlock { Text = task.Title, TextWrapping = TextWrapping.Wrap, TextDecorations = task.IsCompleted ? new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Strikethrough } } : null };
        if (task.IsCompleted) title.Classes.Add("faint");
        text.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(task.Notes)) text.Children.Add(new TextBlock { Text = task.Notes, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "caption" } });
        var edit = new Button { Content = text, Classes = { "task-row-action" }, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(edit, "Modifier : " + task.Title);
        edit.Click += async (_, _) =>
        {
            if (!ProjectTaskContextCurrent(root) || _projectTaskBusy) return;
            var dialog = new ProjectTaskWindow(task, (title, notes) => SaveProjectTaskAsync(root, project, (store, profile) => ProjectTasks.Edit(store, profile, task.Id, title, notes)));
            await dialog.ShowDialog<bool>(this);
        };
        var items = ProjectTasks.Items(project.Profile);
        var index = items.ToList().FindIndex(t => t.Id == task.Id);
        var up = new Button { Content = Icons.Label("IconChevronUp", "Monter"), IsEnabled = index > 0 };
        up.Click += async (_, _) => await MoveProjectTaskAsync(project, task, -1);
        var down = new Button { Content = Icons.Label("IconChevronDown", "Descendre"), IsEnabled = index < items.Count - 1 };
        down.Click += async (_, _) => await MoveProjectTaskAsync(project, task, 1);
        var remove = new Button { Content = Icons.Label("IconTrash", "Retirer la tâche"), Classes = { "menu-destructive" } };
        remove.Click += async (_, _) =>
        {
            if (!ProjectTaskContextCurrent(root) || _projectTaskBusy) return;
            if (!await new ConfirmRemovalWindow(task.Title, "Cette tâche et ses notes seront retirées du projet. Les fichiers du projet et les recettes ne sont pas concernés.").ShowDialog<bool>(this)) return;
            var error = await SaveProjectTaskAsync(root, project, (store, profile) => ProjectTasks.Remove(store, profile, task.Id));
            if (error is not null) Notify(error, error: true);
        };
        var menu = CreateOverflowActions(up, down, remove);
        menu.Classes.Add("task-row-action");
        AutomationProperties.SetName(menu, "Actions de la tâche : " + task.Title);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 8, MinHeight = 48 };
        var border = new Border { Child = row, Tag = new TaskRowIdentity(project.Profile!.Id, task.Id), Classes = { "project-task-row" } };
        var grip = CreateProjectTaskGrip(project, task, border);
        Grid.SetColumn(check, 1); Grid.SetColumn(edit, 2); Grid.SetColumn(menu, 3);
        row.Children.Add(grip); row.Children.Add(check); row.Children.Add(edit); row.Children.Add(menu);
        check.IsCheckedChanged += async (_, _) =>
        {
            var completed = check.IsChecked == true;
            var error = await SaveProjectTaskAsync(root, project, (store, profile) => ProjectTasks.SetCompleted(store, profile, task.Id, completed));
            if (error is not null)
            {
                // Rebuild from the saved snapshot: a failed write must never look completed.
                if (ProjectTaskContextCurrent(root)) RenderTasks();
                Notify(error, error: true);
            }
        };
        return border;
    }

    private async Task<bool> MoveProjectTaskAsync(ProjectSummary project, ProjectTask task, int direction)
    {
        var error = await SaveProjectTaskAsync(WorkspaceRoot, project, (store, profile) => ProjectTasks.Move(store, profile, task.Id, direction));
        if (error is not null) Notify(error, error: true);
        return error is null;
    }

    private bool ProjectTaskContextCurrent(string root) => IsVisible && TasksWorkspacePanel.IsVisible && string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase);

    private async Task<string?> SaveProjectTaskAsync(string root, ProjectSummary project, Func<ProjectProfileStore, ProjectProfile, ProjectProfile> change, Action? onSaved = null)
    {
        if (!ProjectTaskContextCurrent(root) || project.Profile is null) return "Le contexte du projet a changé. Revenez aux tâches et réessayez.";
        if (_projectTaskBusy) return "Un enregistrement est déjà en cours.";
        _projectTaskBusy = true;
        TaskList.IsEnabled = false; TaskAddArea.IsEnabled = false;
        try
        {
            var updated = await Task.Run(() => change(new ProjectProfileStore(new WorkspaceMetadataPaths(root)), project.Profile));
            onSaved?.Invoke();
            if (ProjectTaskContextCurrent(root))
            {
                _taskProjects = _taskProjects.Select(p => p.Profile?.Id == updated.Id ? p with { Profile = updated } : p).ToArray();
                var scroll = TaskList.Parent as ScrollViewer; var offset = scroll?.Offset;
                RenderTasks();
                if (scroll is not null && offset is { } previous) Dispatcher.UIThread.Post(() => { if (ProjectTaskContextCurrent(root)) scroll.Offset = previous; });
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return e.Message + " Actualisez la liste avant de réessayer ; votre saisie reste disponible."; }
        finally
        {
            _projectTaskBusy = false;
            TaskList.IsEnabled = true; TaskAddArea.IsEnabled = true;
        }
    }
}
