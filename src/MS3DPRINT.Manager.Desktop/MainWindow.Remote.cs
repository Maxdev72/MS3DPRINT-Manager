using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    public bool IsServerWorkspace { get; private set; }
    private RemoteWorkspaceConnection? _serverConnection;
    private HttpClient _serverHttp = null!;
    private RemoteWorkspaceCache _serverCache = null!;
    private IRemoteSessionStore? _serverSessions;
    private string _workspaceModePath = null!;
    private bool _serverBusy, _serverClosed;
    private int _serverGeneration;
    private bool _serverFormOpen;
    private Task? _serverPreloadTask;
    private string _serverPage = "Accueil";
    private readonly DispatcherTimer _serverTimer = new() { Interval = TimeSpan.FromSeconds(30) };

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
                if (!connection.IsAuthenticated) ServerLoginPanel.IsVisible = true;
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
        ServerRefreshButton.IsEnabled = !_serverBusy && _serverConnection?.IsAuthenticated == true;
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
        foreach (var control in new Control[] { ServerConnectionButton, ServerRefreshButton, ServerLogoutButton }) control.IsVisible = IsServerWorkspace;
        ServerReceiptButton.IsVisible = IsServerWorkspace && connection?.Copy?.PendingCommand is not null;
        ServerLoginPanel.IsVisible = IsServerWorkspace && connection?.IsAuthenticated != true;
        GlobalSearchContainer.IsVisible = !IsServerWorkspace;
        ToolTip.SetTip(ViewerNavButton, "Visualiseur 3D");
        ProjectTrackingViewButton.IsVisible = !IsServerWorkspace;
        ProjectCalendarViewButton.IsVisible = !IsServerWorkspace;
        ProjectListViewButton.IsVisible = !IsServerWorkspace;
        foreach (var button in NavigationButtons())
        {
            ToolTip.SetTip(button, IsServerWorkspace && button.Tag is string name && !IsServerPage(name) ? name + " · données de ce PC" : button.Tag);
        }
        ServerConnectionStatus.Text = !IsServerWorkspace ? "Atelier local indépendant · aucune synchronisation serveur" :
            (connection?.Workspace?.Name is { } workspaceName ? workspaceName + " · " : "") +
            (connection?.Server?.Host ?? "ms3dforge.fr") + " · " + (connection?.Account ?? "connexion requise") + " · " +
            StartupLabel(connection) +
            (connection?.Copy?.ReadAt is { } read ? " · dernière lecture " + read.ToLocalTime().ToString("dd/MM HH:mm") : "") +
            (connection?.Copy?.PendingCommand is { } pending ? " · résultat incertain : vérifier le reçu " + pending : "");
        RefreshWorkspaceIdentity();
        UpdateServerControls();
    }

    /// <summary>Data and documents are reported separately; documents stay « non associés » until Nextcloud is bound.</summary>
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
            _ => "connecté · " + (connection?.Revision?.Verified == true ? "données à jour" : "données à vérifier") + " · documents Nextcloud non associés"
        };
    }

    private Task LoginServerAsync()
    {
        if (!Uri.TryCreate(ServerAddress.Text, UriKind.Absolute, out var server) || server.Scheme != "https")
        { ServerConnectionStatus.Text = "Saisissez l’adresse HTTPS du serveur."; return Task.CompletedTask; }
        if (_serverBusy || _serverFormOpen) return Task.CompletedTask;
        _serverGeneration++;
        var password = ServerPassword.Text ?? "";
        ServerPassword.Text = "";
        return RunServerAsync(connection => connection.LoginAsync(server, (ServerAccount.Text ?? "").Trim(), password, ServerRemember.IsChecked == true));
    }
    private Task SynchronizeServerAsync() => RunServerAsync(connection => connection.RefreshAsync());
    private async void ServerLogin_Click(object? sender, RoutedEventArgs e) => await LoginServerAsync();
    private async void ServerRefresh_Click(object? sender, RoutedEventArgs e) => await SynchronizeServerAsync();
    private void ServerConnection_Click(object? sender, RoutedEventArgs e) => ServerLoginPanel.IsVisible = !ServerLoginPanel.IsVisible;
    private async void ServerReceipt_Click(object? sender, RoutedEventArgs e) => await RunServerAsync(connection => connection.CheckReceiptAsync(), refresh: true);
    private async void ServerLogout_Click(object? sender, RoutedEventArgs e)
    {
        if (_serverBusy || _serverFormOpen) return;
        _serverGeneration++;
        await RunServerAsync(connection => connection.LogoutAsync());
    }

    /// <summary>Pages hosted by the server; every other page keeps using this PC's workshop.</summary>
    private static bool IsServerPage(string destination) => destination is "Accueil" or "Clients" or "Projets";

    private async Task NavigateServerAsync(string destination)
    {
        _serverPage = destination;
        _serverClientDetail = null; _serverProjectDetail = null;
        ShowWorkspace(destination);
        PageTitle.Text = destination;
        PageSubtitle.Text = "Données de l’atelier serveur · " + (_serverConnection?.IsOnline == true ? "modifications sur le serveur" : "copie de consultation hors connexion");
        RenderServerPage();
        await Task.CompletedTask;
    }

    private void RenderServerPage()
    {
        if (!IsServerWorkspace) return;
        if (_serverPage == "Clients") RenderServerClients();
        else if (_serverPage == "Projets") RenderServerProjects();
        else RenderServerDashboard();
    }

    private async void WorkspaceMode_Click(object? sender, RoutedEventArgs e) => await ChangeWorkspaceModeAsync();
    private Task ChangeWorkspaceModeAsync() => ChangeWorkspaceModeCoreAsync(null);
    private async Task ChangeWorkspaceModeCoreAsync(RemoteDeviceSession? migrationSession)
    {
        bool OperationInProgress() => _migrationOpen || _serverBusy || _serverFormOpen || _projectTaskBusy || _panel is not null || _mobilePublishing != 0 || _estimateDraftAutoSaving || _busy != 0;
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
        _serverPage = "Accueil"; _serverClientDetail = null; _serverProjectDetail = null; _serverPreloadTask = null;
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
