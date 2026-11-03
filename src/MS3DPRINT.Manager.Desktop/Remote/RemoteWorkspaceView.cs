using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Native;

/// <summary>Shared PC/Android server screen. Its snapshot, drafts and device session never touch the local workshop.</summary>
public sealed partial class RemoteWorkspaceView : UserControl, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Func<string, Task<bool>>? _confirmRemoval;
    private RemoteWorkspaceClient? _client;
    private readonly TextBox _server = Field("https://ms3dforge.fr", "RemoteServer");
    private readonly TextBox _name = Field("Nom du compte", "RemoteName");
    private readonly TextBox _password = Field("Mot de passe", "RemotePassword");
    private readonly TextBlock _status = Text("Connectez-vous au serveur HTTPS.", "RemoteStatus");
    private readonly TextBlock _lastRead = Text("");
    private readonly TextBlock _projectInfo = Text("Sélectionnez un projet pour consulter ses tâches.");
    private readonly ComboBox _clients = new() { Name = "RemoteClients", PlaceholderText = "Choisir un client", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _projects = new() { Name = "RemoteProjects", PlaceholderText = "Choisir un projet", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _kind = new() { Name = "RemoteClientKind", ItemsSource = new[] { "Professionnel", "Particulier" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _clientCode = Field("Code client (ex. ABC)", "RemoteClientCode");
    private readonly TextBox _company = Field("Raison sociale", "RemoteCompany");
    private readonly TextBox _firstName = Field("Prénom", "RemoteFirstName");
    private readonly TextBox _lastName = Field("Nom", "RemoteLastName");
    private readonly TextBox _email = Field("Courriel");
    private readonly TextBox _phone = Field("Téléphone");
    private readonly TextBox _address = Field("Adresse");
    private readonly TextBox _clientNotes = Field("Notes client");
    private readonly TextBox _projectName = Field("Nom du projet", "RemoteProjectName");
    private readonly TextBox _description = Field("Description du projet");
    private readonly TextBox _dueDate = Field("Échéance AAAA-MM-JJ (facultatif)");
    private readonly ComboBox _priority = new() { ItemsSource = new[] { "Aucune priorité", "Basse", "Moyenne", "Haute" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _taskTitle = Field("Texte de la tâche", "RemoteTaskTitle");
    private readonly TextBox _taskNotes = Field("Notes de la tâche", "RemoteTaskNotes");
    private readonly TextBlock _editing = Text("Nouvelle tâche");
    private readonly StackPanel _taskList = new() { Spacing = 8 };
    private readonly StackPanel _confirmation = new() { Spacing = 10, IsVisible = false };
    private readonly List<Control> _mutations = [];
    private readonly List<Control> _projectMutations = [];
    private readonly Button _login;
    private readonly Button _logout;
    private readonly Button _refresh;
    private readonly Button _receipt;
    private readonly Button _createProject;
    private readonly Button _saveTask;
    private IReadOnlyList<ClientResponse> _clientSnapshot = [];
    private IReadOnlyList<ProjectResponse> _projectSnapshot = [];
    private ProjectDetailResponse? _project;
    private Guid? _editingTask;
    private Guid? _uncertainCommand;
    private Uri? _sessionServer;
    private Uri? _uncertainServer;
    private bool _busy;
    private bool _online;
    private bool _binding;
    private bool _disposed;

    public RemoteWorkspaceView(HttpClient? http = null, Func<string, Task<bool>>? confirmRemoval = null, RemoteWorkspaceCache? cache = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };
        _confirmRemoval = confirmRemoval;
        _server.Text = "https://ms3dforge.fr";
        _password.PasswordChar = '●';
        _password.MaxLength = 1024;
        _taskTitle.MaxLength = 500; _taskNotes.MaxLength = 10000;
        _taskNotes.AcceptsReturn = true; _taskNotes.MinHeight = 80;
        _login = Action("Se connecter", "RemoteLogin", LoginAsync);
        _logout = Action("Se déconnecter", "RemoteLogout", LogoutAsync);
        _refresh = Action("Actualiser le serveur", "RemoteRefresh", RefreshAsync);
        _receipt = Action("Vérifier le reçu de la commande", "RemoteReceipt", CheckReceiptAsync);
        var createClient = Action("Créer le client", "RemoteCreateClient", CreateClientAsync); _mutations.Add(createClient);
        _createProject = Action("Créer le projet du client sélectionné", "RemoteCreateProject", CreateProjectAsync); _mutations.Add(_createProject);
        _saveTask = Action("Enregistrer la tâche", "RemoteSaveTask", SaveTaskAsync); _projectMutations.Add(_saveTask);
        var newTask = Action("Préparer une nouvelle tâche", "RemoteNewTask", () => { _editingTask = null; _editing.Text = "Nouvelle tâche"; _taskTitle.Text = ""; _taskNotes.Text = ""; return Task.CompletedTask; });
        _clients.SelectionChanged += (_, _) => { if (!_binding) { BindProjects(); UpdateEnabled(); } };
        _projects.SelectionChanged += async (_, _) => { if (!_binding) await RunAsync(LoadSelectedProjectAsync); };
        _kind.SelectionChanged += (_, _) => UpdateClientFields();
        _taskList.Children.Add(Text("Aucune tâche chargée."));
        var body = new StackPanel { Spacing = 16, Margin = new Thickness(18), MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(Text("MS3DForge · Atelier distant", size: 24));
        body.Children.Add(Text("Clients, projets et tâches du serveur. Les achats et recettes du compagnon restent liés au dossier d’échange local. Cette vue n’écrit jamais dans l’atelier local."));
        body.Children.Add(Card("Connexion sécurisée", _server, _name, _password, _login, _logout, _status, _lastRead, _refresh, _receipt));
        body.Children.Add(Card("Clients", _clients, new Expander { Header = "Créer un client", HorizontalAlignment = HorizontalAlignment.Stretch, Content = Stack(_kind, _clientCode, _company, _firstName, _lastName, _email, _phone, _address, _clientNotes, createClient) }));
        body.Children.Add(Card("Projets du client", _projects, _projectInfo, new Expander { Header = "Créer un projet", HorizontalAlignment = HorizontalAlignment.Stretch, Content = Stack(_projectName, _description, _dueDate, _priority, _createProject) }));
        body.Children.Add(Card("Tâches du projet", _taskList, _confirmation, _editing, _taskTitle, _taskNotes, _saveTask, newTask));
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        InitializeSynchronization(cache);
        UpdateClientFields(); UpdateEnabled();
    }

    private async Task LoginAsync()
    {
        if (!Uri.TryCreate((_server.Text ?? "").Trim(), UriKind.Absolute, out var uri)) throw new ArgumentException("Adresse HTTPS invalide.");
        if (_uncertainCommand is not null && uri != _uncertainServer)
            throw new ArgumentException("Reconnectez-vous au serveur de la commande incertaine pour vérifier son reçu.");
        if (_cacheRecoveryBlocked) throw new ArgumentException("La copie locale doit être vérifiée avant la reconnexion.");
        var account = (_name.Text ?? "").Trim().ToUpperInvariant();
        if (_uncertainCommand is not null && account != _sessionAccount)
            throw new ArgumentException("Reconnectez-vous au compte de la commande incertaine pour vérifier son reçu.");
        var client = new RemoteWorkspaceClient(_http, uri);
        var password = _password.Text ?? "";
        _password.Text = "";
        await client.LoginAsync((_name.Text ?? "").Trim(), password);
        _client?.ForgetSession(); _client = client;
        if (_disposed) { client.ForgetSession(); return; }
        var sameWorkshop = uri == _sessionServer && account == _sessionAccount;
        _sessionServer = uri; _sessionAccount = account;
        if (_viewAttached) _synchronizationTimer.Start();
        if (!sameWorkshop)
        {
            _clientSnapshot = []; _projectSnapshot = []; _project = null; _snapshotReadAt = null;
            SaveCopy();
        }
        BindClients(); RenderTasks();
        await RefreshAsync();
    }

    public Task RefreshServerAsync() => RunAsync(RefreshAsync);

    private async Task LogoutAsync()
    {
        _online = false;
        _synchronizationTimer.Stop();
        try { if (_client is not null) await _client.LogoutAsync(); }
        finally
        {
            _client?.ForgetSession();
            // Session revocation must still happen if removing the local copy fails.
            if (!_disposed && _uncertainCommand is null) _cache?.Clear();
            _status.Text = _uncertainCommand is { } id
                ? "Session fermée. La commande " + id + " reste incertaine : reconnectez-vous au même serveur pour vérifier son reçu. Les modifications restent bloquées."
                : "Session fermée sur cet appareil. Dernière vue consultable jusqu’à la fermeture de cet écran.";
        }
    }

    private Task RefreshAsync() => RefreshAsync(automatic: false);

    private async Task RefreshAsync(bool automatic)
    {
        var client = RequireClient();
        var clients = await client.GetClientsAsync();
        var projects = await client.GetProjectsAsync();
        var selected = SelectedProjectId;
        var detail = selected.HasValue && projects.Any(p => p.Id == selected) ? await client.GetProjectAsync(selected.Value) : null;
        var copy = new RemoteWorkspaceCopy(_sessionServer!.AbsoluteUri, _sessionAccount!, DateTimeOffset.UtcNow, clients, projects, _uncertainCommand);
        RemoteWorkspaceCache.Validate(copy);
        if (_disposed) return;
        _cache?.Save(copy);
        // A background read must not silently make an old draft eligible to overwrite a newer version.
        if (automatic && _project?.Id == detail?.Id && (_editingTask.HasValue || !string.IsNullOrEmpty(_taskTitle.Text) || !string.IsNullOrEmpty(_taskNotes.Text))) detail = _project;
        _snapshotReadAt = copy.ReadAt;
        _clientSnapshot = clients; _projectSnapshot = projects; _project = detail;
        BindClients(); RenderTasks();
        _online = true;
        ShowLastRead();
        _status.Text = _uncertainCommand is null ? "Synchronisation automatique active · modifications envoyées au serveur." : "Résultat encore incertain. Consultez le reçu ; aucun renvoi automatique.";
    }

    private async Task LoadSelectedProjectAsync()
    {
        if (SelectedProjectId is not { } id) { _project = null; RenderTasks(); return; }
        if (!_online || _client?.IsAuthenticated != true)
        {
            var saved = _projectSnapshot.Single(p => p.Id == id);
            _project = new(saved.Id, saved.ClientId, "", saved.Reference, "", saved.ProjectName, Enum.Parse<ProjectStatus>(saved.Status), default, saved.DueDate, null, null, saved.UpdatedAt, Enum.Parse<ProjectPriority>(saved.Priority), saved.Tasks);
            RenderTasks(); return;
        }
        _project = await _client.GetProjectAsync(id);
        RenderTasks();
    }

    private async Task CreateClientAsync()
    {
        RequireWritable();
        var commandId = Guid.NewGuid();
        var created = await ExecuteCommandAsync(commandId, () => RequireClient().CreateClientAsync(new(commandId, _kind.SelectedIndex == 0 ? ClientKind.Professional : ClientKind.Individual,
            Value(_clientCode), Value(_company), Value(_firstName), Value(_lastName), Value(_address), Value(_clientNotes), new(Value(_firstName), Value(_lastName), null, Value(_phone), Value(_email)))));
        _clientCode.Text = _company.Text = _firstName.Text = _lastName.Text = _email.Text = _phone.Text = _address.Text = _clientNotes.Text = "";
        await RefreshAsync();
        _clients.SelectedIndex = _clientSnapshot.ToList().FindIndex(c => c.Id == created.Id);
    }

    private async Task CreateProjectAsync()
    {
        RequireWritable();
        if (SelectedClientId is not { } clientId) throw new ArgumentException("Sélectionnez un client.");
        DateOnly? due = null;
        if (!string.IsNullOrWhiteSpace(_dueDate.Text))
        {
            if (!DateOnly.TryParseExact(_dueDate.Text.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
                throw new ArgumentException("Échéance attendue : AAAA-MM-JJ.");
            due = parsed;
        }
        var commandId = Guid.NewGuid();
        var created = await ExecuteCommandAsync(commandId, () => RequireClient().CreateProjectAsync(new(commandId, clientId, Value(_projectName), DateTime.Now.Year, due, Value(_description), (ProjectPriority)_priority.SelectedIndex)));
        _projectName.Text = _description.Text = _dueDate.Text = "";
        await RefreshAsync();
        _projects.SelectedIndex = VisibleProjects().ToList().FindIndex(p => p.Id == created.Id);
        _project = created; RenderTasks();
    }

    private async Task SaveTaskAsync()
    {
        RequireWritable();
        var p = _project ?? throw new ArgumentException("Sélectionnez un projet.");
        var commandId = Guid.NewGuid();
        _project = await ExecuteCommandAsync(commandId, () => _editingTask is { } task
            ? RequireClient().EditTaskAsync(p.Id, task, new(commandId, p.UpdatedAt, Value(_taskTitle), Value(_taskNotes)))
            : RequireClient().AddTaskAsync(p.Id, new(commandId, p.UpdatedAt, Value(_taskTitle), Value(_taskNotes))));
        RememberProject(); RenderTasks();
        _editingTask = null; _editing.Text = "Nouvelle tâche"; _taskTitle.Text = _taskNotes.Text = "";
    }

    private async Task ChangeTaskAsync(ProjectTask task, string action)
    {
        RequireWritable();
        var p = _project ?? throw new ArgumentException("Sélectionnez un projet.");
        var commandId = Guid.NewGuid();
        _project = await ExecuteCommandAsync(commandId, () => action switch
        {
            "complete" => RequireClient().CompleteTaskAsync(p.Id, task.Id, new(commandId, p.UpdatedAt, !task.IsCompleted)),
            "up" => RequireClient().MoveTaskAsync(p.Id, task.Id, new(commandId, p.UpdatedAt, -1)),
            "down" => RequireClient().MoveTaskAsync(p.Id, task.Id, new(commandId, p.UpdatedAt, 1)),
            "remove" => RequireClient().RemoveTaskAsync(p.Id, task.Id, new(commandId, p.UpdatedAt, true)),
            _ => throw new ArgumentException("Action inconnue.")
        });
        RememberProject(); RenderTasks();
    }

    private async Task CheckReceiptAsync()
    {
        if (_uncertainCommand is not { } id) return;
        var receipt = await RequireClient().GetCommandReceiptAsync(id);
        if (receipt.CommandId == id && receipt.State == "Completed" && receipt.StatusCode is >= 200 and < 500)
        {
            CompletePendingCommand();
            await RefreshAsync();
            _status.Text = $"Commande vérifiée : résultat serveur {receipt.StatusCode}. Contrôlez la vue actualisée et votre brouillon avant toute nouvelle action.";
        }
        else _status.Text = "Commande " + id + " : " + receipt.State + ". Modifications bloquées ; vérification du serveur nécessaire.";
    }

    private async Task RunAsync(Func<Task> work)
    {
        if (_busy || _disposed) return;
        _busy = true; UpdateEnabled();
        try { await work(); }
        catch (RemoteWorkspaceException ex)
        {
            _online = false;
            if (ex.Kind == RemoteFailure.Uncertain && ex.CommandId is { } id)
            {
                _uncertainCommand = id;
                _uncertainServer = _sessionServer;
            }
            _status.Text = ex.Message + (ex.Kind == RemoteFailure.Uncertain ? " Reçu : " + ex.CommandId : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _online = false;
            _status.Text = "Copie locale indisponible : " + ex.Message + (_uncertainCommand is null ? " Aucune nouvelle commande ne sera envoyée avant résolution." : " Vérifiez le reçu avant toute nouvelle commande.");
        }
        catch (ArgumentException ex) { _status.Text = ex.Message; }
        finally { _busy = false; UpdateEnabled(); }
    }

    private void RenderTasks()
    {
        _taskList.Children.Clear(); _projectMutations.Clear(); _projectMutations.Add(_saveTask);
        _confirmation.IsVisible = false;
        _projectInfo.Text = _project is { } p ? $"{p.Reference} · {p.ProjectName}\n{p.Status} · priorité {p.Priority}" : "Sélectionnez un projet pour consulter ses tâches.";
        foreach (var task in _project?.Tasks ?? [])
        {
            var edit = Action("Modifier", "", () => { _editingTask = task.Id; _editing.Text = "Modifier : " + task.Title; _taskTitle.Text = task.Title; _taskNotes.Text = task.Notes; return Task.CompletedTask; });
            var complete = Action(task.IsCompleted ? "Rouvrir" : "Terminer", "", () => ChangeTaskAsync(task, "complete"));
            var up = Action("Monter", "", () => ChangeTaskAsync(task, "up"));
            var down = Action("Descendre", "", () => ChangeTaskAsync(task, "down"));
            var remove = Action("Retirer…", "", () => AskRemovalAsync(task));
            _projectMutations.AddRange([complete, up, down, remove]);
            _taskList.Children.Add(Card((task.IsCompleted ? "✓ " : "○ ") + task.Title, Text(task.Notes ?? ""), new WrapPanel { Orientation = Orientation.Horizontal, Children = { edit, complete, up, down, remove } }));
        }
        if (_project?.Tasks.Count == 0) _taskList.Children.Add(Text("Aucune tâche. Ajoutez la première action ci-dessous."));
        UpdateEnabled();
    }

    private async Task AskRemovalAsync(ProjectTask task)
    {
        if (_confirmRemoval is not null)
        {
            if (await _confirmRemoval(task.Title)) await ChangeTaskAsync(task, "remove");
            return;
        }
        // Android has no owned Window: keep the same explicit, cancel-first confirmation in the page.
        _confirmation.Children.Clear();
        var cancel = new Button { Content = "Annuler", Name = "RemoteCancelRemoval" };
        var confirm = Action("Confirmer le retrait", "RemoteConfirmRemoval", async () => { _confirmation.IsVisible = false; await ChangeTaskAsync(task, "remove"); });
        confirm.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Enter) e.Handled = true; }, RoutingStrategies.Tunnel);
        confirm.AddHandler(KeyUpEvent, (_, e) => { if (e.Key == Key.Enter) e.Handled = true; }, RoutingStrategies.Tunnel);
        cancel.Click += (_, _) => _confirmation.IsVisible = false;
        _confirmation.Children.Add(Text("Retirer « " + task.Title + " » ? La tâche sera retirée du projet sur le serveur."));
        _confirmation.Children.Add(cancel); _confirmation.Children.Add(confirm);
        _confirmation.IsVisible = true; cancel.Focus();
    }

    private void BindClients()
    {
        var id = SelectedClientId;
        _binding = true;
        _clients.ItemsSource = _clientSnapshot.Select(c => new ComboBoxItem { Content = c.DisplayName + " · " + c.ClientCode, Tag = c.Id }).ToArray();
        _clients.SelectedIndex = _clientSnapshot.ToList().FindIndex(c => c.Id == id);
        _binding = false;
        BindProjects();
    }

    private void BindProjects()
    {
        var id = SelectedProjectId;
        _binding = true;
        var list = VisibleProjects().ToList();
        _projects.ItemsSource = list.Select(p => new ComboBoxItem { Content = p.Reference + " · " + p.ProjectName, Tag = p.Id }).ToArray();
        _projects.SelectedIndex = list.FindIndex(p => p.Id == id);
        _binding = false;
        if (SelectedProjectId != _project?.Id) { _project = null; RenderTasks(); }
    }

    private IEnumerable<ProjectResponse> VisibleProjects() => _projectSnapshot.Where(p => p.ClientId == SelectedClientId);
    private Guid? SelectedClientId => (_clients.SelectedItem as ComboBoxItem)?.Tag as Guid?;
    private Guid? SelectedProjectId => (_projects.SelectedItem as ComboBoxItem)?.Tag as Guid?;
    private RemoteWorkspaceClient RequireClient() => _client ?? throw new ArgumentException("Connectez-vous au serveur.");
    private void RequireWritable() { if (_cacheRecoveryBlocked || !_online || _uncertainCommand is not null || _client?.IsAuthenticated != true) throw new ArgumentException("Actualisez le serveur avant de modifier les données."); }
    private void RememberProject()
    {
        if (_project is not { } p) return;
        _projectSnapshot = _projectSnapshot.Select(existing => existing.Id == p.Id ? existing with { UpdatedAt = p.UpdatedAt, Tasks = p.Tasks } : existing).ToArray();
        SaveCopy();
    }
    private void UpdateClientFields() { _company.IsVisible = _kind.SelectedIndex == 0; _firstName.IsVisible = _lastName.IsVisible = _kind.SelectedIndex == 1; }
    private void UpdateEnabled()
    {
        var authenticated = _client?.IsAuthenticated == true;
        var writable = authenticated && _online && !_busy && !_cacheRecoveryBlocked && _uncertainCommand is null;
        _login.IsEnabled = !_busy && !_cacheRecoveryBlocked && !authenticated;
        _server.IsEnabled = !_busy && !authenticated && _uncertainCommand is null;
        _name.IsEnabled = _password.IsEnabled = !_busy && !authenticated;
        _logout.IsEnabled = !_busy && authenticated;
        _refresh.IsEnabled = !_busy && authenticated;
        _receipt.IsVisible = _uncertainCommand is not null; _receipt.IsEnabled = !_busy && authenticated;
        _clients.IsEnabled = _projects.IsEnabled = !_busy;
        foreach (var control in _mutations) control.IsEnabled = writable;
        _createProject.IsEnabled = writable && SelectedClientId is not null;
        foreach (var control in _projectMutations) control.IsEnabled = writable && _project is not null;
    }
    private Button Action(string label, string name, Func<Task> work)
    {
        var button = new Button { Content = label, Name = string.IsNullOrEmpty(name) ? null : name, MinHeight = 42, Margin = new Thickness(0, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += async (_, _) => await RunAsync(work);
        return button;
    }
    private static TextBox Field(string watermark, string? name = null)
    {
        var box = new TextBox { PlaceholderText = watermark, Name = name, MinHeight = 42, HorizontalAlignment = HorizontalAlignment.Stretch };
        Avalonia.Automation.AutomationProperties.SetName(box, watermark);
        return box;
    }
    private static string Value(TextBox box) => (box.Text ?? "").Trim();
    private static TextBlock Text(string value, string? name = null, double size = 14) => new() { Text = value, Name = name, TextWrapping = TextWrapping.Wrap, FontSize = size };
    private static StackPanel Stack(params Control[] controls) { var stack = new StackPanel { Spacing = 10 }; foreach (var control in controls) stack.Children.Add(control); return stack; }
    private static Border Card(string title, params Control[] controls) => new() { Padding = new Thickness(14), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(10), Child = Stack([Text(title, size: 18), .. controls]) };
    public void Dispose() { _disposed = true; _synchronizationTimer.Stop(); _client?.ForgetSession(); _cacheLease?.Dispose(); _cacheLease = null; if (_ownsHttp) _http.Dispose(); }
}
