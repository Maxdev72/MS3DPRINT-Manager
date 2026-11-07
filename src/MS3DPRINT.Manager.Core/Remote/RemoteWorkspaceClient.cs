using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS3DPRINT.Manager.Core.Remote;

public enum RemoteFailure { Unauthorized, Conflict, Network, Uncertain, InvalidResponse, Rejected }

public sealed class RemoteWorkspaceException(RemoteFailure kind, string message, Guid? commandId = null, Exception? inner = null)
    : Exception(message, inner)
{
    public RemoteFailure Kind { get; } = kind;
    public Guid? CommandId { get; } = commandId;
}

/// <summary>A device session kept only in memory. No mutation is retried, including after a lost response.</summary>
public sealed partial class RemoteWorkspaceClient
{
    private readonly HttpClient _http;
    private readonly Uri _server;
    private string? _token;
    private string? _account;
    private DateTimeOffset _expiresAt;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public bool IsAuthenticated => _token is not null && DateTimeOffset.UtcNow < _expiresAt;

    public RemoteWorkspaceClient(HttpClient http, Uri server)
    {
        if (!server.IsAbsoluteUri || server.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(server.UserInfo)
            || !string.IsNullOrEmpty(server.Query) || !string.IsNullOrEmpty(server.Fragment) || server.AbsolutePath != "/")
            throw new ArgumentException("Utilisez l’adresse HTTPS du serveur (sans chemin ni identifiants).", nameof(server));
        _http = http;
        _server = server;
    }

    public async Task LoginAsync(string name, string password, CancellationToken cancellationToken = default)
    {
        ForgetSession();
        var session = await SendAsync<LoginResponse>(HttpMethod.Post, "auth/login", new { name, password }, cancellationToken: cancellationToken, authenticated: false);
        if (string.IsNullOrWhiteSpace(session.AccessToken) || session.ExpiresIn <= 0 || session.ExpiresIn > 31536000)
            throw new RemoteWorkspaceException(RemoteFailure.InvalidResponse, "Réponse de connexion invalide. Mettez l’application à jour si nécessaire.");
        _account = name.Trim();
        _token = session.AccessToken;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(session.ExpiresIn);
    }

    public RemoteDeviceSession CaptureSession(string account)
    {
        if (!string.Equals(account.Trim(), _account, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Le compte ne correspond pas à la session active.", nameof(account));
        if (!IsAuthenticated || _expiresAt > DateTimeOffset.UtcNow.Add(RemoteDeviceSession.MaximumLifetime))
            throw new RemoteWorkspaceException(RemoteFailure.Unauthorized, "Session native invalide ou expirée.");
        return new RemoteDeviceSession(_server.AbsoluteUri, account, _token!, _expiresAt);
    }

    public void RestoreSession(RemoteDeviceSession session)
    {
        ForgetSession();
        if (new Uri(session.Server) != _server || session.ExpiresAt <= DateTimeOffset.UtcNow
            || session.ExpiresAt > DateTimeOffset.UtcNow.Add(RemoteDeviceSession.MaximumLifetime))
            throw new RemoteWorkspaceException(RemoteFailure.Unauthorized, "Session native invalide ou expirée.");
        _account = session.Account.Trim(); _token = session.AccessToken; _expiresAt = session.ExpiresAt;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try { if (IsAuthenticated) await SendAsync<object>(HttpMethod.Post, "auth/logout", cancellationToken: cancellationToken, readBody: false); }
        finally { ForgetSession(); }
    }

    public void ForgetSession() { _token = null; _account = null; _expiresAt = default; }
    public Task<WorkspaceDescriptor> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        => SendAsync<WorkspaceDescriptor>(HttpMethod.Get, "workspace", cancellationToken: cancellationToken);
    public Task<WorkspaceCapabilities> GetWorkspaceCapabilitiesAsync(CancellationToken cancellationToken = default)
        => SendAsync<WorkspaceCapabilities>(HttpMethod.Get, "workspace/capabilities", cancellationToken: cancellationToken);
    public Task<WorkspaceRevisionResponse> GetWorkspaceRevisionAsync(CancellationToken cancellationToken = default)
        => SendAsync<WorkspaceRevisionResponse>(HttpMethod.Get, "workspace/revision", cancellationToken: cancellationToken);
    public Task<IReadOnlyList<ClientResponse>> GetClientsAsync(CancellationToken cancellationToken = default)
        => SendAsync<IReadOnlyList<ClientResponse>>(HttpMethod.Get, "clients", cancellationToken: cancellationToken);
    public Task<IReadOnlyList<ProjectResponse>> GetProjectsAsync(CancellationToken cancellationToken = default)
        => SendAsync<IReadOnlyList<ProjectResponse>>(HttpMethod.Get, "projects", cancellationToken: cancellationToken);
    public Task<ClientDetailResponse> GetClientAsync(Guid id, CancellationToken cancellationToken = default)
        => SendAsync<ClientDetailResponse>(HttpMethod.Get, $"clients/{id}", cancellationToken: cancellationToken);
    public Task<ProjectDetailResponse> GetProjectAsync(Guid id, CancellationToken cancellationToken = default)
        => SendAsync<ProjectDetailResponse>(HttpMethod.Get, $"projects/{id}", cancellationToken: cancellationToken);
    public Task<CommandReceiptResponse> GetCommandReceiptAsync(Guid id, CancellationToken cancellationToken = default)
        => SendAsync<CommandReceiptResponse>(HttpMethod.Get, $"commands/{id}", cancellationToken: cancellationToken);
    public Task<ClientDetailResponse> CreateClientAsync(CreateClientCommand command, CancellationToken cancellationToken = default)
        => SendAsync<ClientDetailResponse>(HttpMethod.Post, "clients", command, command.CommandId, cancellationToken);
    public Task<ClientDetailResponse> UpdateClientAsync(Guid id, UpdateClientCommand command, CancellationToken cancellationToken = default)
        => SendAsync<ClientDetailResponse>(HttpMethod.Put, $"clients/{id}", command, command.CommandId, cancellationToken);
    public Task<ProjectDetailResponse> CreateProjectAsync(CreateProjectCommand command, CancellationToken cancellationToken = default)
        => SendAsync<ProjectDetailResponse>(HttpMethod.Post, "projects", command, command.CommandId, cancellationToken);
    public Task<ProjectDetailResponse> UpdateProjectAsync(Guid id, UpdateProjectCommand command, CancellationToken cancellationToken = default)
        => SendAsync<ProjectDetailResponse>(HttpMethod.Put, $"projects/{id}", command, command.CommandId, cancellationToken);
    public Task<ProjectDetailResponse> AddTaskAsync(Guid id, AddTaskCommand command, CancellationToken cancellationToken = default)
        => SendAsync<ProjectDetailResponse>(HttpMethod.Post, $"projects/{id}/tasks", command, command.CommandId, cancellationToken);
    public Task<ProjectDetailResponse> EditTaskAsync(Guid id, Guid taskId, EditTaskCommand command, CancellationToken cancellationToken = default)
        => SendAsync<ProjectDetailResponse>(HttpMethod.Put, $"projects/{id}/tasks/{taskId}", command, command.CommandId, cancellationToken);
    public Task<ProjectDetailResponse> CompleteTaskAsync(Guid id, Guid taskId, CompleteTaskCommand command, CancellationToken cancellationToken = default)
        => SendAsync<ProjectDetailResponse>(HttpMethod.Post, $"projects/{id}/tasks/{taskId}/complete", command, command.CommandId, cancellationToken);
    public Task<ProjectDetailResponse> MoveTaskAsync(Guid id, Guid taskId, MoveTaskCommand command, CancellationToken cancellationToken = default)
        => SendAsync<ProjectDetailResponse>(HttpMethod.Post, $"projects/{id}/tasks/{taskId}/move", command, command.CommandId, cancellationToken);
    public Task<ProjectDetailResponse> RemoveTaskAsync(Guid id, Guid taskId, RemoveTaskCommand command, CancellationToken cancellationToken = default)
        => SendAsync<ProjectDetailResponse>(HttpMethod.Post, $"projects/{id}/tasks/{taskId}/remove", command, command.CommandId, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, Guid? commandId = null,
        CancellationToken cancellationToken = default, bool authenticated = true, bool readBody = true)
    {
        if (authenticated && !IsAuthenticated)
        {
            ForgetSession();
            throw new RemoteWorkspaceException(RemoteFailure.Unauthorized, "Session expirée : reconnectez-vous.");
        }
        using var request = new HttpRequestMessage(method, new Uri(_server, "api/v1/" + path));
        request.Headers.Add("X-MS3DFORGE-Client", "native");
        if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ForgetSession();
                throw new RemoteWorkspaceException(RemoteFailure.Unauthorized, "Connexion refusée ou session expirée : reconnectez-vous.", commandId);
            }
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                // A 409 can also be a durable Pending command. Only its receipt proves a version conflict.
                if (commandId is { } id && !await IsRecordedConflictAsync(id, cancellationToken).ConfigureAwait(false))
                    throw new RemoteWorkspaceException(RemoteFailure.Uncertain, "Résultat incertain : vérifiez le reçu de la commande avant toute nouvelle modification.", id);
                throw new RemoteWorkspaceException(RemoteFailure.Conflict, "Conflit : le serveur a changé. Votre brouillon est conservé ; actualisez avant de réessayer.", commandId);
            }
            if (!response.IsSuccessStatusCode)
                throw new RemoteWorkspaceException(commandId.HasValue && (int)response.StatusCode >= 500 ? RemoteFailure.Uncertain : RemoteFailure.Rejected,
                    commandId.HasValue && (int)response.StatusCode >= 500 ? "Résultat incertain : vérifiez le reçu et actualisez, sans renvoyer la commande." : $"Le serveur a refusé la demande ({(int)response.StatusCode}). Vérifiez les champs et la version de l’application.", commandId);
            if (!readBody) return default!;
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("Empty response");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            throw new RemoteWorkspaceException(commandId.HasValue ? RemoteFailure.Uncertain : RemoteFailure.Network,
                commandId.HasValue ? "Réponse perdue : résultat incertain. Vérifiez le reçu et actualisez ; ne renvoyez pas la commande." : "Serveur injoignable. La dernière vue reste consultable ; actualisez pour reprendre les modifications.", commandId, ex);
        }
        catch (JsonException ex)
        {
            throw new RemoteWorkspaceException(commandId.HasValue ? RemoteFailure.Uncertain : RemoteFailure.InvalidResponse,
                "Réponse serveur incompatible. Actualisez et vérifiez la version de l’application avant toute modification.", commandId, ex);
        }
    }

    private async Task<bool> IsRecordedConflictAsync(Guid commandId, CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await GetCommandReceiptAsync(commandId, cancellationToken).ConfigureAwait(false);
            return receipt.CommandId == commandId && receipt.State == "Completed" && receipt.StatusCode == 409;
        }
        catch (RemoteWorkspaceException) { return false; }
    }

    private sealed record LoginResponse(string AccessToken, int ExpiresIn);
}
