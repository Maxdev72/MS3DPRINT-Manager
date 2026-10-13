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
        ShowWorkspace("Projets");
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
                BeginBusy();
                try { await Task.Run(() => new WorkspaceProjectCreator().CreateTracked(root, client, DateTime.Today.Year, projectName)); }
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
            var root = WorkspaceRoot;
            await Task.Run(() => new WorkspaceProjectCreator().CreateTracked(root, selected, DateTime.Today.Year, projectName));
            Notify("Projet créé.");
            await LoadClientDetailAsync(client with { ProjectCount = client.ProjectCount + 1 });
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
        {
            Notify(exception.Message, error: true);
        }
    }

    private IReadOnlyList<ProjectSummary> _allProjects = [];

    private int _projectSortColumn = -1;

    private bool _projectSortAscending = true;

    private int _clientSortColumn = -1;

    private bool _clientSortAscending = true;

    private string _projectStatusFilter = string.Empty;

    private async Task LoadProjectsAsync()
    {
        var workspaceRoot = WorkspaceRoot;
        ProjectList.Children.Clear();
        ProjectList.Spacing = 0;
        _projectListMode = true;
        ProjectsPanelTitle.Text = "Tous les projets";
        ProjectsPanelActions.IsVisible = true;
        ProjectsPanelHeader.IsVisible = true;
        ProjectFilterBar.IsVisible = true;

        try
        {
            _allProjects = await Task.Run(() => new WorkspaceProjectReader().Load(workspaceRoot));
            RenderProjects();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ProjectList.Children.Add(CreateEmptyState("IconAlert", "Impossible de lire les projets", exception.Message));
        }
    }

    private static bool MatchesProjectStatus(ProjectSummary project, string filter) => filter switch
    {
        "" => true,
        "TODO" => project.Profile is null,
        var status => project.Profile is not null && project.StatusLabel == status
    };

    private void RenderProjects()
    {
        if (!_projectListMode) return;
        ProjectList.Children.Clear();
        var text = ProjectFilterBox.Text?.Trim() ?? string.Empty;
        var projects = _allProjects
            .Where(project => MatchesProjectStatus(project, _projectStatusFilter))
            .Where(project => text.Length == 0 ||
                              $"{project.Reference} {project.ProjectName} {project.ClientFolderName}".Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
        ProjectsPanelSubtitle.Text = projects.Length == _allProjects.Count
            ? (_allProjects.Count switch { 0 => "Aucun projet", 1 => "1 projet", var count => $"{count} projets" })
            : $"{projects.Length} sur {_allProjects.Count} projets";

        if (_allProjects.Count == 0)
        {
            ProjectFilterBar.IsVisible = false;
            ProjectList.Children.Add(CreateEmptyState("IconProjects", "Aucun projet pour l’instant", "Créez un projet pour suivre un devis, une fabrication et ses fichiers.", "Nouveau projet", CreateProject_Click));
            return;
        }
        if (projects.Length == 0)
        {
            ProjectList.Children.Add(CreateEmptyState("IconSearch", "Aucun résultat", "Aucun projet ne correspond à ce filtre."));
            return;
        }

        projects = SortProjects(projects);
        ProjectList.Children.Add(CreateSortableHeader(ActionRowColumns, ["PROJET", "CLIENT", "STATUT", "ACTIONS"], _projectSortColumn, _projectSortAscending, column =>
        {
            _projectSortAscending = _projectSortColumn != column || !_projectSortAscending;
            _projectSortColumn = column;
            RenderProjects();
        }));
        foreach (var project in projects)
        {
            var open = new Button { Tag = project, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            open.Click += ProjectEntry_Click;
            var folder = new Button { Content = Icons.Label("IconOpen", "Ouvrir le dossier"), Tag = project.ProjectPath }; folder.Click += OpenFolder_Click;
            var edit = new Button { Content = Icons.Label("IconEdit", "Modifier la fiche"), Tag = project }; edit.Click += EditProject_Click;
            var rename = new Button { Content = Icons.Label("IconRename", "Renommer"), Tag = project }; rename.Click += RenameProject_Click;
            var move = new Button { Content = Icons.Label("IconMove", "Déplacer"), Tag = project }; move.Click += MoveProject_Click;
            var trash = new Button { Content = Icons.Label("IconTrash", "Corbeille"), Tag = project }; trash.Click += TrashProject_Click;
            ProjectList.Children.Add(CreateActionRow(open, CreateOverflowActions(folder, edit, rename, move, trash),
                CreateCompactTableCell($"{project.Reference} · {project.Title()}", prominent: true),
                CreateCompactTableCell(project.ClientTitle()),
                CreateStatusBadge(project.StatusLabel)));
        }
    }

    private ProjectSummary[] SortProjects(ProjectSummary[] projects)
    {
        if (_projectSortColumn < 0) return projects;
        IOrderedEnumerable<ProjectSummary> ordered = _projectSortColumn switch
        {
            1 => projects.OrderBy(project => project.ClientTitle(), StringComparer.CurrentCultureIgnoreCase),
            2 => projects.OrderBy(project => project.StatusLabel switch { "DEVIS" => 0, "EN_COURS" => 1, "TERMINE" => 2, _ => 3 }),
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
        ShowWorkspace("Projets");
        PageTitle.Text = project.Title();
        PageSubtitle.Text = $"{project.Reference} · {project.ClientTitle()}";
        await LoadProjectDirectoryAsync(project, project.ProjectPath);
    }

    private void AddProjectOverview(ProjectSummary project)
    {
        var edit = new Button { Content = Icons.Label("IconEdit", "Modifier la fiche"), Tag = project, VerticalAlignment = VerticalAlignment.Center };
        edit.Click += EditProject_Click;
        var badge = CreateStatusBadge(project.Profile is null ? "À compléter" : project.StatusLabel);

        var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        summary.Children.Add(badge);
        summary.Children.Add(new TextBlock
        {
            Text = project.Profile?.DueDate is { } due ? $"Échéance le {due:dd/MM/yyyy}" : "Aucune échéance",
            Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center
        });

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(edit, 1);
        header.Children.Add(summary);
        header.Children.Add(edit);

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(header);
        if (!string.IsNullOrWhiteSpace(project.Profile?.Description))
            content.Children.Add(new TextBlock { Text = project.Profile.Description, TextWrapping = TextWrapping.Wrap });
        else if (project.Profile is null)
            content.Children.Add(new TextBlock { Text = "La fiche de ce projet est à compléter.", Classes = { "muted" } });
        ProjectList.Children.Add(new Border { Classes = { "card-raised" }, Margin = new Avalonia.Thickness(0, 0, 0, 10), Child = content });
    }

    private async void EditProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        if (project.Profile is null)
        {
            var root = WorkspaceRoot;
            var client = (await Task.Run(() => new WorkspaceClientReader().Load(root)))
                .FirstOrDefault(candidate => string.Equals(candidate.ClientPath, project.ClientPath, StringComparison.OrdinalIgnoreCase));
            if (client?.Profile is null) { Notify("Complétez d’abord la fiche de ce client historique.", error: true); return; }
            var completion = new TextPromptWindow("Compléter la fiche projet", "Le dossier et les fichiers existants seront conservés. Le nom proposé retrouve les accents que le nom de dossier a perdus : corrigez-le si besoin.", DisplayNames.Format(project.ProjectName));
            if (!await completion.ShowDialog<bool>(this)) return;
            try
            {
                var completedName = completion.Value;
                var completed = await Task.Run(() => new WorkspaceProjectCreator().CompleteExisting(root, project, client.Profile, completedName));
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
            var (status, dueDate, description, notes, name) = (dialog.Status, dialog.DueDate, dialog.Description, dialog.Notes, dialog.ProjectName);
            var updated = await Task.Run(() => new WorkspaceProfileEditor().UpdateProject(root, profile, status, dueDate, description, notes, name));
            Notify("Fiche projet enregistrée.");
            if (_projectListMode) await LoadProjectsAsync();
            else await LoadProjectDirectoryAsync(project with { Profile = updated }, project.ProjectPath);
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
