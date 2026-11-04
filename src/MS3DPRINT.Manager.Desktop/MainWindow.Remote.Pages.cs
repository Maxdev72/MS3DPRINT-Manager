using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private Guid? _serverClientDetail, _serverProjectDetail;
    private IReadOnlyList<ClientResponse> ServerClients => _serverConnection?.Copy?.Clients ?? [];
    private IReadOnlyList<ProjectResponse> ServerProjects => _serverConnection?.Copy?.Projects ?? [];
    private string ServerClientName(Guid id) => ServerClients.FirstOrDefault(c => c.Id == id)?.DisplayName ?? "Client indisponible";
    private static string ServerStatus(string status) => status switch { "InProgress" => "EN_COURS", "Completed" => "TERMINE", _ => "A_FAIRE" };
    private Button ServerAction(string label, Func<Task> action, bool write = false, string? name = null)
    {
        var connection = _serverConnection; var generation = _serverGeneration;
        var button = new Button { Content = label, Name = name, IsEnabled = !write || _serverConnection?.CanWrite == true };
        button.Click += async (_, _) => { if (connection is not null && ServerContextCurrent(connection, generation)) await action(); };
        return button;
    }
    private static TextBlock ServerText(string text, bool title = false) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = title ? 18 : 14, FontWeight = title ? FontWeight.SemiBold : FontWeight.Normal };

    private void RenderServerDashboard()
    {
        var projects = ServerProjects;
        ProjectCountText.Text = projects.Count.ToString();
        QuoteCountText.Text = projects.Count(p => p.Status == "Quote").ToString();
        InProgressCountText.Text = projects.Count(p => p.Status == "InProgress").ToString();
        NavClientsCount.Text = ServerClients.Count.ToString(); NavClientsBadge.IsVisible = ServerClients.Count > 0;
        NavProjectsCount.Text = projects.Count.ToString(); NavProjectsBadge.IsVisible = projects.Count > 0;
        RecentProjectsList.Children.Clear(); AttentionList.Children.Clear(); RecentFilesList.Children.Clear();
        RecentFilesList.Children.Add(ServerText("Fichiers indisponibles en mode serveur."));
        AttentionList.Children.Add(ServerText(_serverConnection?.IsOnline == true ? "Lecture automatique toutes les 30 secondes." : "Copie de consultation · reconnectez-vous pour modifier."));
        foreach (var project in projects.Where(p => p.Status != "Completed").OrderBy(p => p.DueDate ?? DateOnly.MaxValue).Take(8))
        {
            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(ServerText(project.ProjectName, true));
            content.Children.Add(ServerText(project.Reference + " · " + ServerClientName(project.ClientId)));
            content.Children.Add(ServerText(project.Tasks.FirstOrDefault(t => !t.IsCompleted)?.Title ?? (project.Tasks.Count > 0 ? "Toutes les tâches sont terminées" : "Aucune tâche")));
            var button = ServerAction(project.ProjectName, () => OpenServerProjectAsync(project.Id));
            button.Content = content; button.Classes.Add("table-row");
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            RecentProjectsList.Children.Add(button);
        }
        if (projects.Count == 0) RecentProjectsList.Children.Add(ServerText(_serverConnection?.Copy is null ? "Connectez-vous pour lire les projets de votre atelier." : "Aucun projet sur le serveur."));
    }

    private void RenderServerClients()
    {
        if (ClientList is null) return;
        ClientList.Children.Clear(); ClientList.Spacing = 0;
        _clientListMode = true;
        ClientsPanelTitle.Text = "Tous les clients";
        ClientsPanelActions.IsVisible = true; ClientsPanelHeader.IsVisible = true; ClientFilterBar.IsVisible = true; ClientFilterBar.IsEnabled = true;
        if (_serverClientDetail is { } id)
        {
            var client = ServerClients.FirstOrDefault(c => c.Id == id);
            if (client is not null) { RenderServerClientSheet(client); return; }
            _serverClientDetail = null;
        }
        var query = ClientFilterBox.Text?.Trim() ?? "";
        var rows = ServerClients.Where(c => (query.Length == 0 || (c.DisplayName + " " + c.ClientCode).Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
            (_clientKindFilter.Length == 0 || (_clientKindFilter == "Particulier" ? c.Kind == "Individual" : _clientKindFilter == "Professionnel" && c.Kind == "Professional")));
        rows = _clientSortColumn == 1 ? rows.OrderBy(c => c.ClientCode) : rows.OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase);
        if (!_clientSortAscending) rows = rows.Reverse();
        var clients = rows.ToArray();
        ClientsPanelSubtitle.Text = $"{clients.Length} client{(clients.Length == 1 ? "" : "s")} · atelier serveur";
        PagedRows.Render(ClientList, clients, client =>
        {
            var open = ServerAction("", () => OpenServerClientAsync(client.Id));
            Avalonia.Automation.AutomationProperties.SetName(open, "Ouvrir " + client.DisplayName);
            var edit = ServerAction("Modifier la fiche", () => EditServerClientAsync(client.Id), write: true);
            return CreateActionRow(ClientRowColumns, open, CreateOverflowActions(edit),
                CreateCompactTableCell(client.DisplayName, prominent: true), CreateCompactTableCell(client.ClientCode, accent: true),
                CreateCompactTableCell(client.Kind == "Individual" ? "Particulier" : "Professionnel"),
                CreateCompactTableCell(ServerProjects.Count(p => p.ClientId == client.Id) + " projets"));
        }, () => CreateSortableHeader(ClientRowColumns, ["CLIENT", "CODE", "FICHE", "PROJETS", "ACTIONS"], _clientSortColumn, _clientSortAscending, column =>
        { _clientSortAscending = _clientSortColumn != column || !_clientSortAscending; _clientSortColumn = column; RenderServerClients(); }),
        ("server-clients", _serverConnection?.Server, _serverConnection?.Account, query, _clientKindFilter, _clientSortColumn, _clientSortAscending));
    }

    private Task OpenServerClientAsync(Guid id)
    {
        _serverPage = "Clients"; ShowWorkspace("Clients"); _serverClientDetail = id;
        PageTitle.Text = "Clients"; RenderServerClients(); return Task.CompletedTask;
    }
    private void RenderServerClientSheet(ClientResponse client)
    {
        ClientsPanelTitle.Text = client.DisplayName; ClientsPanelSubtitle.Text = client.ClientCode + " · atelier serveur";
        ClientFilterBar.IsVisible = false;
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(ServerAction("Tous les clients", () => { _serverClientDetail = null; RenderServerClients(); return Task.CompletedTask; }));
        body.Children.Add(ServerText(client.DisplayName, true));
        body.Children.Add(ServerText(client.ClientCode + " · " + (client.Kind == "Individual" ? "Particulier" : "Professionnel")));
        body.Children.Add(ServerAction("Modifier la fiche", () => EditServerClientAsync(client.Id), true));
        body.Children.Add(ServerAction("Nouveau projet pour ce client", () => EditServerProjectAsync(null, client.Id), true));
        body.Children.Add(ServerText("Projets", true));
        foreach (var project in ServerProjects.Where(p => p.ClientId == client.Id)) body.Children.Add(ServerAction(project.ProjectName + " · " + project.Reference, () => OpenServerProjectAsync(project.Id)));
        body.Children.Add(ServerText("Fichiers et opérations de classement indisponibles en mode serveur."));
        ClientList.Children.Add(body);
    }

    private void RenderServerProjects()
    {
        if (ProjectList is null) return;
        ProjectList.Children.Clear(); ProjectList.Spacing = 0; _projectListMode = true;
        ProjectsPanelTitle.Text = "Tous les projets"; ProjectsPanelActions.IsVisible = true; ProjectsPanelHeader.IsVisible = true;
        ProjectFilterBar.IsVisible = true; ProjectFilterBar.IsEnabled = true;
        if (_serverProjectDetail is { } id)
        {
            var selected = ServerProjects.FirstOrDefault(p => p.Id == id);
            if (selected is not null) { RenderServerProjectSheet(selected); return; }
            _serverProjectDetail = null;
        }
        var query = ProjectFilterBox.Text?.Trim() ?? "";
        var rows = ServerProjects.Where(p => (_projectStatusFilter.Length == 0 || ServerStatus(p.Status) == _projectStatusFilter) &&
            (_projectPriorityFilter is null || p.Priority == _projectPriorityFilter.ToString()) &&
            (query.Length == 0 || (p.ProjectName + " " + p.Reference + " " + ServerClientName(p.ClientId) + " " + string.Join(" ", p.Tasks.Select(t => t.Title + " " + t.Notes))).Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        rows = _projectSortColumn switch { 1 => rows.OrderBy(p => ServerClientName(p.ClientId)), 2 => rows.OrderBy(p => p.Status), 3 => rows.OrderBy(p => p.DueDate), 4 => rows.OrderByDescending(p => Enum.Parse<ProjectPriority>(p.Priority)), _ => rows.OrderBy(p => p.ProjectName, StringComparer.CurrentCultureIgnoreCase) };
        if (!_projectSortAscending) rows = rows.Reverse();
        var projects = rows.ToArray(); ProjectsPanelSubtitle.Text = $"{projects.Length} projet{(projects.Length == 1 ? "" : "s")} · atelier serveur";
        PagedRows.Render(ProjectList, projects, project =>
        {
            var open = ServerAction("", () => OpenServerProjectAsync(project.Id));
            Avalonia.Automation.AutomationProperties.SetName(open, "Ouvrir " + project.ProjectName);
            var edit = ServerAction("Modifier la fiche", () => EditServerProjectAsync(project.Id), true);
            var heading = new StackPanel { Spacing = 4, Children = { CreateCompactTableCell(project.ProjectName, prominent: true), CreateCompactTableCell(project.Reference, accent: true) } };
            return CreateActionRow(ProjectRowColumns, open, CreateOverflowActions(edit), heading, CreateCompactTableCell(ServerClientName(project.ClientId)), CreateStatusBadge(ServerStatus(project.Status)), CreateCompactTableCell(project.DueDate?.ToString("dd/MM/yyyy") ?? "Sans échéance"));
        }, () => CreateSortableHeader(ProjectRowColumns, ["PROJET", "CLIENT", "STATUT", "ÉCHÉANCE", "ACTIONS"], _projectSortColumn, _projectSortAscending, column =>
        { _projectSortAscending = _projectSortColumn != column || !_projectSortAscending; _projectSortColumn = column; SaveWindowPreferences(); RenderServerProjects(); }),
        ("server-projects", _serverConnection?.Server, _serverConnection?.Account, query, _projectStatusFilter, _projectPriorityFilter, _projectSortColumn, _projectSortAscending));
    }

    private Task OpenServerProjectAsync(Guid id)
    {
        _serverPage = "Projets"; ShowWorkspace("Projets"); _serverProjectDetail = id;
        PageTitle.Text = "Projets"; RenderServerProjects(); return Task.CompletedTask;
    }
    private void RenderServerProjectSheet(ProjectResponse project)
    {
        ProjectsPanelTitle.Text = project.ProjectName; ProjectsPanelSubtitle.Text = project.Reference + " · " + ServerClientName(project.ClientId);
        ProjectFilterBar.IsVisible = false;
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(ServerAction("Tous les projets", () => { _serverProjectDetail = null; RenderServerProjects(); return Task.CompletedTask; }));
        body.Children.Add(ServerText(project.ProjectName, true)); body.Children.Add(CreateStatusBadge(ServerStatus(project.Status)));
        body.Children.Add(ServerText((project.DueDate?.ToString("dd/MM/yyyy") ?? "Sans échéance") + " · " + ProjectPriorityVisual.Text(Enum.TryParse<ProjectPriority>(project.Priority, out var priority) ? priority : ProjectPriority.None)));
        body.Children.Add(ServerAction("Modifier la fiche", () => EditServerProjectAsync(project.Id), true));
        body.Children.Add(ServerText("Tâches", true));
        foreach (var task in project.Tasks)
        {
            var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
            var check = new CheckBox { Content = task.Title, IsChecked = task.IsCompleted, IsEnabled = _serverConnection?.CanWrite == true };
            var origin = _serverConnection; var generation = _serverGeneration;
            check.IsCheckedChanged += async (_, _) =>
            {
                if (origin is null || !ServerContextCurrent(origin, generation)) return;
                await RunServerAsync(async connection =>
                { var command = new CompleteTaskCommand(Guid.NewGuid(), project.UpdatedAt, check.IsChecked == true); await connection.ExecuteAsync(command.CommandId, c => c.CompleteTaskAsync(project.Id, task.Id, command)); }, true);
            };
            row.Children.Add(check);
            row.Children.Add(ServerAction("Modifier", () => EditServerTaskAsync(project, task), true));
            row.Children.Add(ServerAction("Monter", () => MoveServerTaskAsync(project, task, -1), true));
            row.Children.Add(ServerAction("Descendre", () => MoveServerTaskAsync(project, task, 1), true));
            row.Children.Add(ServerAction("Retirer", async () =>
            {
                if (!await ConfirmRemovalWindow.AskAsync(this, task.Title, "Cette tâche sera retirée du projet sur le serveur.")) return;
                await RunServerAsync(async connection => { var command = new RemoveTaskCommand(Guid.NewGuid(), project.UpdatedAt, true); await connection.ExecuteAsync(command.CommandId, c => c.RemoveTaskAsync(project.Id, task.Id, command)); }, true);
            }, true));
            body.Children.Add(row);
            if (!string.IsNullOrWhiteSpace(task.Notes)) body.Children.Add(ServerText(task.Notes));
        }
        body.Children.Add(ServerAction("Ajouter une tâche", () => EditServerTaskAsync(project, null), true, "ServerAddTask"));
        body.Children.Add(ServerText("Fichiers, devis, factures et opérations de classement indisponibles en mode serveur."));
        ProjectList.Children.Add(body);
    }
    private Task MoveServerTaskAsync(ProjectResponse project, ProjectTask task, int direction) => RunServerAsync(async connection =>
    { var command = new MoveTaskCommand(Guid.NewGuid(), project.UpdatedAt, direction); await connection.ExecuteAsync(command.CommandId, c => c.MoveTaskAsync(project.Id, task.Id, command)); }, true);
}
