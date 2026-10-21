using MS3DPRINT.Manager.Core.Workspace;
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

/// <summary>Clients page and client sheet.</summary>
public partial class MainWindow
{
    private async void RefreshClients_Click(object? sender, RoutedEventArgs e) => await LoadClientsAsync();

    private async void CreateClient_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        var usedCodes = await Task.Run(() => new WorkspaceClientCreator().UsedCodes(root));
        var dialog = new CreateClientWindow(usedCodes: usedCodes);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var (individual, firstName, lastName, clientName, siret, address) = (dialog.IsIndividual, dialog.FirstName, dialog.LastName, dialog.ClientName, dialog.Siret, dialog.Address);
            var (email, mobile) = (dialog.Email, dialog.Mobile);
            BeginBusy();
            ClientProfile created;
            try
            {
                created = await Task.Run(() => individual
                    ? new WorkspaceClientCreator().CreateIndividual(root, firstName, lastName, null, address, email, mobile)
                    : new WorkspaceClientCreator().CreateProfessional(root, clientName, null, siret, address, email, mobile));
            }
            finally { EndBusy(); }
            PageTitle.Text = "Clients";
            Notify($"Client créé (code {created.ClientCode}).");
            ShowWorkspace("Clients");
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
        {
            Notify(exception.Message, error: true);
        }
    }

    private IReadOnlyList<ClientSummary> _allClients = [];

    private string _clientKindFilter = string.Empty;

    private async Task LoadClientsAsync()
    {
        var workspaceRoot = WorkspaceRoot;
        ClientList.Children.Clear();
        ClientList.Spacing = 0;
        _clientListMode = true;
        _currentClientDetail = null;
        ClientsPanelTitle.Text = "Tous les clients";
        ClientsPanelActions.IsVisible = true;
        ClientsPanelHeader.IsVisible = true;
        ClientFilterBar.IsVisible = true;
        try
        {
            _allClients = await Task.Run(() => new WorkspaceClientReader().Load(workspaceRoot));
            RenderClients();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ClientList.Children.Add(CreateEmptyState("IconAlert", "Impossible de lire les clients", exception.Message));
        }
    }

    private void RenderClients()
    {
        if (!_clientListMode) return;
        ClientList.Children.Clear();
        var text = ClientFilterBox.Text?.Trim() ?? string.Empty;
        var clients = _allClients
            .Where(client => _clientKindFilter.Length == 0 || client.ProfileLabel == _clientKindFilter)
            .Where(client => text.Length == 0 || $"{client.DisplayName} {client.ClientCode}".Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
        ClientsPanelSubtitle.Text = clients.Length == _allClients.Count
            ? (_allClients.Count switch { 0 => "Aucun client", 1 => "1 client", var count => $"{count} clients" })
            : $"{clients.Length} sur {_allClients.Count} clients";

        if (_allClients.Count == 0)
        {
            ClientFilterBar.IsVisible = false;
            ClientList.Children.Add(CreateEmptyState("IconClients", "Aucun client pour l’instant", "Ajoutez un client, particulier ou professionnel, pour y rattacher ses projets.", "Nouveau client", CreateClient_Click));
            return;
        }
        if (clients.Length == 0)
        {
            ClientList.Children.Add(CreateEmptyState("IconSearch", "Aucun résultat", "Aucun client ne correspond à ce filtre."));
            return;
        }

        clients = SortClients(clients);
        ClientList.Children.Add(CreateSortableHeader(ClientRowColumns, ["CLIENT", "CODE", "FICHE", "PROJETS", "ACTIONS"], _clientSortColumn, _clientSortAscending, column =>
        {
            _clientSortAscending = _clientSortColumn != column || !_clientSortAscending;
            _clientSortColumn = column;
            RenderClients();
        }));
        foreach (var client in clients)
        {
            var open = new Button { Tag = client, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            open.Click += ClientEntry_Click;
            var folder = new Button { Content = Icons.Label("IconOpen", "Ouvrir le dossier"), Tag = client.ClientPath }; folder.Click += OpenFolder_Click;
            var edit = new Button { Content = Icons.Label("IconEdit", "Modifier la fiche"), Tag = client }; edit.Click += EditClient_Click;
            var code = new Button { Content = Icons.Label("IconEdit", "Changer le code…"), Tag = client }; code.Click += ChangeClientCode_Click;
            var rename = new Button { Content = Icons.Label("IconRename", "Renommer"), Tag = client }; rename.Click += RenameClient_Click;
            var move = new Button { Content = Icons.Label("IconMove", "Déplacer"), Tag = client }; move.Click += MoveClient_Click;
            var trash = new Button { Content = Icons.Label("IconTrash", "Corbeille"), Tag = client }; trash.Click += TrashClient_Click;
            var kindVariant = client.IsProfileMissing ? "warning" : client.Kind == ClientKind.Individual ? "neutral" : "info";
            ClientList.Children.Add(CreateActionRow(ClientRowColumns, open, CreateOverflowActions(folder, edit, code, rename, move, trash),
                CreateCompactTableCell(client.Title(), prominent: true),
                CreateCompactTableCell(string.IsNullOrWhiteSpace(client.ClientCode) ? "—" : client.ClientCode, accent: true),
                new Border { Classes = { "badge", kindVariant }, Child = new TextBlock { Text = client.ProfileLabel } },
                CreateCompactTableCell(client.ProjectCount switch { 0 => "Aucun projet", 1 => "1 projet", var count => $"{count} projets" })));
        }
    }

    private void ClientFilterText_Changed(object? sender, TextChangedEventArgs e)
    {
        if (ClientList is not null) RenderClients();
    }

    private void ClientFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        _clientKindFilter = kind;
        SelectChip(ClientStatusFilters, kind);
        RenderClients();
    }

    private async void ClientEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MS3DPRINT.Manager.Core.Clients.ClientSummary client })
            await LoadClientDetailAsync(client);
    }

    private async Task LoadClientDetailAsync(ClientSummary client)
    {
        ClientList.Children.Clear();
        ClientList.Spacing = 8;
        _clientListMode = false;
        _currentClientDetail = client;
        ClientsPanelTitle.Text = "Fiche client";
        ClientsPanelSubtitle.Text = client.Title();
        ClientsPanelActions.IsVisible = false;
        ClientsPanelHeader.IsVisible = false;
        ClientFilterBar.IsVisible = false;
        var back = new Button { Content = Icons.Label("IconArrowLeft", "Retour aux clients"), Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
        back.Click += async (_, _) =>
        {
            PageTitle.Text = "Clients";
            PageSubtitle.Text = PageSubtitles["Clients"];
            await LoadClientsAsync();
        };
        ClientList.Children.Add(back);
        SetBreadcrumb(("Clients", GoToClientsAsync), (client.Title(), null));

        var card = new StackPanel { Spacing = 8 };
        card.Children.Add(new TextBlock { Text = client.Title(), FontSize = 18, FontWeight = FontWeight.SemiBold });
        card.Children.Add(new TextBlock { Text = $"{client.ProfileLabel} · {client.ClientCode} · {client.ProjectCount} projet(s)", Classes = { "muted" } });
        if (client.Profile?.PrimaryContact is { } contact)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(contact.Mobile)) lines.Add("Portable : " + contact.Mobile);
            if (!string.IsNullOrWhiteSpace(contact.Phone)) lines.Add("Téléphone : " + contact.Phone);
            if (!string.IsNullOrWhiteSpace(contact.Email)) lines.Add("E-mail : " + contact.Email);
            if (lines.Count > 0) card.Children.Add(new TextBlock { Text = string.Join("  ·  ", lines), TextWrapping = TextWrapping.Wrap });
        }
        if (!string.IsNullOrWhiteSpace(client.Profile?.Address)) card.Children.Add(new TextBlock { Text = client.Profile.Address, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(client.Profile?.Notes)) card.Children.Add(new TextBlock { Text = client.Profile.Notes, TextWrapping = TextWrapping.Wrap });
        var edit = new Button { Content = Icons.Label("IconEdit", "Modifier la fiche"), Tag = client };
        edit.Click += EditClient_Click;
        var changeCode = new Button { Content = Icons.Label("IconRename", "Changer le code"), Tag = client };
        changeCode.Click += ChangeClientCode_Click;
        var newProject = new Button { Content = "+  Nouveau projet", Tag = client, Classes = { "primary-action" } };
        newProject.Click += CreateProjectForClient_Click;
        card.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { newProject, edit, changeCode } });
        ClientList.Children.Add(new Border { Classes = { "card-raised" }, Padding = new Avalonia.Thickness(18), Child = card });
        try
        {
            var root = WorkspaceRoot;
            var projects = await Task.Run(() => new WorkspaceProjectReader().Load(root)
                .Where(project => string.Equals(project.ClientPath, client.ClientPath, StringComparison.OrdinalIgnoreCase)).ToArray());
            if (projects.Length == 0)
            {
                ClientList.Children.Add(CreateEmptyState("IconProjects", "Aucun projet pour ce client", "Créez son premier projet avec le bouton « Nouveau projet ».", "Nouveau projet", CreateProjectForClient_Click, client));
                return;
            }
            ClientList.Children.Add(CreateCompactTableHeader(ActionRowColumns, "PROJET", "STATUT", "ÉCHÉANCE", ""));
            foreach (var project in projects)
            {
                var open = new Button { Tag = project, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                open.Click += ProjectEntry_Click;
                ClientList.Children.Add(CreateActionRow(open, null,
                    CreateCompactTableCell($"{project.Reference} · {project.Title()}", prominent: true),
                    CreateProjectStatusCell(project),
                    CreateCompactTableCell(project.Profile?.DueDate is { } due ? $"{due:dd/MM/yyyy}" : "—")));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Notify("Impossible de lire les projets de ce client.", error: true);
        }
    }

    /// <summary>Asks for the missing fiche of a historic client and writes it; null when cancelled or failed.</summary>
    private async Task<ClientProfile?> CompleteClientProfileAsync(ClientSummary client)
    {
        var completion = new CreateClientWindow(client);
        if (!await completion.ShowDialog<bool>(this)) return null;
        try
        {
            var root = WorkspaceRoot;
            var (individual, lastName, clientName, code) = (completion.IsIndividual, completion.LastName, completion.ClientName, completion.Code);
            var (firstName, siret, address) = (completion.FirstName, completion.Siret, completion.Address);
            var (completionEmail, completionMobile) = (completion.Email, completion.Mobile);
            var completedProfile = await Task.Run(() => new WorkspaceClientCreator().CompleteExisting(root, client.ClientPath,
                individual ? ClientKind.Individual : ClientKind.Professional,
                individual ? lastName : clientName, code, firstName, lastName, siret, address, completionEmail, completionMobile));
            Notify("Fiche client historique complétée.");
            return completedProfile;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); return null; }
    }

    private async void EditClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        if (client.Profile is null)
        {
            if (await CompleteClientProfileAsync(client) is { } completedProfile)
            {
                if (_clientListMode) await LoadClientsAsync();
                else await LoadClientDetailAsync(client with { Profile = completedProfile });
            }
            return;
        }
        var profile = client.Profile;
        var dialog = new EditClientWindow(profile);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var (address, notes, contactFirst, contactLast, contactRole) = (dialog.Address, dialog.Notes, dialog.ContactFirstName, dialog.ContactLastName, dialog.ContactRole);
            var (contactPhone, contactEmail, companyName, siret, firstName, lastName) = (dialog.ContactPhone, dialog.ContactEmail, dialog.CompanyName, dialog.Siret, dialog.IdentityFirstName, dialog.IdentityLastName);
            var contactMobile = dialog.ContactMobile;
            var updated = await Task.Run(() => new WorkspaceProfileEditor().UpdateClient(root, profile, address, notes,
                contactFirst, contactLast, contactRole, contactPhone, contactEmail,
                companyName, siret, firstName, lastName, contactMobile));
            Notify("Fiche client enregistrée.");
            if (_clientListMode) await LoadClientsAsync();
            else await LoadClientDetailAsync(client with { Profile = updated });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Notify(exception.Message, error: true);
        }
    }

    /// <summary>Changes the client code after showing, as it is typed, which project folders and fiches will follow.</summary>
    private async void ChangeClientCode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        if (client.Profile is not { } profile) { Notify("Complétez d’abord la fiche de ce client historique.", error: true); return; }
        var root = WorkspaceRoot;
        IReadOnlyList<ClientSummary> all;
        try { all = await Task.Run(() => new WorkspaceClientReader().Load(root)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); return; }

        var name = profile.Kind == ClientKind.Individual ? string.Join(" ", new[] { profile.FirstName, profile.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))) : profile.CompanyName ?? profile.FolderName;
        var othersCodes = all.Where(other => !string.Equals(other.ClientPath, client.ClientPath, StringComparison.OrdinalIgnoreCase)).Select(other => other.ClientCode);
        var changer = new ClientCodeChanger(root);
        var dialog = new ChangeClientCodeWindow(DisplayNames.Format(name), client.ClientCode, MS3DPRINT.Manager.Core.Naming.ClientCodeSuggester.SuggestUnique(name, othersCodes),
            code => changer.Plan(client, code, all));
        if (!await dialog.ShowDialog<bool>(this) || dialog.Plan is not { } plan) return;
        BeginBusy();
        try
        {
            await Task.Run(() => changer.Apply(plan));
            Notify($"Code client changé : {plan.OldCode} → {plan.NewCode}.");
            var updated = client with { ClientCode = plan.NewCode, Profile = profile with { ClientCode = plan.NewCode } };
            if (_clientListMode) await LoadClientsAsync();
            else await LoadClientDetailAsync(updated);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
        finally { EndBusy(); }
    }

    private async void TrashClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        if (!await new ConfirmTrashWindow(client.Title(), "Ses projets et tous ses fichiers seront concernés.").ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).TrashClient(client));
            PageTitle.Text = "Clients";
            Notify("Client envoyé dans la corbeille.");
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void RenameClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        var dialog = new TextPromptWindow("Renommer le client", "Le code client et les références existantes restent inchangés.", client.FolderName);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var newName = dialog.Value;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenameClient(client, newName));
            PageTitle.Text = "Clients";
            Notify("Client renommé.");
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void MoveClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        var destination = await PickWorkspaceFolderAsync(Path.GetDirectoryName(client.ClientPath) ?? WorkspaceRoot,
            "Déplacer le client « " + Path.GetFileName(client.ClientPath) + " »", client.ClientPath);
        if (string.IsNullOrWhiteSpace(destination)) return;
        try
        {
            var root = WorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).MoveClient(client, destination));
            PageTitle.Text = "Clients";
            Notify("Client déplacé.");
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }
}
