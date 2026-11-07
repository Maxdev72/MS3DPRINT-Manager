namespace MS3DPRINT.Manager.Core.Remote;

/// <summary>Owns one cache lease, one device session and a durable, never automatically replayed command.</summary>
public sealed class RemoteWorkspaceConnection : IDisposable
{
    private readonly HttpClient _http;
    private readonly RemoteWorkspaceCache _cache;
    private readonly IRemoteSessionStore? _sessions;
    private readonly IDisposable _lease;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _state = new();
    private RemoteWorkspaceClient? _client;
    private bool _disposed, _cacheHealthy = true;
    public RemoteWorkspaceCopy? Copy { get; private set; }
    public bool IsAuthenticated => !_disposed && _client?.IsAuthenticated == true;
    public bool IsOnline { get; private set; }
    public bool CanWrite => IsAuthenticated && IsOnline && _cacheHealthy && Copy is { PendingCommand: null };
    public string? Account { get; private set; }
    public Uri? Server { get; private set; }
    /// <summary>Null while the server predates workspace identity, or before the first successful read.</summary>
    public WorkspaceDescriptor? Workspace { get; private set; }
    public WorkspaceRevisionResponse? Revision { get; private set; }

    public RemoteWorkspaceConnection(HttpClient http, RemoteWorkspaceCache cache, IRemoteSessionStore? sessions = null)
    {
        _http = http; _cache = cache; _sessions = sessions; _lease = cache.AcquireLease();
        try
        {
            Copy = cache.Load();
            if (Copy is { } copy) { Account = copy.Account; Server = new Uri(copy.Server); }
            // After an explicit sign-out the copy is not shown again before a new sign-in. A pending receipt stays visible.
            if (Copy is { PendingCommand: null } && !cache.IsOfflineAuthorized) Copy = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // An unreadable journal must never become permission to send a new command.
            _cacheHealthy = false;
        }
        catch { _lease.Dispose(); throw; }
    }

    public Task RestoreAsync(CancellationToken cancellationToken = default) => RunAsync(async token =>
    {
        if (!_cacheHealthy) return;
        RemoteDeviceSession? session;
        lock (_state)
        {
            ThrowIfDisposed();
            try { session = _sessions?.Load(); }
            catch (InvalidDataException) { Forget(); return; }
        }
        if (session is null) return;
        if (!ValidSession(session) || Copy is { } copy && !SameIdentity(session.Server, session.Account, copy))
        {
            Forget(); return;
        }
        var client = new RemoteWorkspaceClient(_http, new Uri(session.Server));
        client.RestoreSession(session);
        lock (_state)
        {
            ThrowIfDisposed(); _client = client; Account = session.Account; Server = new Uri(session.Server);
        }
        await RefreshCoreAsync(token).ConfigureAwait(false);
    }, cancellationToken);

    public Task ConnectSessionAsync(RemoteDeviceSession session, CancellationToken cancellationToken = default)
        => RunAsync(async token =>
        {
            if (!_cacheHealthy || Copy is { PendingCommand: not null } copy && !SameIdentity(session.Server, session.Account, copy))
                throw new InvalidOperationException("Vérifiez le reçu de la commande avant de changer de compte ou de serveur.");
            var client = new RemoteWorkspaceClient(_http, new Uri(session.Server));
            client.RestoreSession(session);
            lock (_state)
            {
                ThrowIfDisposed();
                if (Copy is { } current && !SameIdentity(session.Server, session.Account, current)) Copy = null;
                _client?.ForgetSession(); _client = client; Account = session.Account; Server = new Uri(session.Server); IsOnline = false;
                _sessions?.Clear();
            }
            await RefreshCoreAsync(token).ConfigureAwait(false);
        }, cancellationToken);

    public Task LoginAsync(Uri server, string account, string password, bool remember, CancellationToken cancellationToken = default)
        => RunAsync(async token =>
        {
            var client = new RemoteWorkspaceClient(_http, server);
            // Validate identity before any credentials are sent.
            _ = new RemoteDeviceSession(server.AbsoluteUri, account, "validation", DateTimeOffset.UtcNow);
            if (!_cacheHealthy || Copy is { PendingCommand: not null } copy && !SameIdentity(server.AbsoluteUri, account, copy))
                throw new InvalidOperationException("Vérifiez le reçu de la commande avant de changer de compte ou de serveur.");
            await client.LoginAsync(account, password, token).ConfigureAwait(false);
            var session = client.CaptureSession(account);
            lock (_state)
            {
                ThrowIfDisposed();
                if (Copy is { } current && !SameIdentity(session.Server, account, current)) Copy = null;
                _client?.ForgetSession(); _client = client; Account = account; Server = server; IsOnline = false;
                if (remember) _sessions?.Save(session); else _sessions?.Clear();
            }
            await RefreshCoreAsync(token).ConfigureAwait(false);
        }, cancellationToken);

    public Task RefreshAsync(CancellationToken cancellationToken = default) => RunAsync(RefreshCoreAsync, cancellationToken);

    private async Task RefreshCoreAsync(CancellationToken token)
    {
        var client = RequireClient();
        var clients = await client.GetClientsAsync(token).ConfigureAwait(false);
        var projects = await client.GetProjectsAsync(token).ConfigureAwait(false);
        var (workspace, revision) = await ReadIdentityAsync(client, token).ConfigureAwait(false);
        var copy = new RemoteWorkspaceCopy(Server!.AbsoluteUri, Account!, DateTimeOffset.UtcNow, clients, projects, Copy?.PendingCommand);
        lock (_state)
        {
            ThrowIfDisposed(); Save(copy); IsOnline = true; Workspace = workspace; Revision = revision;
            _cache.GrantOffline();
        }
    }

    private static async Task<(WorkspaceDescriptor?, WorkspaceRevisionResponse?)> ReadIdentityAsync(RemoteWorkspaceClient client, CancellationToken token)
    {
        try { return (await client.GetWorkspaceAsync(token).ConfigureAwait(false), await client.GetWorkspaceRevisionAsync(token).ConfigureAwait(false)); }
        // A server without these routes still serves clients and projects; the workspace is then shown as not identified.
        catch (RemoteWorkspaceException ex) when (ex.Kind is RemoteFailure.Rejected or RemoteFailure.InvalidResponse) { return (null, null); }
    }

    public Task<T> ReadAsync<T>(Func<RemoteWorkspaceClient, Task<T>> read, CancellationToken cancellationToken = default) => RunAsync(async _ =>
    {
        var result = await read(RequireClient()).ConfigureAwait(false);
        lock (_state) { ThrowIfDisposed(); }
        return result;
    }, cancellationToken);

    public Task<T> ExecuteAsync<T>(Guid id, Func<RemoteWorkspaceClient, Task<T>> send) => RunAsync(async _ =>
    {
        if (id == Guid.Empty) throw new ArgumentException("Identifiant de commande requis.", nameof(id));
        if (!CanWrite) throw new InvalidOperationException("Actualisez et vérifiez le reçu avant toute modification.");
        var client = RequireClient();
        lock (_state) { ThrowIfDisposed(); Save(Copy! with { PendingCommand = id }); }
        var result = await send(client).ConfigureAwait(false);
        lock (_state)
        {
            // Closing while a response is in flight leaves the durable journal for the next owner.
            if (!_disposed) Save(Copy! with { PendingCommand = null });
        }
        return result;
    }, default);

    public Task CheckReceiptAsync(CancellationToken cancellationToken = default) => RunAsync(async token =>
    {
        if (Copy?.PendingCommand is not { } id) return;
        var receipt = await RequireClient().GetCommandReceiptAsync(id, token).ConfigureAwait(false);
        if (receipt.CommandId != id || receipt.State != "Completed" || receipt.StatusCode is not (>= 200 and < 500)) return;
        lock (_state) { ThrowIfDisposed(); Save(Copy with { PendingCommand = null }); }
        await RefreshCoreAsync(token).ConfigureAwait(false);
    }, cancellationToken);

    public Task LogoutAsync(CancellationToken cancellationToken = default) => RunAsync(async token =>
    {
        var revoked = false;
        try
        {
            if (_client is { } client) { await client.LogoutAsync(token).ConfigureAwait(false); revoked = true; }
        }
        finally
        {
            lock (_state)
            {
                _client?.ForgetSession(); IsOnline = false; Workspace = null; Revision = null;
                if (!_disposed)
                {
                    _sessions?.Clear();
                    _cache.RevokeOffline();
                    if (revoked && _cacheHealthy && Copy is { PendingCommand: null }) { _cache.Clear(); Copy = null; }
                }
            }
        }
    }, cancellationToken);

    private void Save(RemoteWorkspaceCopy copy)
    {
        try { _cache.Save(copy); Copy = copy; _cacheHealthy = true; }
        catch { _cacheHealthy = false; IsOnline = false; throw; }
    }

    private RemoteWorkspaceClient RequireClient()
    {
        ThrowIfDisposed();
        if (_client?.IsAuthenticated != true)
        {
            Forget(); throw new RemoteWorkspaceException(RemoteFailure.Unauthorized, "Reconnectez-vous au serveur.");
        }
        return _client;
    }

    private void Forget()
    {
        lock (_state)
        {
            _client?.ForgetSession(); IsOnline = false;
            if (!_disposed) _sessions?.Clear();
        }
    }

    private static bool ValidSession(RemoteDeviceSession session) => session.ExpiresAt > DateTimeOffset.UtcNow
        && session.ExpiresAt <= DateTimeOffset.UtcNow.Add(RemoteDeviceSession.MaximumLifetime);
    private static bool SameIdentity(string server, string account, RemoteWorkspaceCopy copy)
        => new Uri(server) == new Uri(copy.Server) && string.Equals(account.Trim(), copy.Account.Trim(), StringComparison.OrdinalIgnoreCase);
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken token)
        => RunAsync(async ct => { await operation(ct).ConfigureAwait(false); return true; }, token);

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _operations.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            lock (_state) { ThrowIfDisposed(); }
            return await operation(linked.Token).ConfigureAwait(false);
        }
        catch (RemoteWorkspaceException ex)
        {
            // A receipt lookup may revoke the session while the mutation remains uncertain.
            if (ex.Kind == RemoteFailure.Unauthorized || _client?.IsAuthenticated == false) Forget();
            else IsOnline = false;
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            IsOnline = false; throw;
        }
        finally { _operations.Release(); }
    }

    public void Dispose()
    {
        lock (_state)
        {
            if (_disposed) return;
            _disposed = true; IsOnline = false; _client?.ForgetSession(); _lease.Dispose();
        }
        _lifetime.Cancel();
    }
}
