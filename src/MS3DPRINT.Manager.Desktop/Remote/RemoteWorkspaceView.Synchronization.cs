using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Native;

public sealed partial class RemoteWorkspaceView
{
    private readonly DispatcherTimer _synchronizationTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private RemoteWorkspaceCache? _cache;
    private IDisposable? _cacheLease;
    private string? _sessionAccount;
    private DateTimeOffset? _snapshotReadAt;
    private bool _viewAttached;

    private void InitializeSynchronization(RemoteWorkspaceCache? cache)
    {
        _cache = cache;
        _synchronizationTimer.Tick += async (_, _) => await SynchronizeServerAsync();
        AttachedToVisualTree += (_, _) =>
        {
            _viewAttached = true;
            if (!_disposed && _client?.IsAuthenticated == true) _synchronizationTimer.Start();
        };
        DetachedFromVisualTree += (_, _) => { _viewAttached = false; _synchronizationTimer.Stop(); };
        try
        {
            _cacheLease = _cache?.AcquireLease();
            if (_cache?.Load() is not { } copy) return;
            _server.Text = copy.Server; _name.Text = copy.Account; _password.Text = "";
            _sessionServer = new(copy.Server); _sessionAccount = copy.Account;
            _clientSnapshot = copy.Clients; _projectSnapshot = copy.Projects;
            _snapshotReadAt = copy.ReadAt;
            _uncertainCommand = copy.PendingCommand;
            _uncertainServer = copy.PendingCommand.HasValue ? _sessionServer : null;
            BindClients(); ShowLastRead();
            _status.Text = copy.PendingCommand.HasValue
                ? "Copie locale · commande à vérifier. Reconnectez-vous au même compte et serveur pour consulter son reçu."
                : "Copie locale de consultation · reconnectez-vous pour reprendre la synchronisation. Aucune écriture hors connexion.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Do not overwrite an unreadable journal: it could contain an unresolved command.
            _cacheRecoveryBlocked = true;
            _status.Text = "Copie locale illisible. Conservez-la pour vérifier les commandes avant de la remplacer : " + ex.Message;
        }
    }

    private bool _cacheRecoveryBlocked;

    public Task SynchronizeServerAsync()
    {
        if (_disposed || _cacheRecoveryBlocked || _client is null || _confirmation.IsVisible) return Task.CompletedTask;
        if (!_client.IsAuthenticated)
        {
            _online = false; _synchronizationTimer.Stop();
            _status.Text = "Session expirée · dernière copie consultable. Reconnectez-vous pour reprendre la synchronisation.";
            UpdateEnabled(); return Task.CompletedTask;
        }
        return RunAsync(() => RefreshAsync(automatic: true));
    }

    private void ShowLastRead() => _lastRead.Text = _snapshotReadAt is { } date
        ? "Dernière synchronisation : " + date.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
        : "Aucune synchronisation complète effectuée.";

    private void SaveCopy()
    {
        if (_disposed || _cache is null || _sessionServer is null || _sessionAccount is null) return;
        _cache.Save(new(_sessionServer.AbsoluteUri, _sessionAccount, _snapshotReadAt, _clientSnapshot, _projectSnapshot, _uncertainCommand));
    }

    private async Task<T> ExecuteCommandAsync<T>(Guid commandId, Func<Task<T>> send)
    {
        _uncertainCommand = commandId; _uncertainServer = _sessionServer;
        try { SaveCopy(); }
        catch { _uncertainCommand = null; _uncertainServer = null; throw; }
        T result;
        try { result = await send(); }
        catch (RemoteWorkspaceException ex) when (ex.Kind != RemoteFailure.Uncertain)
        {
            CompletePendingCommand(); throw;
        }
        // If the view was closed during the request, keep the on-disk receipt to resolve on reopening.
        if (!_disposed) CompletePendingCommand();
        return result;
    }

    private void CompletePendingCommand()
    {
        if (_disposed) return;
        var pending = _uncertainCommand;
        _uncertainCommand = null; _uncertainServer = null;
        try { SaveCopy(); }
        catch { _uncertainCommand = pending; _uncertainServer = _sessionServer; throw; }
    }
}
