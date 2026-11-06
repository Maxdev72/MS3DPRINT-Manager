using System.Net;
using System.Net.Http.Json;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class RemoteWorkspaceClientTests
{
    [Theory]
    [InlineData("Uncertain", null, RemoteFailure.Uncertain)]
    [InlineData("Completed", 409, RemoteFailure.Conflict)]
    [InlineData("Completed", 201, RemoteFailure.Uncertain)]
    [InlineData("Unavailable", null, RemoteFailure.Uncertain)]
    [InlineData("Unauthorized", null, RemoteFailure.Uncertain)]
    public async Task Conflict_is_classified_from_durable_receipt_without_replaying_mutation(string state, int? status, RemoteFailure expected)
    {
        var handler = new Handler(_ => Task.FromResult(Json(new { accessToken = "token", expiresIn = 3600 })));
        var client = New(handler); await client.LoginAsync("owner", "secret");
        var id = Guid.NewGuid(); var mutations = 0; var receipts = 0;
        handler.Respond = request =>
        {
            if (request.Method != HttpMethod.Get) { mutations++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)); }
            receipts++;
            Assert.EndsWith($"/commands/{id}", request.RequestUri!.AbsolutePath);
            if (state == "Unavailable") throw new HttpRequestException();
            if (state == "Unauthorized") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            return Task.FromResult(Json(new { commandId = id, state, statusCode = status, result = (object?)null }));
        };
        var error = await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.AddTaskAsync(Guid.NewGuid(), new(id, DateTimeOffset.UtcNow, "Task", null)));
        Assert.Equal(expected, error.Kind); Assert.Equal(id, error.CommandId);
        Assert.Equal(1, mutations); Assert.Equal(1, receipts);
        if (state == "Unauthorized") Assert.False(client.IsAuthenticated);
    }

    [Fact]
    public async Task Commands_use_string_enums_and_read_server_detail_versions()
    {
        var handler = new Handler(_ => Task.FromResult(Json(new { accessToken = "token", expiresIn = 3600 })));
        var client = New(handler);
        await client.LoginAsync("owner", "secret");
        var id = Guid.NewGuid(); var version = DateTimeOffset.UtcNow;
        handler.Respond = async request =>
        {
            var json = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"priority\":\"High\"", json);
            Assert.EndsWith("/projects", request.RequestUri!.AbsolutePath);
            return Json(new { id, clientId = Guid.NewGuid(), clientCode = "CF", reference = "CF-2026-001", folderName = "Projet", projectName = "Pièce", status = "InProgress", createdAt = version, updatedAt = version, priority = "High", tasks = Array.Empty<object>() });
        };
        var project = await client.CreateProjectAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Pièce", 2026, null, null, ProjectPriority.High));
        Assert.Equal(id, project.Id); Assert.Equal(version, project.UpdatedAt); Assert.Equal(ProjectPriority.High, project.Priority);
    }

    [Fact]
    public void Rejects_plain_http_before_sending_credentials()
        => Assert.Throws<ArgumentException>(() => new RemoteWorkspaceClient(new HttpClient(), new Uri("http://example.com")));

    [Fact]
    public async Task Login_uses_native_header_and_bearer_is_memory_only_and_cleared_on_401()
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal("native", request.Headers.GetValues("X-MS3DFORGE-Client").Single());
            Assert.Null(request.Headers.Authorization);
            Assert.Contains("secret", await request.Content!.ReadAsStringAsync());
            return Json(new { accessToken = "memory-token", expiresIn = 3600 });
        });
        var client = New(handler);
        await client.LoginAsync("owner", "secret");
        Assert.True(client.IsAuthenticated);
        handler.Respond = request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("memory-token", request.Headers.Authorization?.Parameter);
            Assert.DoesNotContain("token", request.RequestUri!.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        };
        var error = await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.GetClientsAsync());
        Assert.Equal(RemoteFailure.Unauthorized, error.Kind);
        Assert.False(client.IsAuthenticated);
    }

    [Theory]
    [InlineData(409, RemoteFailure.Conflict)]
    [InlineData(503, RemoteFailure.Uncertain)]
    public async Task Mutation_error_is_explicit_and_never_retried(int status, RemoteFailure expected)
    {
        var count = 0;
        var handler = new Handler(_ => Task.FromResult(Json(new { accessToken = "token", expiresIn = 3600 })));
        var client = New(handler);
        await client.LoginAsync("owner", "secret");
        var command = new AddTaskCommand(Guid.NewGuid(), DateTimeOffset.UtcNow, "Préparer", "Notes du brouillon");
        handler.Respond = request =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(Json(new { commandId = command.CommandId, state = "Completed", statusCode = 409, result = (object?)null }));
            count++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        };
        var error = await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.AddTaskAsync(Guid.NewGuid(), command));
        Assert.Equal(expected, error.Kind);
        Assert.Equal(command.CommandId, error.CommandId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Lost_mutation_response_is_uncertain_and_receipt_can_be_queried_without_replay()
    {
        var handler = new Handler(_ => Task.FromResult(Json(new { accessToken = "token", expiresIn = 3600 })));
        var client = New(handler);
        await client.LoginAsync("owner", "secret");
        var id = Guid.NewGuid();
        handler.Respond = _ => throw new HttpRequestException("connection lost");
        var error = await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.AddTaskAsync(Guid.NewGuid(), new(id, DateTimeOffset.UtcNow, "Task", null)));
        Assert.Equal(RemoteFailure.Uncertain, error.Kind);
        Assert.Equal(id, error.CommandId);
        Assert.Equal(RemoteFailure.Network, (await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.GetClientsAsync())).Kind);
        handler.Respond = request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.EndsWith($"/commands/{id}", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(new { commandId = id, state = "Uncertain", statusCode = (int?)null, result = (object?)null }));
        };
        Assert.Equal("Uncertain", (await client.GetCommandReceiptAsync(id)).State);
    }

    [Fact]
    public async Task Logout_forgets_token_even_if_network_is_lost()
    {
        var handler = new Handler(_ => Task.FromResult(Json(new { accessToken = "token", expiresIn = 3600 })));
        var client = New(handler);
        await client.LoginAsync("owner", "secret");
        handler.Respond = _ => throw new HttpRequestException();
        await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.LogoutAsync());
        Assert.False(client.IsAuthenticated);
    }

    [Fact]
    public async Task Workspace_identity_and_capabilities_are_read_from_the_authenticated_routes()
    {
        var handler = new Handler(_ => Task.FromResult(Json(new { accessToken = "token", expiresIn = 3600 })));
        var client = New(handler); await client.LoginAsync("owner", "secret");
        var id = Guid.NewGuid(); var paths = new List<string>();
        handler.Respond = request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return Task.FromResult(request.RequestUri.AbsolutePath.EndsWith("/capabilities")
                ? Json(new { schemaVersion = 1, modules = new[] { "clients" }, documentProtocol = (string?)null })
                : Json(new { workspaceId = id, accountId = "acc", name = "Atelier", schemaVersion = 1, revision = 4, documents = (object?)null }));
        };

        var workspace = await client.GetWorkspaceAsync();
        var capabilities = await client.GetWorkspaceCapabilitiesAsync();

        Assert.Equal(id, workspace.WorkspaceId);
        Assert.Equal(4, workspace.Revision);
        Assert.Equal(["clients"], capabilities.Modules);
        Assert.Equal(["/api/v1/workspace", "/api/v1/workspace/capabilities"], paths);
    }

    private static RemoteWorkspaceClient New(Handler handler) => new(new HttpClient(handler), new Uri("https://example.com/"));
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Respond(request);
    }
}
