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
    private static TextBlock ServerText(string text, bool title = false) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = title ? Ui.Text.Section : Ui.Text.Body, FontWeight = title ? FontWeight.SemiBold : FontWeight.Normal };

    /// <summary>A server client sheet when one is open, otherwise the full PC client list (server-only clients appended).</summary>
    private void RenderServerClients()
    {
        if (ClientList is null) return;
        if (_serverClientDetail is { } id && ServerClients.FirstOrDefault(c => c.Id == id) is { } client)
        {
            ClientList.Children.Clear(); ClientList.Spacing = 0; _clientListMode = false;
            RenderServerClientSheet(client); return;
        }
        _serverClientDetail = null;
        if (_clientListMode) RenderClients();
    }

    /// <summary>Clients created on the server (for example from the other PC) that have no folder on this PC yet.</summary>
    private void AppendServerOnlyClients()
    {
        if (!IsServerWorkspace || _serverConnection?.Copy is null) return;
        var query = ClientFilterBox.Text?.Trim() ?? "";
        var local = _allClients.Select(c => c.ClientCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clients = ServerClients.Where(c => !local.Contains(c.ClientCode)
            && (query.Length == 0 || (c.DisplayName + " " + c.ClientCode).Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (clients.Length == 0) return;
        ClientList.Children.Add(ServerOnlyHeading(clients.Length == 1 ? "1 client sur le serveur, pas encore sur ce PC" : $"{clients.Length} clients sur le serveur, pas encore sur ce PC"));
        foreach (var client in clients)
        {
            var open = ServerAction("", () => OpenServerClientAsync(client.Id));
            Avalonia.Automation.AutomationProperties.SetName(open, "Ouvrir " + client.DisplayName);
            var edit = ServerAction("Modifier la fiche", () => EditServerClientAsync(client.Id), write: true);
            ClientList.Children.Add(CreateActionRow(ClientRowColumns, open, CreateOverflowActions(edit),
                CreateCompactTableCell(client.DisplayName, prominent: true), CreateCompactTableCell(client.ClientCode, accent: true),
                CreateCompactTableCell(client.Kind == "Individual" ? "Particulier" : "Professionnel"),
                CreateCompactTableCell(ServerProjects.Count(p => p.ClientId == client.Id) + " projets")));
        }
    }

    private static TextBlock ServerOnlyHeading(string text)
        => new() { Text = text, Margin = new Avalonia.Thickness(0, 16, 0, 8), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };

    private Task OpenServerClientAsync(Guid id)
    {
        ShowWorkspace("Clients"); _serverClientDetail = id;
        PageTitle.Text = "Clients"; RenderServerClients(); return Task.CompletedTask;
    }
    private void RenderServerClientSheet(ClientResponse client)
    {
        ClientsPanelTitle.Text = client.DisplayName; ClientsPanelSubtitle.Text = client.ClientCode + " · atelier serveur";
        ClientFilterBar.IsVisible = false;
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(ServerAction("Tous les clients", () => { _serverClientDetail = null; return LoadClientsAsync(); }));
        body.Children.Add(ServerText(client.DisplayName, true));
        body.Children.Add(ServerText(client.ClientCode + " · " + (client.Kind == "Individual" ? "Particulier" : "Professionnel")));
        body.Children.Add(ServerAction("Modifier la fiche", () => EditServerClientAsync(client.Id), true));
        body.Children.Add(ServerAction("Nouveau projet pour ce client", () => EditServerProjectAsync(null, client.Id), true));
        body.Children.Add(ServerText("Projets", true));
        foreach (var project in ServerProjects.Where(p => p.ClientId == client.Id)) body.Children.Add(ServerAction(project.ProjectName + " · " + project.Reference, () => OpenServerProjectAsync(project.Id)));
        body.Children.Add(ServerText("Ce client n’a pas encore de dossier sur ce PC : ses fichiers apparaîtront après la synchronisation."));
        ClientList.Children.Add(body);
    }

    /// <summary>A server project sheet when one is open, otherwise the full PC project list (server-only projects appended).</summary>
    private void RenderServerProjects()
    {
        if (ProjectList is null) return;
        if (_serverProjectDetail is { } id && ServerProjects.FirstOrDefault(p => p.Id == id) is { } selected)
        {
            ProjectList.Children.Clear(); ProjectList.Spacing = 0; _projectListMode = false;
            RenderServerProjectSheet(selected); return;
        }
        _serverProjectDetail = null;
        if (_projectListMode) RenderProjects();
    }

    /// <summary>Projects created on the server (for example from the other PC) that have no folder on this PC yet.</summary>
    private void AppendServerOnlyProjects()
    {
        if (!IsServerWorkspace || _serverConnection?.Copy is null || _projectCalendarView || _projectStatusFilter == "ARCHIVES") return;
        var query = ProjectFilterBox.Text?.Trim() ?? "";
        var local = _allProjects.Select(p => p.Reference).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projects = ServerProjects.Where(p => !local.Contains(p.Reference)
            && (_projectStatusFilter.Length == 0 || ServerStatus(p.Status) == _projectStatusFilter)
            && (_projectPriorityFilter is null || p.Priority == _projectPriorityFilter.ToString())
            && (query.Length == 0 || (p.ProjectName + " " + p.Reference + " " + ServerClientName(p.ClientId)).Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(p => p.ProjectName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (projects.Length == 0) return;
        ProjectList.Children.Add(ServerOnlyHeading(projects.Length == 1 ? "1 projet sur le serveur, pas encore sur ce PC" : $"{projects.Length} projets sur le serveur, pas encore sur ce PC"));
        foreach (var project in projects)
        {
            var open = ServerAction("", () => OpenServerProjectAsync(project.Id));
            Avalonia.Automation.AutomationProperties.SetName(open, "Ouvrir " + project.ProjectName);
            var edit = ServerAction("Modifier la fiche", () => EditServerProjectAsync(project.Id), true);
            var heading = new StackPanel { Spacing = 4, Children = { CreateCompactTableCell(project.ProjectName, prominent: true), CreateCompactTableCell(project.Reference, accent: true) } };
            ProjectList.Children.Add(CreateActionRow(ProjectRowColumns, open, CreateOverflowActions(edit), heading, CreateCompactTableCell(ServerClientName(project.ClientId)), CreateStatusBadge(ServerStatus(project.Status)), CreateCompactTableCell(project.DueDate?.ToString("dd/MM/yyyy") ?? "Sans échéance"), CreateCreatedCell(null)));
        }
    }

    private Task OpenServerProjectAsync(Guid id)
    {
        ShowWorkspace("Projets"); _serverProjectDetail = id;
        PageTitle.Text = "Projets"; RenderServerProjects(); return Task.CompletedTask;
    }
    private void RenderServerProjectSheet(ProjectResponse project)
    {
        ProjectsPanelTitle.Text = project.ProjectName; ProjectsPanelSubtitle.Text = project.Reference + " · " + ServerClientName(project.ClientId);
        ProjectFilterBar.IsVisible = false;
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(ServerAction("Tous les projets", () => { _serverProjectDetail = null; return LoadProjectsAsync(); }));
        body.Children.Add(ServerText(project.ProjectName, true)); body.Children.Add(CreateStatusBadge(ServerStatus(project.Status)));
        body.Children.Add(ServerText((project.DueDate?.ToString("dd/MM/yyyy") ?? "Sans échéance") + " · " + ProjectPriorityVisual.Text(Enum.TryParse<ProjectPriority>(project.Priority, out var priority) ? priority : ProjectPriority.None)));
        body.Children.Add(ServerAction("Modifier la fiche", () => EditServerProjectAsync(project.Id), true));
        body.Children.Add(ServerText("Tâches", true));
        foreach (var task in project.Tasks)
        {
            var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
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
        body.Children.Add(ServerText("Ce projet n’a pas encore de dossier sur ce PC : ses fichiers apparaîtront après la synchronisation."));
        ProjectList.Children.Add(body);
    }
    private Task MoveServerTaskAsync(ProjectResponse project, ProjectTask task, int direction) => RunServerAsync(async connection =>
    { var command = new MoveTaskCommand(Guid.NewGuid(), project.UpdatedAt, direction); await connection.ExecuteAsync(command.CommandId, c => c.MoveTaskAsync(project.Id, task.Id, command)); }, true);
}
