using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    public bool IsServerWorkspace { get; private set; }
    /// <summary>Clients and projects are created on the PC, then sent by the sync; the server forms remain for a PC without the workspace folder.</summary>
    private bool CreatesOnServer => IsServerWorkspace && !Directory.Exists(WorkspaceRoot);
    private RemoteWorkspaceConnection? _serverConnection;
    private HttpClient _serverHttp = null!;
    private RemoteWorkspaceCache _serverCache = null!;
    private IRemoteSessionStore? _serverSessions;
    private string _workspaceModePath = null!;
    private bool _serverBusy, _serverClosed;
    private int _serverGeneration;
    private bool _serverFormOpen;
    private Task? _serverPreloadTask;
    private readonly DispatcherTimer _serverTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    private void ConfigureWorkspaceMode(HttpClient? http, RemoteWorkspaceCache? cache, string? modePath, IRemoteSessionStore? sessions, bool useHostDefaults)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "MS3DPRINT Manager");
        _workspaceModePath = modePath ?? Path.Combine(directory, "workspace-mode.txt");
        _serverHttp = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
        _serverCache = cache ?? new RemoteWorkspaceCache(Path.Combine(directory, "remote", "copy.json"));
        _serverSessions = sessions ?? (useHostDefaults ? DesktopApplication.RemoteSessionStoreFactory?.Invoke() : null);
        var explicitLocal = useHostDefaults && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE"));
        string? explicitMode = null;
        var unreadableMode = false;
        try { if (File.Exists(_workspaceModePath)) explicitMode = File.ReadAllText(_workspaceModePath).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadableMode = true; }
        if (useHostDefaults)
        {
            var root = _workspaceLocation.Resolve();
            var knownLocalUnavailable = !Directory.Exists(root) && !_workspaceLocation.NeedsSetup;
            IsServerWorkspace = !unreadableMode && !WorkspaceStartupMode.UseLocal(root, explicitMode, explicitLocal, knownLocalUnavailable);
        }
        else IsServerWorkspace = explicitMode != "local";

    }

    private void InitializeServerWorkspace()
    {
        _serverTimer.Tick += async (_, _) => { if (IsServerWorkspace && !_serverBusy) await SynchronizeServerAsync(); };
        if (IsServerWorkspace) OpenServerConnection();
        UpdateServerChrome();
    }

    private void OpenServerConnection()
    {
        try { _serverConnection = new RemoteWorkspaceConnection(_serverHttp, _serverCache, _serverSessions); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ServerConnectionStatus.Text = "Copie indisponible : " + e.Message; }
    }

    private async Task PreloadServerAsync()
    {
        _serverPreloadTask ??= RunServerAsync(async connection => await connection.RestoreAsync());
        await _serverPreloadTask;
        RenderServerPage();
        _serverTimer.Start();
    }

    private async Task RunServerAsync(Func<RemoteWorkspaceConnection, Task> action, bool refresh = false)
    {
        if (!IsServerWorkspace || _serverBusy || _serverConnection is not { } connection) return;
        var generation = _serverGeneration;
        _serverBusy = true;
        UpdateServerChrome();
        try
        {
            await action(connection);
            if (refresh && ServerContextCurrent(connection, generation)) await connection.RefreshAsync();
            if (ServerContextCurrent(connection, generation)) { UpdateServerChrome(); RenderServerPage(); }
        }
        catch (Exception e) when (e is RemoteWorkspaceException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            if (ServerContextCurrent(connection, generation))
            {
                UpdateServerChrome();
                RenderServerPage();
                ServerConnectionStatus.Text += " · " + e.Message;
                ServerLoginError.Text = e is RemoteWorkspaceException { Kind: RemoteFailure.Unauthorized } ? "Identifiant ou mot de passe incorrect."
                    : e is RemoteWorkspaceException { Kind: RemoteFailure.Network } ? "Serveur injoignable : vérifiez la connexion Internet." : e.Message;
                ServerLoginError.IsVisible = !connection.IsAuthenticated;
                if (!connection.IsAuthenticated && !_serverLoginDismissed) ServerLoginPanel.IsVisible = true;
            }
        }
        finally
        {
            if (ServerContextCurrent(connection, generation)) { _serverBusy = false; UpdateServerControls(); }
        }
    }

    private bool ServerContextCurrent(RemoteWorkspaceConnection connection, int generation)
        => !_serverClosed && IsServerWorkspace && _serverConnection == connection && _serverGeneration == generation;

    private void UpdateServerControls()
    {
        ServerLoginButton.IsEnabled = !_serverBusy && _serverConnection is not null;
        ServerLogoutButton.IsEnabled = !_serverBusy && _serverConnection?.IsAuthenticated == true;
        ServerReceiptButton.IsEnabled = !_serverBusy && _serverConnection?.IsAuthenticated == true;
        WorkspaceModeButton.IsEnabled = !_serverBusy;
        WorkspaceModeButton.IsVisible = !IsServerWorkspace;
        DashboardNewProject.IsEnabled = DashboardNewClient.IsEnabled = !IsServerWorkspace || !_serverBusy && _serverConnection?.CanWrite == true;
        ToolTip.SetTip(DashboardNewEstimate, "Préparer un devis");
        ToolTip.SetTip(DashboardViewer, "Visualiseur 3D");
    }

    private void UpdateServerChrome()
    {
        var connection = _serverConnection;
        ServerConnectionBar.IsVisible = IsServerWorkspace;
        LocalServerModeButton.IsVisible = !IsServerWorkspace;
        WorkspaceHeader.RowSpacing = IsServerWorkspace ? 14 : 0;
        var pcData = !IsServerWorkspace && WorkspaceStartupMode.HasLocalBusinessData(_workspaceLocation.Resolve());
        WorkspaceSourceText.Text = IsServerWorkspace ? "Données serveur" : pcData ? "Données PC conservées ; ouvrir le serveur ne les transfère pas" : "Espace de travail local";
        ToolTip.SetTip(WorkspaceSourceText, pcData ? "Données PC conservées ; ouvrir le serveur ne les transfère pas. Le serveur peut être plus ancien." : WorkspaceSourceText.Text);
        WorkspaceModeButton.Content = IsServerWorkspace ? "Atelier local" : "Atelier serveur";
        // One action at a time: sign in while signed out, sign out while connected. Data refreshes on its own.
        ServerConnectionButton.IsVisible = IsServerWorkspace && connection?.IsAuthenticated != true;
        ServerLogoutButton.IsVisible = IsServerWorkspace && connection?.IsAuthenticated == true;
        ServerReceiptButton.IsVisible = IsServerWorkspace && connection?.Copy?.PendingCommand is not null;
        ServerLoginPanel.IsVisible = IsServerWorkspace && connection?.IsAuthenticated != true && !_serverLoginDismissed
            && _serverPreloadTask is not { IsCompleted: false }; // no flash while a remembered session is being restored
        ToolTip.SetTip(ViewerNavButton, "Visualiseur 3D");
        ProjectCalendarViewButton.IsVisible = !IsServerWorkspace;
        ProjectListViewButton.IsVisible = !IsServerWorkspace;
        foreach (var button in NavigationButtons())
        {
            ToolTip.SetTip(button, button.Tag);
        }
        // Short state on screen; server, account and timings stay in the tooltip.
        ServerConnectionStatus.Text = !IsServerWorkspace ? "Atelier local indépendant · aucune synchronisation serveur" :
            (connection?.Workspace?.Name ?? "Atelier") + " · " + StartupLabel(connection) +
            (connection?.Copy?.PendingCommand is not null ? " · vérifiez le reçu" : "");
        ToolTip.SetTip(ServerConnectionStatus, !IsServerWorkspace ? null :
            (connection?.Server?.Host ?? "ms3dforge.fr") + " · " + (connection?.Account ?? "non connecté") +
            (connection?.Copy?.ReadAt is { } read ? "\nDernière lecture " + read.ToLocalTime().ToString("dd/MM HH:mm") : "") +
            (connection?.Copy?.PendingCommand is { } pending ? "\nRésultat incertain : reçu " + pending : ""));
        RefreshWorkspaceIdentity();
        UpdateServerControls();
    }

    /// <summary>Short connection state shown in the server bar; documents get their own state once Nextcloud is bound.</summary>
    public static string StartupLabel(RemoteWorkspaceConnection? connection)
    {
        var state = WorkspaceStartupPolicy.Resolve(new(
            HasAuthenticatedSession: connection?.IsAuthenticated == true,
            NetworkAvailable: connection?.IsOnline == true,
            HasVerifiedCopy: connection?.Copy is not null,
            WorkspaceSelected: connection?.Workspace is not null,
            DocumentsBound: false,
            LocalMirrorComplete: false,
            RecoveryRequired: connection?.Copy?.PendingCommand is not null));
        return state switch
        {
            WorkspaceStartupState.RecoveryRequired => "à vérifier",
            WorkspaceStartupState.ReadyOfflineReadOnly => "hors connexion · consultation seule",
            WorkspaceStartupState.NeedsSignIn => connection?.Copy is null ? "connexion requise" : "hors connexion · consultation seule",
            WorkspaceStartupState.NeedsWorkspaceSelection => "connecté · atelier non identifié (serveur à mettre à jour)",
            _ => "connecté · " + (connection?.Revision?.Verified == true ? "données à jour" : "données à vérifier")
        };
    }

    private Task LoginServerAsync()
    {
        if (!Uri.TryCreate(ServerAddress.Text, UriKind.Absolute, out var server) || server.Scheme != "https")
        { ServerConnectionStatus.Text = "Saisissez l’adresse HTTPS du serveur."; return Task.CompletedTask; }
        if (_serverBusy || _serverFormOpen) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(ServerAccount.Text) || string.IsNullOrEmpty(ServerPassword.Text))
        { ServerLoginError.Text = "Saisissez votre identifiant et votre mot de passe."; ServerLoginError.IsVisible = true; (string.IsNullOrWhiteSpace(ServerAccount.Text) ? ServerAccount : ServerPassword).Focus(); return Task.CompletedTask; }
        ServerLoginError.IsVisible = false;
        _serverGeneration++;
        var password = ServerPassword.Text ?? "";
        ServerPassword.Text = "";
        return RunServerAsync(connection => connection.LoginAsync(server, (ServerAccount.Text ?? "").Trim(), password, ServerRemember.IsChecked == true));
    }
    private RemoteProfileSync? _profileSync;
    private string? _profileSyncRoot;

    /// <summary>Every 10 s: read the server, then align the sheets of this PC and the server's (most recent wins).</summary>
    private async Task SynchronizeServerAsync()
    {
        ProfileSyncResult? result = null;
        await RunServerAsync(async connection =>
        {
            await connection.RefreshAsync();
            result = await SyncProfilesAsync(connection);
            if (result is { Pushed: > 0 }) await connection.RefreshAsync();
        });
        if (result is not { Changed: true } || _panel is not null || _busy != 0) return;
        var page = NavigationButtons().FirstOrDefault(b => b.Classes.Contains("selected"))?.Tag as string;
        if (page is null || CurrentNavigationStop?.Key != "page:" + page) return;
        await (page switch { "Accueil" => PreloadAsync(force: true), "Projets" => LoadProjectsAsync(), "Clients" => LoadClientsAsync(), "Tâches" when !_taskBusy => LoadTasksAsync(), _ => Task.CompletedTask });
    }

    private async Task<ProfileSyncResult?> SyncProfilesAsync(RemoteWorkspaceConnection connection)
    {
        var root = WorkspaceRoot;
        if (!connection.CanWrite || connection.Copy is not { } copy || !Directory.Exists(root)) return null;
        if (_profileSyncRoot != root) { _profileSync = new(root); _profileSyncRoot = root; }
        var sync = _profileSync!;
        var profiles = await Task.Run(() => sync.RunAsync(copy.Clients, copy.Projects,
            (id, command) => connection.ExecuteAsync(command.CommandId, client => client.UpdateClientAsync(id, command)),
            (id, command) => connection.ExecuteAsync(command.CommandId, client => client.UpdateProjectAsync(id, command)),
                command => connection.ExecuteAsync(command.CommandId, client => client.CreateClientAsync(command)),
                command => connection.ExecuteAsync(command.CommandId, client => client.CreateProjectAsync(command))));
        var tasks = await SyncTasksAsync(connection, root);
        return new ProfileSyncResult(profiles.Pulled + tasks.Pulled, profiles.Pushed + tasks.Pushed, profiles.Failed + tasks.Failed);
    }

    private static async Task<ProfileSyncResult> SyncTasksAsync(RemoteWorkspaceConnection connection, string root)
    {
        IReadOnlyList<MS3DPRINT.Manager.Core.Tasks.WorkTask> server;
        // An older server has no task list yet: tasks simply stay on this PC until it is updated.
        try { server = await connection.ReadAsync(client => client.GetTasksAsync()); }
        catch (RemoteWorkspaceException) { return new(0, 0, 0); }
        return await Task.Run(() => MS3DPRINT.Manager.Core.Tasks.RemoteTaskSync.RunAsync(root, server, task => connection.ReadAsync(client => client.PutTaskAsync(task))));
    }
    private async void ServerLogin_Click(object? sender, RoutedEventArgs e) => await LoginServerAsync();
    private bool _serverLoginDismissed;
    private void ServerConnection_Click(object? sender, RoutedEventArgs e) => ShowServerLogin();
    private void ShowServerLogin()
    {
        _serverLoginDismissed = false; ServerLoginPanel.IsVisible = true;
        (string.IsNullOrWhiteSpace(ServerAccount.Text) ? ServerAccount : ServerPassword).Focus();
    }
    private void ServerLoginDismiss_Click(object? sender, RoutedEventArgs e) { _serverLoginDismissed = true; ServerLoginPanel.IsVisible = false; }
    private async void ServerLogin_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; await LoginServerAsync(); }
        else if (e.Key == Avalonia.Input.Key.Escape) { e.Handled = true; ServerLoginDismiss_Click(sender, e); }
    }
    private async void ServerReceipt_Click(object? sender, RoutedEventArgs e) => await RunServerAsync(connection => connection.CheckReceiptAsync(), refresh: true);
    private async void ServerLogout_Click(object? sender, RoutedEventArgs e)
    {
        if (_serverBusy || _serverFormOpen) return;
        _serverGeneration++;
        await RunServerAsync(connection => connection.LogoutAsync());
    }

    /// <summary>In server mode, project choices on PC pages follow the server; a matching PC project keeps its folder.</summary>
    private IReadOnlyList<MS3DPRINT.Manager.Core.Projects.ProjectSummary> WithServerProjects(IReadOnlyList<MS3DPRINT.Manager.Core.Projects.ProjectSummary> local)
    {
        if (!IsServerWorkspace || _serverConnection?.Copy is not { } copy) return local;
        var clients = copy.Clients.ToDictionary(c => c.Id);
        return copy.Projects.Select(p => local.FirstOrDefault(l => string.Equals(l.Reference, p.Reference, StringComparison.OrdinalIgnoreCase))
            ?? new MS3DPRINT.Manager.Core.Projects.ProjectSummary(clients.TryGetValue(p.ClientId, out var c) ? c.DisplayName : "", "", "server:" + p.Id,
                p.Reference, p.Reference + "_" + p.ProjectName, null)).ToArray();
    }

    private IEnumerable<MS3DPRINT.Manager.Core.Search.GlobalSearchResult> ServerSearch(string query)
    {
        if (_serverConnection?.Copy is not { } copy) return [];
        bool Has(string? value) => value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;
        var clients = copy.Clients.ToDictionary(c => c.Id);
        return copy.Clients.Where(c => Has(c.DisplayName) || Has(c.ClientCode))
            .Select(c => new MS3DPRINT.Manager.Core.Search.GlobalSearchResult(MS3DPRINT.Manager.Core.Search.GlobalSearchResultKind.Client, c.DisplayName, c.ClientCode, "server:" + c.Id))
            .Concat(copy.Projects.Where(p => Has(p.ProjectName) || Has(p.Reference)).Select(p => new MS3DPRINT.Manager.Core.Search.GlobalSearchResult(
                MS3DPRINT.Manager.Core.Search.GlobalSearchResultKind.Project, p.ProjectName,
                p.Reference + " · " + (clients.TryGetValue(p.ClientId, out var c) ? c.DisplayName : ""), "server:" + p.Id)));
    }

    private IEnumerable<string> ServerClientNames() => IsServerWorkspace && _serverConnection?.Copy is { } copy ? copy.Clients.Select(c => c.DisplayName) : [];

    private string _serverRenderSignature = "";

    /// <summary>After a server read: refresh an open server sheet, or add newly created server clients and projects to the PC lists.</summary>
    private void RenderServerPage()
    {
        if (!IsServerWorkspace || _serverConnection?.Copy is not { } copy) return;
        var signature = (_serverConnection.CanWrite ? "w" : "r") + string.Join("|", copy.Projects.Select(p => p.Id + ":" + p.UpdatedAt.UtcTicks)) + "#" + string.Join("|", copy.Clients.Select(c => c.Id + ":" + c.DisplayName));
        if (signature == _serverRenderSignature) return;
        _serverRenderSignature = signature;
        if (ProjectsWorkspacePanel.IsVisible && (_serverProjectDetail is not null || _projectListMode)) RenderServerProjects();
        else if (ClientsWorkspacePanel.IsVisible && (_serverClientDetail is not null || _clientListMode)) RenderServerClients();
    }

    private async void WorkspaceMode_Click(object? sender, RoutedEventArgs e) => await ChangeWorkspaceModeAsync();
    private Task ChangeWorkspaceModeAsync() => ChangeWorkspaceModeCoreAsync(null);
    private async Task ChangeWorkspaceModeCoreAsync(RemoteDeviceSession? migrationSession)
    {
        bool OperationInProgress() => _migrationOpen || _serverBusy || _serverFormOpen || _taskBusy || _panel is not null || _mobilePublishing != 0 || _estimateDraftAutoSaving || _busy != 0;
        if (OperationInProgress())
        { Notify("Terminez l’opération ou fermez le formulaire avant de changer d’atelier."); return; }
        if (!IsServerWorkspace && !FlushEstimateDraft()) return;
        var server = !IsServerWorkspace;
        if (server && migrationSession is null && WorkspaceStartupMode.HasLocalBusinessData(_workspaceLocation.Resolve()) && !await new ServerSwitchNotice().ShowDialog<bool>(this)) return;
        if (OperationInProgress())
        { Notify("Terminez l’opération ou fermez le formulaire avant de changer d’atelier."); return; }
        if (migrationSession is not null)
        {
            try
            {
                if (_serverCache.Load() is { PendingCommand: not null } pending
                    && (new Uri(pending.Server) != new Uri(migrationSession.Server) || !pending.Account.Equals(migrationSession.Account, StringComparison.OrdinalIgnoreCase)))
                { Notify("Vérifiez le reçu de la commande de l’autre compte avant d’ouvrir cet atelier serveur.", true); return; }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            { Notify("Le journal serveur doit être vérifié avant de changer de mode : " + ex.Message, true); return; }
        }
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_workspaceModePath))!); File.WriteAllText(_workspaceModePath, server ? "server" : "local"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notify("Choix du mode non enregistré : " + ex.Message, true); return; }
        _serverGeneration++;
        _serverConnection?.Dispose(); _serverConnection = null; _serverTimer.Stop();
        _estimateDraftTimer.Stop(); _estimateTimer?.Stop(); _estimateFormRoot = null; _estimateDraftTouched = false;
        CancelFileRead(); _searchCancellation?.Cancel(); _searchCancellation = null;
        _modelViewer?.Release(); ViewerWorkspacePanel.IsVisible = false;
        _navigationHistory.Clear(); _navigationPosition = -1; _navigationWorkspaceRoot = null;
        _allClients = []; _allProjects = []; _currentClientDetail = null; _projectOriginClient = null;
        ClientList.Children.Clear(); ProjectList.Children.Clear(); RecentProjectsList.Children.Clear();
        _activeFileBrowser = null;
        IsServerWorkspace = server;
        _serverRenderSignature = ""; _serverClientDetail = null; _serverProjectDetail = null; _serverPreloadTask = null;
        _preloaded = false;
        if (server)
        {
            OpenServerConnection();
            if (migrationSession is not null && _serverConnection is { } connection)
            {
                ServerAddress.Text = migrationSession.Server; ServerAccount.Text = migrationSession.Account;
                await RunServerAsync(c => c.ConnectSessionAsync(migrationSession));
                _serverPreloadTask = Task.CompletedTask;
            }
        }
        UpdateServerChrome(); ShowWorkspace("Accueil");
        PageTitle.Text = server ? "Accueil" : "Votre atelier";
        await PreloadAsync(force: true);
        if (!server) await EnsureWorkspaceAvailableAsync();
    }

    private sealed class ServerSwitchNotice : DialogWindow
    {
        public ServerSwitchNotice()
        {
            Width = 540;
            var proceed = new Button { Name = "OpenOlderServer", Content = "Ouvrir le serveur indépendant" };
            proceed.Click += (_, _) => Close(true);
            var cancel = new Button { Content = "Rester sur ce PC" };
            cancel.Click += (_, _) => Close(false);
            Layout("Conserver les données de ce PC", "Les clients, projets, tâches et fichiers du PC peuvent être plus récents que le serveur. Ouvrir le serveur ne transfère ni ne synchronise les données du PC et ne les remplace pas.", [], proceed, cancel, destructive: true);
            proceed.Classes.Remove("danger");
        }
    }

    private void DisposeServerWorkspace()
    { if (_panel is GuidedMigrationPanel migration) migration.Close(false); _serverClosed = true; _serverGeneration++; _serverTimer.Stop(); _serverConnection?.Dispose(); _serverConnection = null; }
}
