using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class WorkspaceIdentityApiTests
{
    [Theory]
    [InlineData("/api/v1/workspace")]
    [InlineData("/api/v1/workspace/capabilities")]
    [InlineData("/api/v1/workspace/revision")]
    public async Task Anonymous_requests_are_refused(string route)
    {
        using var host = new ServerFixture();
        using var anonymous = host.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task The_workspace_identity_is_stable_and_survives_a_restart()
    {
        using var host = new ServerFixture();
        using var owner = await host.AuthenticatedClient();
        var one = await owner.GetFromJsonAsync<WorkspaceDescriptor>("/api/v1/workspace");
        var two = await owner.GetFromJsonAsync<WorkspaceDescriptor>("/api/v1/workspace");
        Assert.NotNull(one);
        Assert.NotEqual(Guid.Empty, one.WorkspaceId);
        Assert.False(string.IsNullOrWhiteSpace(one.AccountId));
        Assert.Equal(one.WorkspaceId, two!.WorkspaceId);
        Assert.Equal(one.AccountId, two.AccountId);

        using var restarted = host.WithWebHostBuilder(_ => { });
        using var again = restarted.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        again.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        var three = await again.GetFromJsonAsync<WorkspaceDescriptor>("/api/v1/workspace");
        Assert.Equal(one.WorkspaceId, three!.WorkspaceId);
        Assert.Equal(one.AccountId, three.AccountId);
    }

    [Fact]
    public async Task Responses_expose_neither_host_paths_nor_secrets()
    {
        using var host = new ServerFixture();
        using var owner = await host.AuthenticatedClient();
        foreach (var route in new[] { "/api/v1/workspace", "/api/v1/workspace/capabilities", "/api/v1/workspace/revision" })
        {
            var body = await owner.GetStringAsync(route);
            Assert.DoesNotContain(host.Root.Replace("\\", "\\\\"), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(host.Root, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(host.Password, body);
            Assert.DoesNotContain(owner.DefaultRequestHeaders.Authorization!.Parameter!, body);
        }
    }

    [Fact]
    public async Task Capabilities_list_only_the_modules_actually_served()
    {
        using var host = new ServerFixture();
        using var owner = await host.AuthenticatedClient();
        var capabilities = await owner.GetFromJsonAsync<WorkspaceCapabilities>("/api/v1/workspace/capabilities");
        Assert.Equal(["clients", "projects", "tasks"], capabilities!.Modules);
        Assert.Null(capabilities.DocumentProtocol);
        Assert.True(capabilities.SchemaVersion >= 1);
    }

    [Fact]
    public async Task The_revision_moves_with_acknowledged_commands_only()
    {
        using var host = new ServerFixture();
        using var owner = await host.AuthenticatedClient();
        var before = await owner.GetFromJsonAsync<WorkspaceRevisionResponse>("/api/v1/workspace/revision");
        Assert.True(before!.Verified);

        var created = await owner.PostAsJsonAsync("/api/v1/clients", WorkspaceCommandTests.ClientInput());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var after = await owner.GetFromJsonAsync<WorkspaceRevisionResponse>("/api/v1/workspace/revision");
        Assert.True(after!.Revision > before.Revision);
        Assert.True(after.Verified);

        var refused = await owner.PostAsJsonAsync("/api/v1/clients", new { commandId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(after.Revision, (await owner.GetFromJsonAsync<WorkspaceRevisionResponse>("/api/v1/workspace/revision"))!.Revision);
    }

    [Fact]
    public async Task A_cut_between_the_records_and_the_receipt_is_never_reported_as_up_to_date()
    {
        using var host = new ServerFixture();
        using var owner = await host.AuthenticatedClient();
        var before = await owner.GetFromJsonAsync<WorkspaceRevisionResponse>("/api/v1/workspace/revision");
        var id = Guid.NewGuid();
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(host.State, "commands.sqlite") + ";Pooling=False"))
        {
            db.Open();
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO commands(command_id,request_hash,state) VALUES($id,'cut','Pending')";
            insert.Parameters.AddWithValue("$id", id.ToString());
            insert.ExecuteNonQuery();
        }

        var revision = await owner.GetFromJsonAsync<WorkspaceRevisionResponse>("/api/v1/workspace/revision");
        Assert.False(revision!.Verified);
        Assert.Equal(before!.Revision, revision.Revision);
        var receipt = await owner.GetFromJsonAsync<CommandReceiptResponse>("/api/v1/commands/" + id);
        Assert.Equal("Uncertain", receipt!.State);
    }

    [Fact]
    public async Task A_restart_moves_the_revision_so_offline_changes_are_never_assumed_known()
    {
        using var host = new ServerFixture();
        using var owner = await host.AuthenticatedClient();
        var before = await owner.GetFromJsonAsync<WorkspaceRevisionResponse>("/api/v1/workspace/revision");
        using var restarted = host.WithWebHostBuilder(_ => { });
        using var again = restarted.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        again.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        var after = await again.GetFromJsonAsync<WorkspaceRevisionResponse>("/api/v1/workspace/revision");
        Assert.True(after!.Revision > before!.Revision);
    }
}
