using System.Net;
using System.Net.Http.Json;
using System.ComponentModel;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class RemoteWorkspaceConnectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "manager-connection-" + Guid.NewGuid().ToString("N"));
    private string CachePath => Path.Combine(_directory, "copy.json");
    private RemoteWorkspaceCache Cache => new(CachePath);
    private readonly Store _store = new();
    private readonly Handler _handler = new();
    private RemoteWorkspaceConnection New() => new(new HttpClient(_handler), Cache, _store);
    private static readonly Uri Server = new("https://example.test/");

    [Fact]
    public async Task Login_remembers_only_session_and_restores_verified_copy()
    {
        using (var connection = New())
        {
            await connection.LoginAsync(Server, "owner", "password", true);
            Assert.True(connection.CanWrite);
            Assert.Equal("owner", _store.Session!.Account);
            Assert.DoesNotContain("password", File.ReadAllText(CachePath));
            Assert.DoesNotContain("token", File.ReadAllText(CachePath));
        }
        using var restored = New();
        await restored.RestoreAsync();
        Assert.True(restored.CanWrite);
        Assert.Equal("owner", restored.Account);
        Assert.DoesNotContain("token", _store.Session!.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_or_revoked_session_preserves_copy_and_forgets_secret(bool expired)
    {
        using (var connection = New()) await connection.LoginAsync(Server, "owner", "password", true);
        if (expired) _store.Session = new(Server.AbsoluteUri, "owner", "token", DateTimeOffset.UtcNow.AddSeconds(-1));
        else _handler.Respond = _ => new(HttpStatusCode.Unauthorized);
        using var restored = New();
        if (expired) await restored.RestoreAsync();
        else await Assert.ThrowsAsync<RemoteWorkspaceException>(() => restored.RestoreAsync());
        Assert.NotNull(restored.Copy); Assert.False(restored.CanWrite); Assert.Null(_store.Session);
    }

    [Fact]
    public async Task Lost_response_journals_before_send_blocks_other_identity_and_receipt_404_cannot_clear()
    {
        using var connection = New();
        await connection.LoginAsync(Server, "owner", "password", false);
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<RemoteWorkspaceException>(() => connection.ExecuteAsync<int>(id, _ =>
        {
            Assert.Equal(id, Cache.Load()!.PendingCommand);
            throw new RemoteWorkspaceException(RemoteFailure.Uncertain, "lost", id);
        }));
        Assert.False(connection.CanWrite);
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.LoginAsync(Server, "other", "password", false));
        _handler.Respond = _ => new(HttpStatusCode.NotFound);
        await Assert.ThrowsAsync<RemoteWorkspaceException>(() => connection.CheckReceiptAsync());
        Assert.Equal(id, connection.Copy!.PendingCommand);
    }

    [Theory]
    [InlineData("Pending", 200, false)]
    [InlineData("Completed", 500, false)]
    [InlineData("Completed", 201, true)]
    [InlineData("Completed", 409, true)]
    public async Task Only_exact_completed_final_receipt_clears_journal(string state, int status, bool clears)
    {
        using var connection = New(); await connection.LoginAsync(Server, "owner", "password", false);
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<IOException>(() => connection.ExecuteAsync<int>(id, _ => throw new IOException()));
        _handler.Respond = request => request.RequestUri!.AbsolutePath.Contains("commands/")
            ? Json(new CommandReceiptResponse(id, state, status, null)) : Json(Array.Empty<object>());
        await connection.CheckReceiptAsync();
        Assert.Equal(clears ? null : id, connection.Copy!.PendingCommand);
        Assert.Equal(clears ? null : id, Cache.Load()!.PendingCommand);
    }

    [Fact]
    public async Task Closing_during_send_keeps_journal_and_ignores_late_success()
    {
        var connection = New(); await connection.LoginAsync(Server, "owner", "password", false);
        var id = Guid.NewGuid(); var completion = new TaskCompletionSource<int>();
        var sending = connection.ExecuteAsync(id, _ => completion.Task);
        Assert.Equal(id, Cache.Load()!.PendingCommand);
        connection.Dispose(); completion.SetResult(42); await sending;
        Assert.Equal(id, Cache.Load()!.PendingCommand);
        using var next = New(); await next.RestoreAsync(); Assert.Equal(id, next.Copy!.PendingCommand);
    }

    [Fact]
    public async Task Network_failure_preserves_good_copy_and_logout_forgets_vault()
    {
        using var connection = New(); await connection.LoginAsync(Server, "owner", "password", true);
        var copy = connection.Copy;
        _handler.Respond = _ => throw new HttpRequestException();
        await Assert.ThrowsAsync<RemoteWorkspaceException>(() => connection.RefreshAsync());
        Assert.Same(copy, connection.Copy); Assert.False(connection.CanWrite);
        await Assert.ThrowsAsync<RemoteWorkspaceException>(() => connection.LogoutAsync());
        Assert.Null(_store.Session); Assert.False(connection.IsAuthenticated); Assert.NotNull(Cache.Load());
    }

    [Fact]
    public async Task After_an_explicit_sign_out_the_kept_copy_is_not_shown_offline_before_a_new_sign_in()
    {
        using (var connection = New())
        {
            await connection.LoginAsync(Server, "owner", "password", true);
            _handler.Respond = _ => throw new HttpRequestException();
            await Assert.ThrowsAsync<RemoteWorkspaceException>(() => connection.LogoutAsync());
        }
        Assert.NotNull(Cache.Load());
        using (var offline = New())
        {
            Assert.Null(offline.Copy);
            Assert.Equal("connexion requise", MS3DPRINT.Manager.Desktop.MainWindow.StartupLabel(offline));
        }

        _handler.Respond = new Handler().Respond;
        using (var again = New()) await again.LoginAsync(Server, "owner", "password", false);
        using var reopened = New();
        Assert.NotNull(reopened.Copy);
        Assert.Equal("hors connexion · consultation seule", MS3DPRINT.Manager.Desktop.MainWindow.StartupLabel(reopened));
    }

    [Fact]
    public async Task The_workspace_identity_and_verified_revision_are_read_with_the_data()
    {
        var id = Guid.NewGuid();
        _handler.Respond = request => request.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("auth/login") => Json(new { accessToken = "token", expiresIn = 28800 }),
            var p when p.EndsWith("workspace") => Json(new { workspaceId = id, accountId = "acc", name = "Atelier MS3D", schemaVersion = 1, revision = 7, documents = (object?)null }),
            var p when p.EndsWith("workspace/revision") => Json(new { revision = 7, verified = true }),
            _ => Json(Array.Empty<object>())
        };
        using var connection = New();
        await connection.LoginAsync(Server, "owner", "password", false);

        Assert.Equal(id, connection.Workspace!.WorkspaceId);
        Assert.True(connection.Revision!.Verified);
        Assert.Equal("connecté · données à jour · documents Nextcloud non associés", MS3DPRINT.Manager.Desktop.MainWindow.StartupLabel(connection));
    }

    [Fact]
    public async Task A_server_without_workspace_identity_still_serves_its_data()
    {
        _handler.Respond = request => request.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("auth/login") => Json(new { accessToken = "token", expiresIn = 28800 }),
            var p when p.Contains("/workspace") => new(HttpStatusCode.NotFound),
            _ => Json(Array.Empty<object>())
        };
        using var connection = New();
        await connection.LoginAsync(Server, "owner", "password", false);

        Assert.True(connection.CanWrite);
        Assert.Null(connection.Workspace);
        Assert.Contains("atelier non identifié", MS3DPRINT.Manager.Desktop.MainWindow.StartupLabel(connection));
    }

    [Theory]
    [InlineData("https://other.test/", "owner")]
    [InlineData("https://example.test/", "other")]
    public async Task Vault_identity_must_match_cached_journal_before_any_request(string server, string account)
    {
        using (var connection = New()) await connection.LoginAsync(Server, "owner", "password", true);
        _store.Session = new(server, account, "token", DateTimeOffset.UtcNow.AddHours(1));
        _handler.Respond = _ => throw new InvalidOperationException("No request allowed");
        using var restored = New(); await restored.RestoreAsync();
        Assert.NotNull(restored.Copy); Assert.Null(_store.Session); Assert.False(restored.IsAuthenticated);
    }

    [Fact]
    public async Task Corrupt_journal_cannot_be_overwritten_by_login()
    {
        Directory.CreateDirectory(_directory); File.WriteAllText(CachePath, "not-json");
        using var connection = New();
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.LoginAsync(Server, "owner", "password", false));
        Assert.Equal("not-json", File.ReadAllText(CachePath)); Assert.False(connection.CanWrite);
    }

    [Fact]
    public async Task Failed_same_identity_refresh_retains_good_copy_and_pending_receipt()
    {
        using var connection = New(); await connection.LoginAsync(Server, "owner", "password", true);
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<IOException>(() => connection.ExecuteAsync<int>(id, _ => throw new IOException()));
        var copy = connection.Copy;
        _handler.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("auth/login")
            ? Json(new { accessToken = "new-token", expiresIn = 28800 }) : throw new HttpRequestException();
        await Assert.ThrowsAsync<RemoteWorkspaceException>(() => connection.LoginAsync(Server, "owner", "password", true));
        Assert.Same(copy, connection.Copy); Assert.Equal(id, connection.Copy!.PendingCommand); Assert.False(connection.CanWrite);
    }

    [Fact]
    public async Task Journal_write_failure_prevents_send()
    {
        using var connection = New(); await connection.LoginAsync(Server, "owner", "password", false);
        using var locked = File.Open(CachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var sent = false;
        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => connection.ExecuteAsync(Guid.NewGuid(), _ => { sent = true; return Task.FromResult(1); }));
        Assert.False(sent); Assert.False(connection.CanWrite);
    }

    [Fact]
    public async Task Acknowledgement_cache_failure_preserves_durable_pending_command()
    {
        using var connection = New(); await connection.LoginAsync(Server, "owner", "password", false);
        var id = Guid.NewGuid(); FileStream? locked = null;
        try
        {
            await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => connection.ExecuteAsync(id, _ =>
            {
                locked = File.Open(CachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Task.FromResult(1);
            }));
            Assert.Equal(id, Cache.Load()!.PendingCommand); Assert.Equal(id, connection.Copy!.PendingCommand); Assert.False(connection.CanWrite);
        }
        finally { locked?.Dispose(); }
    }

    [Fact]
    public async Task Wrong_receipt_id_cannot_clear_pending_command()
    {
        using var connection = New(); await connection.LoginAsync(Server, "owner", "password", false);
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<IOException>(() => connection.ExecuteAsync<int>(id, _ => throw new IOException()));
        _handler.Respond = _ => Json(new CommandReceiptResponse(Guid.NewGuid(), "Completed", 201, null));
        await connection.CheckReceiptAsync(); Assert.Equal(id, Cache.Load()!.PendingCommand);
    }

    [Fact]
    public async Task Command_and_refresh_serialize_and_queued_operations_never_run_after_close()
    {
        var connection = New(); await connection.LoginAsync(Server, "owner", "password", false);
        var completion = new TaskCompletionSource<int>();
        var sending = connection.ExecuteAsync(Guid.NewGuid(), _ => completion.Task);
        var reading = connection.ReadAsync<int>(_ => throw new InvalidOperationException("Queued read must not run"));
        var refreshing = connection.RefreshAsync();
        Assert.False(refreshing.IsCompleted);
        connection.Dispose(); completion.SetResult(1); await sending;
        var refreshError = await Assert.ThrowsAnyAsync<Exception>(() => refreshing);
        var readError = await Assert.ThrowsAnyAsync<Exception>(() => reading);
        Assert.True(refreshError is OperationCanceledException or ObjectDisposedException);
        Assert.True(readError is OperationCanceledException or ObjectDisposedException);
        Assert.NotNull(Cache.Load()!.PendingCommand);
    }

    [Fact]
    public async Task Detail_unauthorized_forgets_persisted_session_and_blocks_writes()
    {
        using var connection = New(); await connection.LoginAsync(Server, "owner", "password", true);
        _handler.Respond = _ => new(HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<RemoteWorkspaceException>(() => connection.ReadAsync(client => client.GetClientAsync(Guid.NewGuid())));
        Assert.Null(_store.Session); Assert.False(connection.IsAuthenticated); Assert.False(connection.CanWrite);
    }

    [Fact]
    public async Task Unauthorized_conflict_receipt_forgets_vault_without_losing_pending_command()
    {
        using var connection = New(); await connection.LoginAsync(Server, "owner", "password", true);
        var id = Guid.NewGuid();
        _handler.Respond = request => new(request.Method == HttpMethod.Get ? HttpStatusCode.Unauthorized : HttpStatusCode.Conflict);
        var error = await Assert.ThrowsAsync<RemoteWorkspaceException>(() => connection.ExecuteAsync(id,
            client => client.AddTaskAsync(Guid.NewGuid(), new(id, DateTimeOffset.UtcNow, "Task", null))));
        Assert.Equal(RemoteFailure.Uncertain, error.Kind);
        Assert.Null(_store.Session); Assert.False(connection.IsAuthenticated);
        Assert.Equal(id, Cache.Load()!.PendingCommand);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_response_cannot_change_replacement_owners_journal_or_vault(bool unauthorized)
    {
        var previous = New(); await previous.LoginAsync(Server, "owner", "password", true);
        var oldId = Guid.NewGuid(); var completion = new TaskCompletionSource<int>();
        var sending = previous.ExecuteAsync(oldId, _ => completion.Task);
        previous.Dispose();
        using var next = New(); await next.LoginAsync(Server, "owner", "password", true);
        _handler.Respond = request => request.RequestUri!.AbsolutePath.Contains("commands/")
            ? Json(new CommandReceiptResponse(oldId, "Completed", 201, null)) : Json(Array.Empty<object>());
        await next.CheckReceiptAsync();
        var newId = Guid.NewGuid();
        await Assert.ThrowsAsync<IOException>(() => next.ExecuteAsync<int>(newId, _ => throw new IOException()));
        var savedSession = _store.Session;
        if (unauthorized)
        {
            completion.SetException(new RemoteWorkspaceException(RemoteFailure.Unauthorized, "late revoked"));
            await Assert.ThrowsAsync<RemoteWorkspaceException>(() => sending);
        }
        else { completion.SetResult(1); await sending; }
        Assert.Equal(newId, Cache.Load()!.PendingCommand); Assert.Same(savedSession, _store.Session);
    }

    [Fact]
    public async Task Corrupt_vault_is_forgotten_and_cache_remains_readable()
    {
        using (var connection = New()) await connection.LoginAsync(Server, "owner", "password", true);
        _store.LoadError = new InvalidDataException("corrupt vault");
        using var restored = New();
        await restored.RestoreAsync();
        Assert.Null(_store.Session); Assert.NotNull(restored.Copy); Assert.False(restored.CanWrite);
    }

    [Theory]
    [InlineData("load")]
    [InlineData("save")]
    [InlineData("clear")]
    public async Task Native_vault_failure_preserves_copy_and_blocks_writes(string operation)
    {
        using (var initial = New()) await initial.LoginAsync(Server, "owner", "password", true);
        var remembered = _store.Session;
        using var connection = New();
        if (operation == "clear") await connection.RestoreAsync();
        var failure = new Win32Exception(1312);
        if (operation == "load") _store.LoadError = failure;
        if (operation == "save") _store.SaveError = failure;
        if (operation == "clear") _store.ClearError = failure;
        await Assert.ThrowsAsync<Win32Exception>(() => operation switch
        {
            "load" => connection.RestoreAsync(),
            "save" => connection.LoginAsync(Server, "owner", "password", true),
            _ => connection.LogoutAsync()
        });
        Assert.NotNull(connection.Copy); Assert.NotNull(Cache.Load()); Assert.False(connection.CanWrite);
        Assert.Same(remembered, _store.Session);
    }

    [Fact]
    public async Task Cancelled_read_does_not_invoke_delegate_or_change_copy()
    {
        using var connection = New(); await connection.LoginAsync(Server, "owner", "password", false);
        var copy = connection.Copy; var invoked = false;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.ReadAsync(_ =>
        { invoked = true; return Task.FromResult(1); }, cancelled.Token));
        Assert.False(invoked); Assert.Same(copy, connection.Copy);
    }

    [Fact]
    public async Task Pending_legacy_uppercase_account_can_reauthenticate_same_owner()
    {
        using (var connection = New())
        {
            await connection.LoginAsync(Server, "OWNER", "password", true);
            await Assert.ThrowsAsync<IOException>(() => connection.ExecuteAsync<int>(Guid.NewGuid(), _ => throw new IOException()));
        }
        using var restored = New();
        await restored.LoginAsync(Server, "owner", "password", false);
        Assert.NotNull(restored.Copy!.PendingCommand); Assert.False(restored.CanWrite);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed class Store : IRemoteSessionStore
    {
        public RemoteDeviceSession? Session;
        public Exception? LoadError; public RemoteDeviceSession? Load() => LoadError is { } error ? throw error : Session;
        public Exception? SaveError, ClearError;
        public void Save(RemoteDeviceSession session) { if (SaveError is { } error) throw error; Session = session; }
        public void Clear() { if (ClearError is { } error) throw error; Session = null; }
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = request => request.RequestUri!.AbsolutePath.EndsWith("auth/login")
            ? Json(new { accessToken = "token", expiresIn = 28800 }) : Json(Array.Empty<object>());
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Respond(request));
        protected override void Dispose(bool disposing) { }
    }
}



