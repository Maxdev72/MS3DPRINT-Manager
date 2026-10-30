using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private bool _projectTrackingView;
    private bool _hideCompletedProjectTasks;
    private bool _projectTaskBusy;
    private string? _expandedTaskProject;
    private string? _taskWorkspace;
    private readonly Dictionary<string, string> _projectTaskDrafts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Open the current checklist from the dashboard, regardless of previous project filters.</summary>
    private async Task OpenProjectTaskTrackingAsync(ProjectSummary project)
    {
        _projectTrackingView = true;
        _projectCalendarView = false;
        _taskWorkspace = WorkspaceRoot;
        _expandedTaskProject = project.Reference;
        _projectOriginClient = null;
        _projectStatusFilter = project.IsArchived ? "ARCHIVES" : "";
        SelectChip(ProjectStatusFilters, _projectStatusFilter);
        _projectPriorityFilter = null;
        _projectPriorityFilterBox.SelectedIndex = 0;
        ProjectFilterBox.Text = project.Reference;
        ShowWorkspace("Projets");
        PageTitle.Text = "Projets";
        PageSubtitle.Text = "Suivi des tâches · " + project.Title();
        await LoadProjectsAsync();
    }

    private void ProjectView_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        _projectTrackingView = tag == "tracking";
        _projectCalendarView = tag == "calendar";
        RenderProjects();
    }

    private bool MatchesProjectTaskSearch(ProjectSummary project, string text)
        => _projectTrackingView && ProjectTasks.Items(project.Profile).Any(t => $"{t.Title} {t.Notes}".Contains(text, StringComparison.CurrentCultureIgnoreCase));

    private void RenderProjectTracking(ProjectSummary[] projects)
    {
        if (!string.Equals(_taskWorkspace, WorkspaceRoot, StringComparison.OrdinalIgnoreCase))
        { _expandedTaskProject = null; _taskWorkspace = WorkspaceRoot; }
        var hide = new CheckBox { Content = "Masquer les tâches terminées", IsChecked = _hideCompletedProjectTasks, Margin = new Thickness(8, 0, 0, 12) };
        hide.IsCheckedChanged += (_, _) => { _hideCompletedProjectTasks = hide.IsChecked == true; RenderProjects(); };
        PagedRows.Render(ProjectList, projects, BuildProjectTaskGroup, () => hide,
            (WorkspaceRoot, ProjectFilterBox.Text, _projectStatusFilter, _projectPriorityFilter, _projectSortColumn, _projectSortAscending, _hideCompletedProjectTasks));
    }

    private Control BuildProjectTaskGroup(ProjectSummary project)
    {
        var root = WorkspaceRoot;
        var tasks = ProjectTasks.Items(project.Profile);
        var title = new StackPanel { Spacing = 5 };
        title.Children.Add(new TextBlock { Text = project.Title(), FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        title.Children.Add(new TextBlock { Text = project.ClientTitle() + " · " + project.Reference, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        var next = ProjectTasks.Next(project.Profile);
        title.Children.Add(new TextBlock { Text = next is not null ? "Prochaine tâche : " + next.Title : tasks.Count > 0 ? "Toutes les tâches sont terminées" : "Aucune tâche pour ce projet", Classes = { "queue-next-action" }, TextWrapping = TextWrapping.Wrap });
        var facts = new WrapPanel { ItemSpacing = 16, LineSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        facts.Children.Add(CreateStatusBadge(project.StatusLabel));
        if (project.Priority != ProjectPriority.None) facts.Children.Add(ProjectPriorityVisual.Label(project.Priority));
        var (deadline, _) = ProjectDeadlineLabel(project);
        facts.Children.Add(new TextBlock { Text = deadline, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center });
        facts.Children.Add(new TextBlock { Text = $"{tasks.Count(t => t.IsCompleted)} / {tasks.Count} tâches", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(facts);
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(8, 8, 8, 12) };
        if (project.Profile is null)
        {
            body.Children.Add(new TextBlock { Text = "Complétez la fiche de ce projet historique pour y ajouter des tâches. Ses fichiers seront conservés.", TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
            var complete = new Button { Content = "Compléter la fiche projet", Tag = project };
            complete.Click += EditProject_Click; body.Children.Add(complete);
        }
        else
        {
            foreach (var task in tasks.Where(t => !_hideCompletedProjectTasks || !t.IsCompleted)) body.Children.Add(BuildProjectTaskRow(project, task));
            if (_hideCompletedProjectTasks && tasks.Count > 0 && next is null)
                body.Children.Add(new TextBlock { Text = "Les tâches terminées sont masquées.", Classes = { "caption" } });
            var draftKey = root + "|" + project.Profile.Id;
            var input = new TextBox { Name = "QuickProjectTask", Tag = project.Profile.Id, Text = _projectTaskDrafts.GetValueOrDefault(draftKey), PlaceholderText = "Ajouter une tâche…", MaxLength = 500 };
            input.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.TextProperty) _projectTaskDrafts[draftKey] = input.Text ?? "";
            };
            AutomationProperties.SetName(input, "Nouvelle tâche pour " + project.Title());
            var error = FormKit.ErrorText();
            var add = new Button { Content = Icons.Label("IconPlus", "Ajouter"), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(add, "Ajouter une tâche à " + project.Title());
            async Task Add()
            {
                if (_projectTaskBusy) return;
                if (string.IsNullOrWhiteSpace(input.Text)) { FormKit.ShowError(error, input, "Saisissez le texte de la tâche."); return; }
                var value = input.Text.Trim();
                error.Text = await SaveProjectTaskAsync(root, project, (store, profile) => ProjectTasks.Add(store, profile, value, null), () => _projectTaskDrafts.Remove(draftKey));
                error.IsVisible = error.Text is not null;
                if (error.Text is null && ProjectTaskContextCurrent(root))
                    Dispatcher.UIThread.Post(() => ProjectList.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "QuickProjectTask" && Equals(t.Tag, project.Profile.Id))?.Focus());
            }
            add.Click += async (_, _) => await Add();
            input.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Add(); } };
            var quick = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetColumn(add, 1); quick.Children.Add(input); quick.Children.Add(add); body.Children.Add(quick); body.Children.Add(error);
        }
        var open = new Button { Content = Icons.Label("IconOpen", "Ouvrir le projet"), Tag = project, Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += ProjectEntry_Click; body.Children.Add(open);
        var group = new Expander { Header = title, Content = body, IsExpanded = _expandedTaskProject == project.Reference, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Classes = { "estimate-section", "project-task-group" }, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(group, "Tâches de " + project.Title());
        group.Expanded += (_, _) =>
        {
            _expandedTaskProject = project.Reference;
            foreach (var other in ProjectList.GetLogicalDescendants().OfType<Expander>().Where(x => x.Classes.Contains("project-task-group") && x != group)) other.IsExpanded = false;
        };
        group.Collapsed += (_, _) => { if (_expandedTaskProject == project.Reference) _expandedTaskProject = null; };
        return group;
    }

    private Control BuildProjectTaskRow(ProjectSummary project, ProjectTask task)
    {
        var root = WorkspaceRoot;
        var check = new CheckBox { IsChecked = task.IsCompleted, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
        AutomationProperties.SetName(check, (task.IsCompleted ? "Rouvrir : " : "Terminer : ") + task.Title);
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(new TextBlock { Text = task.Title, TextWrapping = TextWrapping.Wrap, TextDecorations = task.IsCompleted ? new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Strikethrough } } : null, Opacity = task.IsCompleted ? .65 : 1 });
        if (!string.IsNullOrWhiteSpace(task.Notes)) text.Children.Add(new TextBlock { Text = task.Notes, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "caption" } });
        var edit = new Button { Content = text, Classes = { "ghost" }, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
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
        AutomationProperties.SetName(menu, "Actions de la tâche : " + task.Title);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 8, MinHeight = 48 };
        var border = new Border { Child = row, Tag = new TaskRowIdentity(project.Profile!.Id, task.Id), Classes = { "project-task-row" }, Padding = new Thickness(0, 4) };
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
                if (ProjectTaskContextCurrent(root)) RenderProjects();
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

    private bool ProjectTaskContextCurrent(string root) => IsVisible && ProjectsWorkspacePanel.IsVisible && _projectListMode && _projectTrackingView && string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase);

    private async Task<string?> SaveProjectTaskAsync(string root, ProjectSummary project, Func<ProjectProfileStore, ProjectProfile, ProjectProfile> change, Action? onSaved = null)
    {
        if (!ProjectTaskContextCurrent(root) || project.Profile is null) return "Le contexte du projet a changé. Revenez au suivi et réessayez.";
        if (_projectTaskBusy) return "Un enregistrement est déjà en cours.";
        _projectTaskBusy = true;
        ProjectList.IsEnabled = false; ProjectFilterBar.IsEnabled = false; ProjectViewTabs.IsEnabled = false;
        try
        {
            var updated = await Task.Run(() => change(new ProjectProfileStore(new WorkspaceMetadataPaths(root)), project.Profile));
            onSaved?.Invoke();
            if (ProjectTaskContextCurrent(root))
            {
                _allProjects = _allProjects.Select(p => p.Profile?.Id == updated.Id ? p with { Profile = updated } : p).ToArray();
                var scroll = ProjectList.Parent as ScrollViewer; var offset = scroll?.Offset;
                RenderProjects();
                if (scroll is not null && offset is { } previous) Dispatcher.UIThread.Post(() => { if (ProjectTaskContextCurrent(root)) scroll.Offset = previous; });
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return e.Message + " Actualisez la liste avant de réessayer ; votre saisie reste disponible."; }
        finally
        {
            _projectTaskBusy = false;
            ProjectList.IsEnabled = true; ProjectViewTabs.IsEnabled = true;
            ProjectFilterBar.IsEnabled = !_projectCatalogUnavailable;
        }
    }
}
