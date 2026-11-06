using System.Net;
using System.Net.Http.Json;
using MS3DPRINT.Manager.Core.Remote;
namespace MS3DPRINT.Manager.Core.Tests;
public sealed class RemoteWorkspaceMigrationClientTests
{
    [Fact]
    public async Task Old_server_stops_before_any_upload()
    {
        var calls = new List<string>();
        using var http = new HttpClient(new Handler(r => { calls.Add(r.RequestUri!.AbsolutePath); return Task.FromResult(r.RequestUri.AbsolutePath.EndsWith("login") ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { accessToken = "secret", expiresIn = 3600 }) } : new HttpResponseMessage(HttpStatusCode.NotFound)); }));
        var client = new RemoteWorkspaceClient(http, new Uri("https://example.test"));
        await client.LoginAsync("owner", "password");
        var error = await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.GetMigrationCapabilitiesAsync());
        Assert.Contains("mettre à jour", error.Message);
        Assert.Equal(2, calls.Count);
    }
    [Fact]
    public async Task Malformed_status_is_rejected_as_invalid_response_without_crashing()
    {
        var client = new RemoteWorkspaceClient(new HttpClient(new Handler(r => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = JsonContent.Create(r.RequestUri!.AbsolutePath.EndsWith("login") ? (object)new { accessToken = "token", expiresIn = 3600 } : new { operationId = Guid.NewGuid() }) }))), new Uri("https://example.test"));
        await client.LoginAsync("owner", "secret");
        var error = await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.GetMigrationStatusAsync(Guid.NewGuid()));
        Assert.Equal(RemoteFailure.InvalidResponse, error.Kind);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request); }
}
