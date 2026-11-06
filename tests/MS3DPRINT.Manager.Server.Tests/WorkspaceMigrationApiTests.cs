using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Migration;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class WorkspaceMigrationApiTests
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    internal const string Url = "/api/v1/workspace-migrations/operations";
    [Fact]
    public async Task Global_archive_hash_failure_is_blocked_and_limits_are_local()
    {
        using var host = new ServerFixture(); using var client = await host.AuthenticatedClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, new StartWorkspaceMigration(Guid.NewGuid(), 4294967297, new string('A',64)))).StatusCode);
        var id = Guid.NewGuid(); var bytes = new byte[] { 1, 2, 3 };
        (await client.PostAsJsonAsync(Url, new StartWorkspaceMigration(id, bytes.Length, new string('A',64)))).EnsureSuccessStatusCode();
        using var request = new HttpRequestMessage(HttpMethod.Put, Url + $"/{id}/parts/0") { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("X-MS3DFORGE-SHA256", Convert.ToHexString(SHA256.HashData(bytes)));
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
        var prepared = await (await client.PostAsync(Url + $"/{id}/prepare", null)).Content.ReadFromJsonAsync<WorkspaceMigrationStatus>(Json);
        Assert.Equal(WorkspaceMigrationPhase.Blocked, prepared!.Phase); Assert.Null(prepared.ReviewHash);
        Assert.Empty(Directory.EnumerateFileSystemEntries(host.Workspace));
        using var oversized = new StringContent(new string(' ',9000));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/v1/clients", oversized)).StatusCode);
    }

    [Fact]
    public async Task Capabilities_are_authenticated_and_https_only()
    {
        using var host = new ServerFixture();
        using var anonymous = host.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/workspace-migrations/capabilities")).StatusCode);
        using var client = await host.AuthenticatedClient();
        var caps = await client.GetFromJsonAsync<WorkspaceMigrationCapabilities>("/api/v1/workspace-migrations/capabilities", Json);
        Assert.Equal(new(1, 524288, 4294967296), caps);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("http://localhost/api/v1/workspace-migrations/capabilities")).StatusCode);
    }
    [Fact]
    public async Task Upload_is_private_bounded_hashed_and_idempotent()
    {
        using var host = new ServerFixture();
        using var client = await host.AuthenticatedClient();
        var bytes = new byte[524288]; RandomNumberGenerator.Fill(bytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var start = new StartWorkspaceMigration(Guid.NewGuid(), bytes.Length, hash);
        var result = await client.PostAsJsonAsync(Url, start); result.EnsureSuccessStatusCode();
        var state = (await result.Content.ReadFromJsonAsync<WorkspaceMigrationStatus>(Json))!;
        Assert.Equal(start.CommandId, state.OperationId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Url, start with { Bytes = bytes.Length - 1 })).StatusCode);
        var partUrl = Url + "/" + state.OperationId + "/parts/0";
        async Task<HttpResponseMessage> Put(byte[] data, string sha)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, partUrl) { Content = new ByteArrayContent(data) };
            request.Headers.Add("X-MS3DFORGE-SHA256", sha); return await client.SendAsync(request);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(bytes, new string('0',64))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put(bytes, hash)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put(bytes, hash)).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Put(new byte[524289], hash)).StatusCode);
        var statusText = await client.GetStringAsync(Url + "/" + state.OperationId);
        state = JsonSerializer.Deserialize<WorkspaceMigrationStatus>(statusText, Json)!;
        Assert.Equal([0], state.ReceivedParts);
        Assert.Equal(WorkspaceMigrationPhase.Uploaded, state.Phase);
        Assert.DoesNotContain(host.Root, statusText);
        Assert.Empty(Directory.EnumerateFileSystemEntries(host.Workspace));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/workspace-migrations/" + state.OperationId + "/state.json")).StatusCode);
    }
    [Fact]
    public async Task Cookie_mutations_require_csrf()
    {
        using var host = new ServerFixture(); host.CreateOwner(); using var browser = host.Browser();
        var token = await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("requestToken").GetString());
        (await browser.PostAsJsonAsync("/api/v1/auth/login", new { name="owner", password=host.Password })).EnsureSuccessStatusCode();
        browser.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync(Url,new StartWorkspaceMigration(Guid.NewGuid(),1,new string('a',64)))).StatusCode);
    }
}
