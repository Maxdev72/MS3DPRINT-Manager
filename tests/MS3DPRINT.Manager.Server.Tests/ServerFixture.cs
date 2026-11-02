using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Text.Json;
using MS3DPRINT.Manager.Server.Authentication;
using ServerProgram = MS3DPRINT.Manager.Server.Program;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class ServerFixture : WebApplicationFactory<ServerProgram>
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ms3dforge-tests-" + Guid.NewGuid().ToString("N"));
    public string Workspace => Path.Combine(Root, "workspace");
    public string State => Path.Combine(Root, "state");
    public string Password { get; } = Guid.NewGuid() + "-OwnerSecret!";

    public void CreateOwner()
    {
        _ = Server;
        var settings = new ServerStorage(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Server:StateRoot"] = State, ["Server:WorkspaceRoot"] = Workspace
        }).Build());
        new SessionStore(settings, TimeProvider.System).CreateOwner("owner", Password);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Workspace);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Server:StateRoot"] = State,
            ["Server:WorkspaceRoot"] = Workspace
        }));
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    public async Task<HttpClient> AuthenticatedClient()
    {
        CreateOwner();
        var client = Browser();
        client.DefaultRequestHeaders.Add("X-MS3DFORGE-Client", "native");
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { name = "owner", password = Password });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
