using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MS3DPRINT.Manager.Core.Remote;

public sealed record DownloadPublication(string Kind, string FileName, string Label, long Size, string Sha256, DateTimeOffset PublishedAt);

public sealed partial class RemoteWorkspaceClient
{
    /// <summary>Replaces the installer offered on the site ("windows" or "linux") with a local file.</summary>
    public async Task<DownloadPublication> PublishDownloadAsync(string kind, string path, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            ForgetSession();
            throw new RemoteWorkspaceException(RemoteFailure.Unauthorized, "Session expirée : reconnectez-vous.");
        }
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(_server, "api/v1/downloads/" + Uri.EscapeDataString(kind))) { Content = new StreamContent(file, 81920) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentLength = file.Length;
        request.Headers.Add("X-MS3DFORGE-Client", "native");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ForgetSession();
                throw new RemoteWorkspaceException(RemoteFailure.Unauthorized, "Connexion refusée ou session expirée : reconnectez-vous.");
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new RemoteWorkspaceException(RemoteFailure.Rejected, "Le serveur ne propose pas encore les téléchargements : il doit être mis à jour.");
            if (!response.IsSuccessStatusCode)
                throw new RemoteWorkspaceException(RemoteFailure.Rejected, $"Le serveur a refusé le fichier ({(int)response.StatusCode}).");
            return await response.Content.ReadFromJsonAsync<DownloadPublication>(Json, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("Empty response");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            throw new RemoteWorkspaceException(RemoteFailure.Network, "Envoi interrompu : vérifiez la connexion Internet, puis recommencez.", inner: ex);
        }
        catch (JsonException ex)
        {
            throw new RemoteWorkspaceException(RemoteFailure.InvalidResponse, "Réponse serveur incompatible.", inner: ex);
        }
    }
}
