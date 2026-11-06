using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Migration;
namespace MS3DPRINT.Manager.Core.Remote;

public sealed partial class RemoteWorkspaceClient
{
    public async Task<WorkspaceMigrationCapabilities> GetMigrationCapabilitiesAsync(CancellationToken token = default)
    {
        var result = await MigrationSendAsync<WorkspaceMigrationCapabilities>(HttpMethod.Get, "capabilities", token: token);
        if (result.Version != 1 || result.ChunkBytes != 524288 || result.MaxArchiveBytes is <= 0 or > 4294967296)
            throw new RemoteWorkspaceException(RemoteFailure.InvalidResponse, "Capacité de reprise incompatible : mettre à jour le serveur et Manager.");
        return result;
    }
    public Task<WorkspaceMigrationStatus> StartMigrationAsync(StartWorkspaceMigration command, CancellationToken token = default)
        => MigrationSendAsync<WorkspaceMigrationStatus>(HttpMethod.Post, "operations", command, token: token);
    public Task<WorkspaceMigrationStatus> GetMigrationStatusAsync(Guid id, CancellationToken token = default)
        => MigrationSendAsync<WorkspaceMigrationStatus>(HttpMethod.Get, $"operations/{id}", token: token);
    public Task<WorkspaceMigrationStatus> PrepareMigrationAsync(Guid id, CancellationToken token = default)
        => MigrationSendAsync<WorkspaceMigrationStatus>(HttpMethod.Post, $"operations/{id}/prepare", token: token);
    public Task<WorkspaceMigrationStatus> SetMigrationChoicesAsync(Guid id, IReadOnlyList<WorkspaceReconciliationChoice> choices, CancellationToken token = default)
        => MigrationSendAsync<WorkspaceMigrationStatus>(HttpMethod.Put, $"operations/{id}/choices", choices, token: token);
    public Task<WorkspaceMigrationStatus> ApplyMigrationAsync(Guid id, ApplyWorkspaceMigration command, CancellationToken token = default)
        => MigrationSendAsync<WorkspaceMigrationStatus>(HttpMethod.Post, $"operations/{id}/apply", command, uncertain: true, token: token);

    public async Task<WorkspaceMigrationStatus> UploadMigrationAsync(string archive, WorkspaceMigrationStatus status,
        IProgress<long>? progress = null, CancellationToken token = default)
    {
        if (status.ChunkBytes != 524288 || status.ArchiveBytes is <= 0 or > 4294967296) throw new InvalidDataException("Limites de transfert invalides.");
        using var input = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (input.Length != status.ArchiveBytes || !Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(status.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("L’archive locale a changé.");
        var parts = checked((int)((input.Length + status.ChunkBytes - 1) / status.ChunkBytes));
        if (status.ReceivedParts.Distinct().Count() != status.ReceivedParts.Length || status.ReceivedParts.Any(i => i < 0 || i >= parts)) throw new InvalidDataException("Morceaux serveur invalides.");
        var identity = status;
        var received = status.ReceivedParts.ToHashSet();
        long sent = received.Sum(i => Math.Min((long)status.ChunkBytes, input.Length - (long)i * status.ChunkBytes));
        progress?.Report(sent);
        var buffer = new byte[status.ChunkBytes];
        for (var index = 0; index < parts; index++)
        {
            token.ThrowIfCancellationRequested();
            if (received.Contains(index)) continue;
            input.Position = (long)index * status.ChunkBytes;
            var count = (int)Math.Min(buffer.Length, input.Length - input.Position);
            await input.ReadExactlyAsync(buffer.AsMemory(0, count), token);
            using var content = new ByteArrayContent(buffer, 0, count);
            var hash = Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, count)));
            status = await MigrationSendAsync<WorkspaceMigrationStatus>(HttpMethod.Put, $"operations/{status.OperationId}/parts/{index}", content: content, hash: hash, token: token);
            if (status.OperationId != identity.OperationId || status.ArchiveBytes != identity.ArchiveBytes || status.ArchiveSha256 != identity.ArchiveSha256 || status.ChunkBytes != identity.ChunkBytes || status.ReceivedParts is null) throw new InvalidDataException("Identité du transfert incohérente.");
            if (!status.ReceivedParts.Contains(index)) throw new InvalidDataException("Morceau non confirmé par le serveur.");
            sent += count; progress?.Report(sent);
        }
        return status;
    }

    private async Task<T> MigrationSendAsync<T>(HttpMethod method, string path, object? body = null,
        HttpContent? content = null, string? hash = null, bool uncertain = false, CancellationToken token = default)
    {
        if (!IsAuthenticated) throw new RemoteWorkspaceException(RemoteFailure.Unauthorized, "Reconnectez-vous au même compte pour reprendre.");
        using var request = new HttpRequestMessage(method, new Uri(_server, "api/v1/workspace-migrations/" + path));
        request.Headers.Add("X-MS3DFORGE-Client", "native");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (hash is not null) request.Headers.Add("X-MS3DFORGE-SHA256", hash);
        request.Content = content ?? (body is null ? null : JsonContent.Create(body, options: Json));
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized) { ForgetSession(); throw new RemoteWorkspaceException(RemoteFailure.Unauthorized, "Session expirée : reconnectez-vous au même compte."); }
            if (response.StatusCode == HttpStatusCode.NotFound && path == "capabilities") throw new RemoteWorkspaceException(RemoteFailure.Rejected, "Avant toute reprise, mettre à jour le serveur : cette version ne propose pas l’assistant.");
            if (!response.IsSuccessStatusCode) throw new RemoteWorkspaceException(uncertain ? RemoteFailure.Uncertain : RemoteFailure.Rejected,
                uncertain ? "Activation incertaine : consulter uniquement l’état de la reprise, sans renvoyer l’activation." : $"Reprise refusée ({(int)response.StatusCode}). Les sauvegardes sont conservées.");
            var result = await response.Content.ReadFromJsonAsync<T>(Json, token).ConfigureAwait(false) ?? throw new JsonException();
            if (result is WorkspaceMigrationStatus status && (status.OperationId == Guid.Empty || !Enum.IsDefined(status.Phase)
                || status.ArchiveBytes is <= 0 or > 4294967296 || status.ChunkBytes != 524288
                || status.ArchiveSha256 is not { Length: 64 } || !status.ArchiveSha256.All(Uri.IsHexDigit)
                || status.ReceivedParts is null || status.Choices is null || status.Issues is null
                || status.Choices.Any(c => c is null || c.Path is null || !Enum.IsDefined(c.Choice))
                || status.Issues.Any(i => i is null || i.Code is null || i.Path is null || i.Message is null)
                || status.Plan is { } plan && (plan.Files is null || plan.Directories is null || plan.Issues is null
                    || plan.Files.Any(f => f is null || f.Path is null || f.DisplayName is null || !Enum.IsDefined(f.Kind))
                    || plan.Directories.Any(d => d is null) || plan.Issues.Any(i => i is null))))
                throw new JsonException("Invalid migration status");
            return result;
        }
        catch (JsonException e)
        { throw new RemoteWorkspaceException(uncertain ? RemoteFailure.Uncertain : RemoteFailure.InvalidResponse, "Réponse de reprise incompatible. Vérifiez la version du serveur avant de continuer.", inner: e); }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
        { throw new RemoteWorkspaceException(uncertain ? RemoteFailure.Uncertain : RemoteFailure.Network, uncertain ? "Réponse d’activation perdue : vérifier uniquement l’état de la reprise." : "Transfert interrompu. Reconnectez-vous au même serveur et compte pour reprendre.", inner: e); }
    }
}
