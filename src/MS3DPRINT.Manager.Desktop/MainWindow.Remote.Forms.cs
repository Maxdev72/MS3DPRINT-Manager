using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private sealed class ServerFormWindow : DialogWindow
    {
        public ServerFormWindow(string title, string description, IEnumerable<Control> fields, Func<Task<bool>> save, Func<Task<bool>> recover)
        {
            Width = 560;
            var error = FormKit.ErrorText();
            var submit = new Button { Name = "ServerSave", Content = "Enregistrer sur le serveur" };
            submit.Click += async (_, _) =>
            {
                submit.IsEnabled = false;
                try
                {
                    if (await save()) Close(true);
                    else { error.Text = "Enregistrement non confirmé. Votre saisie est conservée ; consultez l’état de connexion et le reçu avant de réessayer."; error.IsVisible = true; }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                { error.Text = ex.Message; error.IsVisible = true; }
                finally { submit.IsEnabled = true; }
            };
            var recovery = new Button { Name = "ServerRecover", Content = "Vérifier le reçu et actualiser la version" };
            recovery.Click += async (_, _) =>
            {
                recovery.IsEnabled = submit.IsEnabled = false;
                try
                {
                    if (await recover()) { Close(true); return; }
                    error.Text = "Vérification effectuée. La saisie est conservée. Si le reçu reste incertain, aucune nouvelle commande ne sera envoyée.";
                    error.IsVisible = true;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                { error.Text = ex.Message; error.IsVisible = true; }
                finally { recovery.IsEnabled = submit.IsEnabled = true; }
            };
            Layout(title, description, [.. fields, error, recovery], submit);
        }
    }
    private async Task<bool> RecoverServerFormAsync(RemoteWorkspaceConnection connection, int generation, Func<Task> rebase)
    {
        if (!ServerContextCurrent(connection, generation)) return false;
        var saved = false;
        await RunServerAsync(async current =>
        {
            if (current.Copy?.PendingCommand is { } command)
            {
                var receipt = await current.ReadAsync(api => api.GetCommandReceiptAsync(command));
                if (receipt.CommandId != command || receipt.State != "Completed" || receipt.StatusCode is not (>= 200 and < 500)) return;
                saved = receipt.StatusCode is >= 200 and < 300;
                await current.CheckReceiptAsync();
                if (saved) return;
            }
            await current.RefreshAsync();
            await rebase();
        });
        return saved && ServerContextCurrent(connection, generation);
    }

    private static TextBox ServerInput(string name, string? value = null, bool multiline = false)
        => new() { Name = name, Text = value ?? "", AcceptsReturn = multiline, MinHeight = multiline ? 90 : 46, TextWrapping = multiline ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap };
    private static ComboBox ServerEnum<T>(T selected) where T : struct, Enum
        => new() { ItemsSource = Enum.GetValues<T>(), SelectedItem = selected, ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<T>((value, _) => new TextBlock { Text = value switch { ClientKind.Professional => "Professionnel", ClientKind.Individual => "Particulier", ProjectStatus.Quote => "À faire", ProjectStatus.InProgress => "En cours", ProjectStatus.Completed => "Terminé", ProjectPriority.High => "Haute", ProjectPriority.Medium => "Moyenne", ProjectPriority.Low => "Basse", _ => "Aucune" } }) };

    private async Task EditServerClientAsync(Guid? id)
    {
        if (_serverConnection?.CanWrite != true || _serverFormOpen || _panel is not null) return;
        var connection = _serverConnection; var generation = _serverGeneration;
        ClientDetailResponse? detail = null;
        if (id is { } clientId)
        {
            await RunServerAsync(async c => detail = await c.ReadAsync(client => client.GetClientAsync(clientId)));
            if (detail is null || !ServerContextCurrent(connection, generation)) return;
        }
        var kind = ServerEnum(detail?.Kind ?? ClientKind.Professional);
        var code = ServerInput("ServerClientCode", detail?.ClientCode); code.IsEnabled = id is null;
        var company = ServerInput("ServerCompany", detail?.CompanyName);
        var first = ServerInput("ServerFirstName", detail?.FirstName);
        var last = ServerInput("ServerLastName", detail?.LastName);
        var address = ServerInput("ServerClientAddress", detail?.Address, true);
        var notes = ServerInput("ServerClientNotes", detail?.Notes, true);
        var contactFirst = ServerInput("ServerContactFirstName", detail?.PrimaryContact.FirstName);
        var contactLast = ServerInput("ServerContactLastName", detail?.PrimaryContact.LastName);
        var contactRole = ServerInput("ServerContactRole", detail?.PrimaryContact.Role);
        var phone = ServerInput("ServerPhone", detail?.PrimaryContact.Phone);
        var email = ServerInput("ServerEmail", detail?.PrimaryContact.Email);
        var mobile = ServerInput("ServerMobile", detail?.PrimaryContact.Mobile);
        var siret = ServerInput("ServerSiret", detail?.Siret); siret.IsReadOnly = true;
        var form = new ServerFormWindow(id is null ? "Nouveau client" : "Modifier le client", "Les modifications concernent uniquement l’atelier serveur. Le SIRET et le changement de code ne sont pas modifiables par cette API.",
        [FormKit.Field("Profil", kind), FormKit.Field("Code client", code), FormKit.Field("Entreprise", company), FormKit.Field("Prénom du particulier", first), FormKit.Field("Nom du particulier", last), FormKit.Field("Adresse", address), FormKit.Field("SIRET · consultation seule", siret), FormKit.Field("Prénom du contact", contactFirst), FormKit.Field("Nom du contact", contactLast), FormKit.Field("Fonction du contact", contactRole), FormKit.Field("Téléphone", phone), FormKit.Field("Courriel", email), FormKit.Field("Mobile", mobile), FormKit.Field("Notes", notes)], async () =>
        {
            if (!ServerContextCurrent(connection, generation)) return false;
            var selected = (ClientKind)kind.SelectedItem!;
            if (id is null && string.IsNullOrWhiteSpace(code.Text)) throw new ArgumentException("Saisissez un code client.");
            if (selected == ClientKind.Professional && string.IsNullOrWhiteSpace(company.Text) || selected == ClientKind.Individual && string.IsNullOrWhiteSpace(last.Text)) throw new ArgumentException("Saisissez le nom du client.");
            var contact = new PrimaryContact(contactFirst.Text, contactLast.Text, contactRole.Text, phone.Text, email.Text, mobile.Text);
            var saved = false;
            await RunServerAsync(async c =>
            {
                if (detail is null)
                {
                    var command = new CreateClientCommand(Guid.NewGuid(), selected, code.Text!.Trim(), company.Text, first.Text, last.Text, address.Text, notes.Text, contact);
                    await c.ExecuteAsync(command.CommandId, api => api.CreateClientAsync(command));
                }
                else
                {
                    var command = new UpdateClientCommand(Guid.NewGuid(), detail.UpdatedAt, selected, company.Text, first.Text, last.Text, address.Text, notes.Text, contact);
                    await c.ExecuteAsync(command.CommandId, api => api.UpdateClientAsync(detail.Id, command));
                }
                saved = true;
            }, true);
            return saved && ServerContextCurrent(connection, generation);
        }, async () => await RecoverServerFormAsync(connection, generation, async () =>
        {
            if (id is { } currentId) detail = await connection.ReadAsync(api => api.GetClientAsync(currentId));
        }));
        _serverFormOpen = true;
        try { await form.ShowDialog<bool>(this); }
        finally { _serverFormOpen = false; }
    }

    private async Task EditServerProjectAsync(Guid? id, Guid? selectedClient = null)
    {
        if (_serverConnection?.CanWrite != true || _serverFormOpen || _panel is not null) return;
        var connection = _serverConnection; var generation = _serverGeneration;
        ProjectDetailResponse? detail = null;
        if (id is { } projectId)
        {
            await RunServerAsync(async c => detail = await c.ReadAsync(api => api.GetProjectAsync(projectId)));
            if (detail is null || !ServerContextCurrent(connection, generation)) return;
        }
        var clients = new ComboBox { Name = "ServerProjectClient", ItemsSource = ServerClients.ToArray(), ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ClientResponse>((client, _) => new TextBlock { Text = client?.DisplayName + " · " + client?.ClientCode }) };
        clients.SelectedItem = ServerClients.FirstOrDefault(c => c.Id == (detail?.ClientId ?? selectedClient)) ?? ServerClients.FirstOrDefault();
        clients.IsEnabled = id is null && selectedClient is null;
        var name = ServerInput("ServerProjectName", detail?.ProjectName); name.IsReadOnly = detail is not null;
        var year = ServerInput("ServerProjectYear", DateTime.Today.Year.ToString()); year.IsEnabled = detail is null;
        var status = ServerEnum(detail?.Status ?? ProjectStatus.Quote); status.IsEnabled = detail is not null;
        var priority = ServerEnum(detail?.Priority ?? ProjectPriority.None);
        var due = new CalendarDatePicker { Name = "ServerProjectDueDate", SelectedDate = detail?.DueDate?.ToDateTime(TimeOnly.MinValue) };
        var description = ServerInput("ServerProjectDescription", detail?.Description, true);
        var notes = ServerInput("ServerProjectNotes", detail?.Notes, true); notes.IsEnabled = detail is not null;
        var form = new ServerFormWindow(detail is null ? "Nouveau projet" : "Modifier le projet", detail is null ? "Projet de l’atelier serveur. Le nouveau projet commence au statut À faire ; les notes seront disponibles après sa création." : "Projet de l’atelier serveur. Le nom, le client et l’année d’un projet existant ne sont pas modifiables par cette API.",
        [FormKit.Field("Client", clients), FormKit.Field("Nom du projet", name), FormKit.Field("Année", year), FormKit.Field("Statut", status), FormKit.Field("Priorité", priority), FormKit.Field("Échéance", due), FormKit.Field("Description", description), FormKit.Field("Notes", notes)], async () =>
        {
            if (!ServerContextCurrent(connection, generation)) return false;
            if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("Saisissez le nom du projet.");
            var deadline = due.SelectedDate is { } date ? DateOnly.FromDateTime(date) : (DateOnly?)null;
            var saved = false;
            await RunServerAsync(async c =>
            {
                if (detail is null)
                {
                    if (clients.SelectedItem is not ClientResponse client) throw new ArgumentException("Choisissez un client.");
                    if (!int.TryParse(year.Text, out var projectYear) || projectYear < 2000 || projectYear > 2100) throw new ArgumentException("Saisissez une année comprise entre 2000 et 2100.");
                    var command = new CreateProjectCommand(Guid.NewGuid(), client.Id, name.Text!.Trim(), projectYear, deadline, description.Text, (ProjectPriority)priority.SelectedItem!);
                    await c.ExecuteAsync(command.CommandId, api => api.CreateProjectAsync(command));
                }
                else
                {
                    var command = new UpdateProjectCommand(Guid.NewGuid(), detail.UpdatedAt, (ProjectStatus)status.SelectedItem!, deadline, description.Text, notes.Text, (ProjectPriority)priority.SelectedItem!);
                    await c.ExecuteAsync(command.CommandId, api => api.UpdateProjectAsync(detail.Id, command));
                }
                saved = true;
            }, true);
            return saved && ServerContextCurrent(connection, generation);
        }, async () => await RecoverServerFormAsync(connection, generation, async () =>
        {
            if (id is { } currentId) detail = await connection.ReadAsync(api => api.GetProjectAsync(currentId));
        }));
        _serverFormOpen = true;
        try { await form.ShowDialog<bool>(this); }
        finally { _serverFormOpen = false; }
    }

    private async Task EditServerTaskAsync(ProjectResponse project, ProjectTask? task)
    {
        if (_serverConnection?.CanWrite != true || _serverFormOpen || _panel is not null) return;
        var connection = _serverConnection; var generation = _serverGeneration;
        var title = ServerInput("ServerTaskTitle", task?.Title);
        var notes = ServerInput("ServerTaskNotes", task?.Notes, true);
        var form = new ServerFormWindow(task is null ? "Ajouter une tâche" : "Modifier la tâche", project.ProjectName + " · " + project.Reference,
        [FormKit.Field("Tâche", title), FormKit.Field("Notes", notes)], async () =>
        {
            if (!ServerContextCurrent(connection, generation)) return false;
            if (string.IsNullOrWhiteSpace(title.Text)) throw new ArgumentException("Saisissez le texte de la tâche.");
            var saved = false;
            await RunServerAsync(async c =>
            {
                if (task is null)
                {
                    var command = new AddTaskCommand(Guid.NewGuid(), project.UpdatedAt, title.Text!.Trim(), notes.Text);
                    await c.ExecuteAsync(command.CommandId, api => api.AddTaskAsync(project.Id, command));
                }
                else
                {
                    var command = new EditTaskCommand(Guid.NewGuid(), project.UpdatedAt, title.Text!.Trim(), notes.Text);
                    await c.ExecuteAsync(command.CommandId, api => api.EditTaskAsync(project.Id, task.Id, command));
                }
                saved = true;
            }, true);
            return saved && ServerContextCurrent(connection, generation);
        }, async () => await RecoverServerFormAsync(connection, generation, () =>
        {
            project = ServerProjects.FirstOrDefault(p => p.Id == project.Id) ?? project;
            return Task.CompletedTask;
        }));
        _serverFormOpen = true;
        try { await form.ShowDialog<bool>(this); }
        finally { _serverFormOpen = false; }
    }
}
