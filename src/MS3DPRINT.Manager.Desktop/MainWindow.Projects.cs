using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Projects page: list, filters, creation, edition, rename, move and trash.</summary>
public partial class MainWindow
{
    private async void RefreshProjects_Click(object? sender, RoutedEventArgs e) => await LoadProjectsAsync();

    private async void RecentProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        _projectOriginClient = null;
        ShowWorkspaceForDetail("Projets");
        PageTitle.Text = project.Title();
        PageSubtitle.Text = $"{project.Reference} · {project.ClientTitle()}";
        await LoadProjectDirectoryAsync(project, project.ProjectPath);
    }

    private async void DashboardProjects_Click(object? sender, RoutedEventArgs e)
    {
        _projectOriginClient = null;
        ShowWorkspace("Projets");
        PageTitle.Text = "Projets";
        PageSubtitle.Text = PageSubtitles["Projets"];
        await LoadProjectsAsync();
    }

    private async void CreateProject_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        var clients = await Task.Run(() => new WorkspaceClientReader().Load(root));
        var dialog = new CreateProjectWindow(clients);
        if (await dialog.ShowDialog<bool>(this) && dialog.Client is { } client)
        {
            try
            {
                var projectName = dialog.ProjectName;
                var priority = dialog.Priority;
                var trackedClient = await EnsurePriorityClientAsync(client, priority);
                if (trackedClient is null) return;
                BeginBusy();
                try { await Task.Run(() => new WorkspaceProjectCreator().CreateTracked(root, trackedClient, DateTime.Today.Year, projectName, priority)); }
                finally { EndBusy(); }
                PageTitle.Text = "Projets";
                Notify("Projet créé.");
                ShowWorkspace("Projets");
                await LoadProjectsAsync();
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
            {
                Notify(exception.Message, error: true);
            }
        }
    }

    private async void CreateProjectForClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        var dialog = new CreateProjectWindow([client], client);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Client is not { } selected) return;
        try
        {
            var projectName = dialog.ProjectName;
            var priority = dialog.Priority;
            var trackedClient = await EnsurePriorityClientAsync(selected, priority);
            if (trackedClient is null) return;
            var root = WorkspaceRoot;
            await Task.Run(() => new WorkspaceProjectCreator().CreateTracked(root, trackedClient, DateTime.Today.Year, projectName, priority));
            Notify("Projet créé.");
            await LoadClientDetailAsync(trackedClient with { ProjectCount = trackedClient.ProjectCount + 1 });
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
        {
            Notify(exception.Message, error: true);
        }
    }

    private IReadOnlyList<ProjectSummary> _allProjects = [];
    private bool _projectCatalogUnavailable;

    private int _projectSortColumn = -1;

    private bool _projectSortAscending = true;

    private int _clientSortColumn = -1;

    private bool _clientSortAscending = true;

    private string _projectStatusFilter = string.Empty;

    private async Task LoadProjectsAsync()
    {
        RememberWorkspace("Projets");
        var workspaceRoot = WorkspaceRoot;
        var load = StartWorkspaceLoad("projects", workspaceRoot);
        _projectCatalogUnavailable = true;
        ProjectList.Children.Clear();
        ProjectList.Spacing = 0;
        _projectListMode = true;
        ProjectsPanelTitle.Text = "Tous les projets";
        ProjectsPanelActions.IsVisible = true;
        ProjectsPanelHeader.IsVisible = true;
        ProjectFilterBar.IsVisible = true;
        ProjectFilterBar.IsEnabled = false;
        ProjectsPanelSubtitle.Text = "Chargement des projets…";

        try
        {
            var projects = await Task.Run(() => new WorkspaceProjectReader().Load(workspaceRoot));
            if (!IsWorkspaceLoadCurrent(load)) return;
            _allProjects = projects;
            _projectCatalogUnavailable = false;
            ProjectFilterBar.IsEnabled = true;
            RenderProjects();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            _allProjects = [];
            ProjectList.Children.Clear();
            ProjectsPanelSubtitle.Text = "Lecture des projets indisponible";
            ProjectList.Children.Add(CreateReadErrorState("Projets indisponibles", "Vérifiez l’accès au dossier de l’atelier, puis réessayez.", exception, LoadProjectsAsync));
        }
    }

    /// <summary>An archived project only shows under « Archivés »; every other filter, « Tous » included, lists the active ones.</summary>
    private static bool MatchesProjectStatus(ProjectSummary project, string filter) => filter switch
    {
        "ARCHIVES" => project.IsArchived,
        _ when project.IsArchived => false,
        "" => true,
        "TODO" => project.Profile is null,
        var status => project.StatusLabel == status
    };

    private void RenderProjects()
    {
        if (!_projectListMode || _projectCatalogUnavailable) return;
        UpdatePrioritySortButton();
        ProjectList.Children.Clear();
        var text = ProjectFilterBox.Text?.Trim() ?? string.Empty;
        var projects = _allProjects
            .Where(project => MatchesProjectStatus(project, _projectStatusFilter))
            .Where(project => _projectPriorityFilter is null || project.Priority == _projectPriorityFilter)
            .Where(project => text.Length == 0 ||
                              $"{project.Reference} {project.ProjectName} {project.ClientFolderName}".Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
        var archivedCount = _allProjects.Count(project => project.IsArchived);
        var scopeCount = _projectStatusFilter == "ARCHIVES" ? archivedCount : _allProjects.Count - archivedCount;
        ProjectsPanelSubtitle.Text = (projects.Length == scopeCount
            ? (scopeCount switch { 0 => "Aucun projet", 1 => "1 projet", var count => $"{count} projets" })
            : $"{projects.Length} sur {scopeCount} projets")
            + (_projectStatusFilter != "ARCHIVES" && archivedCount > 0 ? $" · {archivedCount} archivé{(archivedCount > 1 ? "s" : "")}" : string.Empty);

        if (_allProjects.Count == 0)
        {
            ProjectFilterBar.IsVisible = false;
            ProjectList.Children.Add(CreateEmptyState("IconProjects", "Aucun projet pour l’instant", "Créez un projet pour suivre un devis, une fabrication et ses fichiers.", "Nouveau projet", CreateProject_Click));
            return;
        }
        if (projects.Length == 0)
        {
            ProjectList.Children.Add(CreateEmptyState("IconSearch", "Aucun résultat", "Essayez un autre nom ou réaffichez tous les projets actifs.", "Effacer les filtres", ClearProjectFilters_Click));
            return;
        }

        projects = SortProjects(projects);
        PagedRows.Render(ProjectList, projects, CreateProjectRow, () => CreateSortableHeader(ProjectRowColumns, ["PROJET", "CLIENT", "STATUT", "ÉCHÉANCE", "ACTIONS"], _projectSortColumn, _projectSortAscending, column =>
        {
            _projectSortAscending = _projectSortColumn != column || !_projectSortAscending;
            _projectSortColumn = column;
            RenderProjects();
        }), (WorkspaceRoot, text, _projectStatusFilter, _projectPriorityFilter, _projectSortColumn, _projectSortAscending));
    }

    private Control CreateProjectRow(ProjectSummary project)
        {
            var open = new Button { Tag = project, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            open.Click += ProjectEntry_Click;
            var folder = new Button { Content = Icons.Label("IconOpen", "Ouvrir le dossier"), Tag = project.ProjectPath }; folder.Click += OpenFolder_Click;
            var edit = new Button { Content = Icons.Label("IconEdit", "Modifier la fiche"), Tag = project }; edit.Click += EditProject_Click;
            var rename = new Button { Content = Icons.Label("IconRename", "Renommer"), Tag = project }; rename.Click += RenameProject_Click;
            var move = new Button { Content = Icons.Label("IconMove", "Déplacer"), Tag = project }; move.Click += MoveProject_Click;
            var duplicate = new Button { Content = Icons.Label("IconCopy", "Dupliquer le projet"), Tag = project }; duplicate.Click += DuplicateProject_Click;
            var trash = new Button { Content = Icons.Label("IconTrash", "Corbeille"), Tag = project, Classes = { "menu-destructive" } }; trash.Click += TrashProject_Click;
            var row = (Grid)CreateActionRow(ProjectRowColumns, open, CreateOverflowActions(folder, edit, duplicate, rename, move, trash),
                CreateProjectNameCell(project),
                CreateProjectClientCell(project.ClientTitle()),
                CreateStatusPicker(project),
                CreateDueDateCell(project));
            AddProjectQuickEdit(row, project);
            return row;
    }

    private static Control CreateProjectClientCell(string name)
    {
        var label = new TextBlock { Text = name, FontSize = 14, LineHeight = 19, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(label, name);
        return label;
    }

    /// <summary>The status of a project in the list; clicking it offers the three statuses, without opening the sheet.</summary>
    private Control CreateStatusPicker(ProjectSummary project)
    {
        var options = new[] { (ProjectStatus.Quote, "À faire · 0 %"), (ProjectStatus.InProgress, "En cours · 50 %"), (ProjectStatus.Completed, "Terminé · 100 %") }
            .Select(option =>
            {
                var button = new Button { Content = option.Item2 };
                button.Click += async (_, _) => await SetProjectStatusAsync(project, option.Item1);
                return button;
            }).ToArray();
        var menu = (Flyout)CreateOverflowActions(PlacementMode.BottomEdgeAlignedLeft, options).Tag!;
        var picker = new Button { Content = CreateProjectStatusCell(project), Classes = { "ghost", "interactive-cell" }, Padding = new Avalonia.Thickness(6, 4), HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(-7, 0, 0, 0) }; // padding 6 + border 1: the ring starts where the STATUT heading and the other cells start
        ToolTip.SetTip(picker, "Changer le statut");
        Avalonia.Automation.AutomationProperties.SetName(picker, "Changer le statut de " + project.Reference);
        picker.Click += (_, _) => menu.ShowAt(picker);
        return picker;
    }

    /// <summary>A finished project goes to the 99_ARCHIVES of its client, any other status brings it back. A failed move keeps the saved status and says so.</summary>
    private async Task<(string Path, string Note)> SyncProjectArchiveAsync(string root, ProjectSummary project, ProjectStatus status)
    {
        var archive = status == ProjectStatus.Completed;
        if (archive == project.IsArchived) return (project.ProjectPath, string.Empty);
        try
        {
            var service = new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root);
            var path = await Task.Run(() => archive ? service.ArchiveProject(project) : service.RestoreProject(project));
            return (path, archive ? " Dossier archivé." : " Dossier sorti des archives.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return (project.ProjectPath, $" Le statut est enregistré, mais le dossier n’a pas été {(archive ? "archivé" : "restauré")} : {exception.Message}");
        }
    }

    private async Task SetProjectStatusAsync(ProjectSummary project, ProjectStatus status)
    {
        try
        {
            var root = WorkspaceRoot;
            var profile = project.Profile;
            if (profile is null)
            {
                // a project from before the sheets: its sheet is created now, named after its folder, then the status is set
                var client = (await Task.Run(() => new WorkspaceClientReader().Load(root)))
                    .FirstOrDefault(candidate => string.Equals(candidate.ClientPath, project.ClientPath, StringComparison.OrdinalIgnoreCase));
                if (client is null) { Notify("Client introuvable pour ce projet.", error: true); return; }
                var clientProfile = client.Profile ?? await CompleteClientProfileAsync(client);
                if (clientProfile is null) return;
                var name = DisplayNames.Format(project.ProjectName);
                profile = await Task.Run(() => new WorkspaceProjectCreator().CompleteExisting(root, project, clientProfile, name));
            }
            else if (profile.Status == status && project.IsArchived == (status == ProjectStatus.Completed)) return;
            var updated = await Task.Run(() => new WorkspaceProfileEditor().UpdateProject(root, profile, status, profile.DueDate, profile.Description, profile.Notes));
            var (path, archiveNote) = await SyncProjectArchiveAsync(root, project with { Profile = updated }, status);
            if (path != project.ProjectPath) await LoadProjectsAsync(); // the fiche now holds the new folder: read it back
            else
            {
                _allProjects = _allProjects.Select(candidate => candidate.ProjectPath == project.ProjectPath ? project with { Profile = updated } : candidate).ToList();
                RenderProjects();
            }
            Notify((status switch { ProjectStatus.Completed => "Projet terminé.", ProjectStatus.InProgress => "Projet en cours.", _ => "Projet à faire." }) + archiveNote);
            await PreloadAsync(force: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Notify(exception.Message, error: true);
        }
    }

    /// <summary>The reference small and quiet, then the name that identifies the project at a glance.</summary>
    private Control CreateProjectNameCell(ProjectSummary project)
    {
        var reference = new TextBlock { Text = project.Reference, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = project.Title(), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxLines = 2, LineHeight = 20, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(title, project.Reference + " · " + project.Title());
        title.FontSize = 15;
        var cell = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        cell.Children.Add(title); cell.Children.Add(reference);
        ToolTip.SetTip(cell, project.Title() + "\n" + project.Reference);
        cell.IsHitTestVisible = false;
        var priority = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Classes = { "interactive-cell" } };
        priority.Children.Add(CreateProjectPriorityButton(project));
        Grid.SetColumn(cell, 1); priority.Children.Add(cell);
        return priority;
    }

    /// <summary>The due date; one that has passed on a project that is not finished is said so, in the warning colour.</summary>
    private static Control CreateDueDateCell(ProjectSummary project)
    {
        if (project.Profile?.DueDate is not { } due) return new TextBlock { Text = "—", Classes = { "faint" }, VerticalAlignment = VerticalAlignment.Center };
        var late = project.StatusLabel != "TERMINE" && due < DateOnly.FromDateTime(DateTime.Today);
        var text = new TextBlock { Text = late ? $"En retard · {due:dd/MM}" : $"{due:dd/MM/yyyy}", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (late)
        {
            text.Foreground = (IBrush?)Avalonia.Application.Current?.FindResource("WarningBrush") ?? Brushes.Orange;
            text.FontWeight = FontWeight.SemiBold;
            ToolTip.SetTip(text, $"Prévu le {due:dd/MM/yyyy}");
        }
        else text.Classes.Add("muted");
        return text;
    }

    private ProjectSummary[] SortProjects(ProjectSummary[] projects)
    {
        if (_projectSortColumn == 4)
            return projects.OrderBy(project => project.Priority == ProjectPriority.None ? 3 : _projectSortAscending ? 3 - (int)project.Priority : (int)project.Priority - 1)
                .ThenBy(project => project.Reference, StringComparer.OrdinalIgnoreCase).ToArray();
        if (_projectSortColumn < 0) return projects;
        IOrderedEnumerable<ProjectSummary> ordered = _projectSortColumn switch
        {
            1 => projects.OrderBy(project => project.ClientTitle(), StringComparer.CurrentCultureIgnoreCase),
            2 => projects.OrderBy(project => project.StatusLabel switch { "A_FAIRE" => 0, "EN_COURS" => 1, _ => 2 }),
            3 => projects.OrderBy(project => project.Profile?.DueDate is null).ThenBy(project => project.Profile?.DueDate),
            _ => projects.OrderBy(project => project.Title(), StringComparer.CurrentCultureIgnoreCase)
        };
        return (_projectSortAscending ? ordered : ordered.Reverse()).ToArray();
    }

    private ClientSummary[] SortClients(ClientSummary[] clients)
    {
        if (_clientSortColumn < 0) return clients;
        IOrderedEnumerable<ClientSummary> ordered = _clientSortColumn switch
        {
            1 => clients.OrderBy(client => client.ClientCode, StringComparer.OrdinalIgnoreCase),
            2 => clients.OrderBy(client => client.ProfileLabel, StringComparer.CurrentCultureIgnoreCase),
            3 => clients.OrderBy(client => client.ProjectCount),
            _ => clients.OrderBy(client => client.Title(), StringComparer.CurrentCultureIgnoreCase)
        };
        return (_clientSortAscending ? ordered : ordered.Reverse()).ToArray();
    }

    private void ProjectFilterText_Changed(object? sender, TextChangedEventArgs e)
    {
        if (ProjectList is not null) RenderProjects();
    }

    private void ClearProjectFilters_Click(object? sender, RoutedEventArgs e)
    {
        _projectPriorityFilterBox.SelectedIndex = 0;
        _projectPriorityFilter = null;
        _projectStatusFilter = string.Empty;
        SelectChip(ProjectStatusFilters, string.Empty);
        ProjectFilterBox.Text = string.Empty;
        RenderProjects();
    }

    private void AddProjectQuickEdit(Grid row, ProjectSummary project)
    {
        row.Classes.Add("project-row");
        row.MinHeight = 76;
        var overflow = row.Children.OfType<Button>().Last();
        overflow.HorizontalAlignment = HorizontalAlignment.Right;
        var edit = new Button { Content = Icons.Glyph("IconEdit", 18), Tag = project, Classes = { "icon-action", "quick-row-action" }, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(edit, "Modifier " + project.Title());
        ToolTip.SetTip(edit, "Modifier la fiche du projet");
        edit.Click += EditProject_Click;
        Grid.SetColumn(edit, row.ColumnDefinitions.Count - 1);
        row.Children.Add(edit);
    }

    private void ProjectFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string status }) return;
        _projectStatusFilter = status;
        SelectChip(ProjectStatusFilters, status);
        RenderProjects();
    }

    private static void SelectChip(Panel chips, string tag)
    {
        foreach (var chip in chips.Children.OfType<Button>())
        {
            if (chip.Tag as string == tag) chip.Classes.Add("selected"); else chip.Classes.Remove("selected");
        }
    }

    private async void ProjectEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Projects.ProjectSummary project }) return;
        // A project opened from a client sheet remembers that client, so "Retour" goes back to it.
        _projectOriginClient = ClientsWorkspacePanel.IsVisible ? _currentClientDetail : null;
        ShowWorkspaceForDetail("Projets");
        PageTitle.Text = project.Title();
        PageSubtitle.Text = $"{project.Reference} · {project.ClientTitle()}";
        await LoadProjectDirectoryAsync(project, project.ProjectPath);
    }

    private void AddProjectOverview(ProjectSummary project)
    {
        ProjectList.Children.Add(BuildProjectOverview(project));
    }

    private async void EditProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        if (project.Profile is null)
        {
            var root = WorkspaceRoot;
            var client = (await Task.Run(() => new WorkspaceClientReader().Load(root)))
                .FirstOrDefault(candidate => string.Equals(candidate.ClientPath, project.ClientPath, StringComparison.OrdinalIgnoreCase));
            if (client is null) { Notify("Client introuvable pour ce projet.", error: true); return; }
            var clientProfile = client.Profile ?? await CompleteClientProfileAsync(client);
            if (clientProfile is null) return;
            var completion = new TextPromptWindow("Compléter la fiche projet", "Le dossier et les fichiers existants seront conservés. Le nom proposé retrouve les accents que le nom de dossier a perdus : corrigez-le si besoin.", DisplayNames.Format(project.ProjectName));
            if (!await completion.ShowDialog<bool>(this)) return;
            try
            {
                var completedName = completion.Value;
                var completed = await Task.Run(() => new WorkspaceProjectCreator().CompleteExisting(root, project, clientProfile, completedName));
                Notify("Fiche projet historique complétée.");
                if (_projectListMode) await LoadProjectsAsync();
                else await LoadProjectDirectoryAsync(project with { Profile = completed }, project.ProjectPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { Notify(exception.Message, error: true); }
            return;
        }
        var profile = project.Profile;
        var dialog = new EditProjectWindow(profile);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var (status, dueDate, description, notes, name, priority) = (dialog.Status, dialog.DueDate, dialog.Description, dialog.Notes, dialog.ProjectName, dialog.Priority);
            var updated = await Task.Run(() => new WorkspaceProfileEditor().UpdateProject(root, profile, status, dueDate, description, notes, name, priority));
            var (path, archiveNote) = await SyncProjectArchiveAsync(root, project with { Profile = updated }, status);
            Notify("Fiche projet enregistrée." + archiveNote);
            if (_projectListMode) await LoadProjectsAsync();
            else await LoadProjectDirectoryAsync(project with { Profile = updated, ProjectPath = path }, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Notify(exception.Message, error: true);
        }
    }

    private async void TrashProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        if (!await new ConfirmTrashWindow(project.Reference, "Tous les fichiers et sous-dossiers du projet sont concernés.").ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).TrashProject(project));
            PageTitle.Text = "Projets";
            Notify("Projet envoyé dans la corbeille.");
            await LoadProjectsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void RenameProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        var dialog = new TextPromptWindow("Renommer le projet", "La référence du projet restera inchangée.", project.ProjectName);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var newName = dialog.Value;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenameProject(project, newName));
            PageTitle.Text = "Projets";
            Notify("Projet renommé.");
            await LoadProjectsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void MoveProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        var root = WorkspaceRoot;
        var clients = await Task.Run(() => new WorkspaceClientReader().Load(root)
            .Where(client => !string.Equals(client.ClientPath, project.ClientPath, StringComparison.OrdinalIgnoreCase)).ToArray());
        var dialog = new ClientPickerWindow(clients);
        if (!await dialog.ShowDialog<bool>(this) || dialog.SelectedClient is not { } destination) return;
        try
        {
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).MoveProject(project, destination));
            PageTitle.Text = "Projets";
            Notify("Projet déplacé.");
            await LoadProjectsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }
}
