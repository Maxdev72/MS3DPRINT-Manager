using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class WorkspaceCommandTests
{
    [Theory]
    [InlineData("AE")]
    [InlineData(" ae ")]
    [InlineData("AÉ")]
    public async Task Normalized_client_codes_are_unique_before_creating_any_folder(string code)
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        await CreateClient(http);
        var input = new { commandId = Guid.NewGuid(), kind = "Professional", clientCode = code, companyName = "Autre client", contact = new { } };
        var result = await http.PostAsJsonAsync("/api/v1/clients", input);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Single(new ClientProfileStore(new WorkspaceMetadataPaths(host.Workspace)).LoadAll());
        Assert.Single(Directory.GetDirectories(Path.Combine(host.Workspace, "01_CLIENTS")));
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/v1/clients", input)).StatusCode);
    }

    [Fact]
    public async Task Creates_real_client_and_project_with_core_reference_and_pathless_details()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        var customer = await CreateClient(http);
        Assert.Equal("Professional", customer.GetProperty("kind").GetString());
        Assert.Equal("Atelier Étoile", customer.GetProperty("companyName").GetString());
        Assert.Equal("Marie", customer.GetProperty("primaryContact").GetProperty("firstName").GetString());
        var project = await CreateProject(http, customer.GetProperty("id").GetGuid());
        Assert.Equal("AE-2026-001", project.GetProperty("reference").GetString());
        Assert.Equal("Quote", project.GetProperty("status").GetString());
        Assert.Equal("High", project.GetProperty("priority").GetString());
        Assert.True(Directory.Exists(Path.Combine(host.Workspace, "01_CLIENTS", "ATELIER_ETOILE", "AE-2026-001_PIECE_TEST")));
        var detail = await http.GetStringAsync("/api/v1/projects/" + project.GetProperty("id").GetGuid());
        Assert.DoesNotContain("relativePath", detail);
        Assert.DoesNotContain(host.Root, detail);
        var clientDetail = await http.GetStringAsync("/api/v1/clients/" + customer.GetProperty("id").GetGuid());
        Assert.DoesNotContain("relativePath", clientDetail);
        Assert.DoesNotContain(host.Root, clientDetail);
    }

    [Fact]
    public async Task Task_commands_update_order_text_completion_and_require_confirmation()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        var customer = await CreateClient(http);
        var project = await CreateProject(http, customer.GetProperty("id").GetGuid());
        var route = "/api/v1/projects/" + project.GetProperty("id").GetGuid();
        var version = project.GetProperty("updatedAt").GetDateTimeOffset();
        project = await Ok(await http.PostAsJsonAsync(route + "/tasks", new { commandId = Guid.NewGuid(), expectedUpdatedAt = version, title = "Imprimer", notes = "PLA" }));
        var task = project.GetProperty("tasks")[0].GetProperty("id").GetGuid();
        project = await Ok(await http.PutAsJsonAsync(route + "/tasks/" + task, new { commandId = Guid.NewGuid(), expectedUpdatedAt = Version(project), title = "Imprimer pièce", notes = "PETG" }));
        Assert.Equal("Imprimer pièce", project.GetProperty("tasks")[0].GetProperty("title").GetString());
        project = await Ok(await http.PostAsJsonAsync(route + "/tasks/" + task + "/complete", new { commandId = Guid.NewGuid(), expectedUpdatedAt = Version(project), completed = true }));
        Assert.True(project.GetProperty("tasks")[0].GetProperty("isCompleted").GetBoolean());
        Assert.Equal("Quote", project.GetProperty("status").GetString());
        project = await Ok(await http.PostAsJsonAsync(route + "/tasks", new { commandId = Guid.NewGuid(), expectedUpdatedAt = Version(project), title = "Expédier" }));
        project = await Ok(await http.PostAsJsonAsync(route + "/tasks/" + task + "/move", new { commandId = Guid.NewGuid(), expectedUpdatedAt = Version(project), direction = 1 }));
        Assert.Equal(task, project.GetProperty("tasks")[1].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(route + "/tasks/" + task + "/remove", new { commandId = Guid.NewGuid(), expectedUpdatedAt = Version(project), confirmed = false })).StatusCode);
        project = await Ok(await http.PostAsJsonAsync(route + "/tasks/" + task + "/remove", new { commandId = Guid.NewGuid(), expectedUpdatedAt = Version(project), confirmed = true }));
        Assert.Single(project.GetProperty("tasks").EnumerateArray());
        Assert.Equal("Expédier", project.GetProperty("tasks")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Client_and_project_updates_preserve_identity_and_refuse_stale_versions()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        using var second = host.Browser();
        second.DefaultRequestHeaders.Authorization = http.DefaultRequestHeaders.Authorization;
        var customer = await CreateClient(http);
        var clientRoute = "/api/v1/clients/" + customer.GetProperty("id").GetGuid();
        var oldVersion = Version(customer);
        var updated = await Ok(await http.PutAsJsonAsync(clientRoute, UpdateClient(oldVersion)));
        Assert.Equal("AE", updated.GetProperty("clientCode").GetString());
        Assert.Equal("ATELIER_ETOILE", updated.GetProperty("folderName").GetString());
        Assert.True(Version(updated) > oldVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await second.PutAsJsonAsync(clientRoute, UpdateClient(oldVersion))).StatusCode);
        var project = await CreateProject(http, customer.GetProperty("id").GetGuid());
        var route = "/api/v1/projects/" + project.GetProperty("id").GetGuid();
        var request = new { commandId = Guid.NewGuid(), expectedUpdatedAt = Version(project), status = "InProgress", dueDate = "2026-12-01", description = "Modifié", notes = "Note", priority = "None" };
        var changed = await Ok(await http.PutAsJsonAsync(route, request));
        Assert.Equal("InProgress", changed.GetProperty("status").GetString());
        Assert.Equal("AE-2026-001", changed.GetProperty("reference").GetString());
        Assert.Equal("Pièce test", changed.GetProperty("projectName").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await second.PostAsJsonAsync(route + "/tasks", new { commandId = Guid.NewGuid(), expectedUpdatedAt = Version(project), title = "Old" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await second.PutAsJsonAsync(route, request with { commandId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task Browser_commands_validate_csrf_before_creating_any_receipt_or_profile()
    {
        using var host = new ServerFixture();
        host.CreateOwner();
        using var http = host.Browser();
        var csrf = await http.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        http.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        (await http.PostAsJsonAsync("/api/v1/auth/login", new { name = "owner", password = host.Password })).EnsureSuccessStatusCode();
        http.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var command = ClientInput();
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/v1/clients", command)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/commands/" + command.commandId)).StatusCode);
        Assert.Equal("[]", await http.GetStringAsync("/api/v1/clients"));
        csrf = await http.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        http.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/v1/clients", command)).StatusCode);
    }

    [Fact]
    public async Task Durable_receipt_replays_exact_response_without_duplicate_and_rejects_changed_input()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        var command = ClientInput();
        var first = await http.PostAsJsonAsync("/api/v1/clients", command);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var content = await first.Content.ReadAsStringAsync();
        using var reopened = host.WithWebHostBuilder(_ => { });
        using var restartedHttp = reopened.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        restartedHttp.DefaultRequestHeaders.Authorization = http.DefaultRequestHeaders.Authorization;
        var replay = await restartedHttp.PostAsJsonAsync("/api/v1/clients", command);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(content, await replay.Content.ReadAsStringAsync());
        Assert.Single((await http.GetFromJsonAsync<JsonElement>("/api/v1/clients")).EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/v1/clients", command with { companyName = "Autre" })).StatusCode);
        var receipt = await http.GetFromJsonAsync<JsonElement>("/api/v1/commands/" + command.commandId);
        Assert.Equal("Completed", receipt.GetProperty("state").GetString());
        Assert.Equal(201, receipt.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonDocument.Parse(content).RootElement.GetProperty("id").GetGuid(), receipt.GetProperty("result").GetProperty("id").GetGuid());
        using var db = new SqliteConnection("Data Source=" + Path.Combine(host.State, "commands.sqlite") + ";Pooling=False");
        db.Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM commands WHERE state='Completed'";
        Assert.Equal(1L, query.ExecuteScalar());
    }

    [Fact]
    public async Task Pending_receipt_is_uncertain_and_never_executes_even_after_reopening_store()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        _ = await http.GetAsync("/api/v1/commands/" + Guid.NewGuid());
        var command = ClientInput();
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(host.State, "commands.sqlite") + ";Pooling=False"))
        {
            db.Open();
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO commands(command_id, request_hash, state) VALUES($id,$hash,'Pending')";
            var typed = new MS3DPRINT.Manager.Core.Remote.CreateClientCommand(command.commandId, ClientKind.Professional, "AE", "Atelier Étoile", null, null, null, null,
                new("Marie", "Dupont", "Atelier", "01", "contact@example.test", "06"));
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false) } };
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("POST\n/api/v1/clients\n" + JsonSerializer.Serialize(typed, json))));
            insert.Parameters.AddWithValue("$hash", hash);
            insert.Parameters.AddWithValue("$id", command.commandId.ToString());
            insert.ExecuteNonQuery();
        }
        using var reopened = host.WithWebHostBuilder(_ => { });
        using var restartedHttp = reopened.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        restartedHttp.DefaultRequestHeaders.Authorization = http.DefaultRequestHeaders.Authorization;
        var receipt = await restartedHttp.GetFromJsonAsync<JsonElement>("/api/v1/commands/" + command.commandId);
        Assert.Equal("Uncertain", receipt.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await restartedHttp.PostAsJsonAsync("/api/v1/clients", command)).StatusCode);
        Assert.Equal("[]", await http.GetStringAsync("/api/v1/clients"));
    }

    [Theory]
    [InlineData("missing-name")]
    [InlineData("long-contact")]
    [InlineData("empty-command")]
    [InlineData("reserved-name")]
    [InlineData("invalid-enum")]
    [InlineData("reserved-workspace")]
    public async Task Invalid_clients_do_not_create_folders(string invalid)
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        var input = ClientInput();
        input = invalid switch
        {
            "missing-name" => input with { companyName = " " },
            "long-contact" => input with { contact = new { firstName = new string('x', 501), lastName = "Dupont", role = "Atelier", phone = "01", email = "contact@example.test", mobile = "06" } },
            "empty-command" => input with { commandId = Guid.Empty },
            "reserved-name" => input with { companyName = "CON" },
            "reserved-workspace" => input with { companyName = "99 ARCHIVES" },
            _ => input with { kind = "Invalid" }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/v1/clients", input)).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(host.Workspace, "01_CLIENTS")));
    }

    [Fact]
    public async Task Missing_entities_and_unsafe_profile_paths_are_rejected_without_disclosing_paths()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/clients/" + Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/projects/" + Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/v1/clients/" + Guid.Empty)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync("/api/v1/projects", new { commandId = Guid.NewGuid(), clientId = Guid.NewGuid(), projectName = "Pièce", year = 2026, priority = "None" })).StatusCode);
        var customer = await CreateClient(http);
        var paths = new WorkspaceMetadataPaths(host.Workspace);
        var store = new ClientProfileStore(paths);
        var profile = store.Load(customer.GetProperty("id").GetGuid());
        store.Update(profile with { RelativePath = "../outside" }, profile.UpdatedAt);
        var response = await http.PostAsJsonAsync("/api/v1/projects", new { commandId = Guid.NewGuid(), clientId = profile.Id, projectName = "Pièce", year = 2026, priority = "None" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(host.Root, await response.Content.ReadAsStringAsync());
        Assert.False(Directory.Exists(Path.Combine(host.Root, "outside")));
    }

    [Fact]
    public async Task Concurrent_retries_do_not_duplicate_and_concurrent_versions_have_one_winner()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        var input = ClientInput();
        var replies = await Task.WhenAll(http.PostAsJsonAsync("/api/v1/clients", input), http.PostAsJsonAsync("/api/v1/clients", input));
        Assert.All(replies, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(await replies[0].Content.ReadAsStringAsync(), await replies[1].Content.ReadAsStringAsync());
        var customer = await replies[0].Content.ReadFromJsonAsync<JsonElement>();
        var updates = await Task.WhenAll(http.PutAsJsonAsync("/api/v1/clients/" + customer.GetProperty("id").GetGuid(), UpdateClient(Version(customer))),
            http.PutAsJsonAsync("/api/v1/clients/" + customer.GetProperty("id").GetGuid(), UpdateClient(Version(customer))));
        Assert.Single(updates, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(updates, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Single((await http.GetFromJsonAsync<JsonElement>("/api/v1/clients")).EnumerateArray());
    }

    [Fact]
    public async Task Receipt_hash_uses_typed_json_and_binds_method_and_route()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        var command = ClientInput();
        var result = await http.PostAsJsonAsync("/api/v1/clients", command);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        // Different member ordering and explicit optional nulls describe the same typed command.
        var reordered = new { contact = command.contact, notes = (string?)null, lastName = (string?)null, firstName = (string?)null, address = (string?)null,
            companyName = command.companyName, clientCode = command.clientCode, kind = command.kind, commandId = command.commandId };
        var retry = await http.PostAsJsonAsync("/api/v1/clients", reordered);
        Assert.Equal(await result.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        var customer = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Conflict, (await http.PutAsJsonAsync("/api/v1/clients/" + customer.GetProperty("id").GetGuid(),
            new { commandId = command.commandId, expectedUpdatedAt = Version(customer), kind = "Professional", companyName = "Changed", contact = command.contact })).StatusCode);
    }

    [Fact]
    public async Task Corrupt_folder_names_do_not_disclose_absolute_paths_in_details()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        var customer = await CreateClient(http);
        var store = new ClientProfileStore(new WorkspaceMetadataPaths(host.Workspace));
        var profile = store.Load(customer.GetProperty("id").GetGuid());
        store.Update(profile with { FolderName = Path.Combine(host.Root, "internal") }, profile.UpdatedAt);
        var response = await http.GetAsync("/api/v1/clients/" + profile.Id);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(host.Root, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Case_only_existing_directory_collision_is_refused_on_every_platform()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        Directory.CreateDirectory(Path.Combine(host.Workspace, "01_CLIENTS", "atelier_etoile"));
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/v1/clients", ClientInput())).StatusCode);
        Assert.Empty(new ClientProfileStore(new WorkspaceMetadataPaths(host.Workspace)).LoadAll());
    }
    [Fact]
    public async Task Invalid_task_payloads_and_missing_versions_do_not_change_the_project()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        var customer = await CreateClient(http);
        var project = await CreateProject(http, customer.GetProperty("id").GetGuid());
        var route = "/api/v1/projects/" + project.GetProperty("id").GetGuid();
        foreach (var title in new[] { " ", new string('x', 501) })
            Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(route + "/tasks", new { commandId = Guid.NewGuid(), expectedUpdatedAt = Version(project), title })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(route + "/tasks", new { commandId = Guid.NewGuid(), title = "Sans version" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync(route + "/tasks/" + Guid.NewGuid() + "/complete", new { commandId = Guid.NewGuid(), expectedUpdatedAt = Version(project), completed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(route + "/tasks/" + Guid.Empty + "/complete", new { commandId = Guid.NewGuid(), expectedUpdatedAt = Version(project), completed = true })).StatusCode);
        var unchanged = await http.GetFromJsonAsync<JsonElement>(route);
        Assert.Equal(Version(project), Version(unchanged));
        Assert.Empty(unchanged.GetProperty("tasks").EnumerateArray());
    }
    [Fact]
    public async Task New_project_template_paths_cannot_create_case_collisions_on_linux()
    {
        using var host = new ServerFixture();
        using var http = await host.AuthenticatedClient();
        var customer = await CreateClient(http);
        var template = new MS3DPRINT.Manager.Core.Templates.ProjectTemplate(
        [
            new("parts/stl", MS3DPRINT.Manager.Core.Files.ProjectFileCategory.ClientFiles),
            new("PARTS/docs")
        ]);
        new MS3DPRINT.Manager.Core.Templates.ProjectTemplateStore(new WorkspaceMetadataPaths(host.Workspace)).Save(template);
        var response = await http.PostAsJsonAsync("/api/v1/projects", new { commandId = Guid.NewGuid(), clientId = customer.GetProperty("id").GetGuid(), projectName = "Test", year = 2026, priority = "None" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(new ProjectProfileStore(new WorkspaceMetadataPaths(host.Workspace)).LoadAll());
    }
    private static DateTimeOffset Version(JsonElement item) => item.GetProperty("updatedAt").GetDateTimeOffset();

    private static async Task<JsonElement> CreateClient(HttpClient http)
    {
        var result = await http.PostAsJsonAsync("/api/v1/clients", ClientInput());
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return await result.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task<JsonElement> CreateProject(HttpClient http, Guid clientId)
    {
        var result = await http.PostAsJsonAsync("/api/v1/projects", new { commandId = Guid.NewGuid(), clientId, projectName = "Pièce test", year = 2026, dueDate = "2026-12-01", description = "Démo", priority = "High" });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return await result.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static object UpdateClient(DateTimeOffset version) => new
    {
        commandId = Guid.NewGuid(), expectedUpdatedAt = version, kind = "Professional", companyName = "Atelier changé", firstName = (string?)null, lastName = (string?)null, address = "Rue", notes = "Note",
        contact = new { firstName = "Marie", lastName = "Dupont", role = "Atelier", phone = "01", email = "contact@example.test", mobile = "06" }
    };
    private static ClientTestInput ClientInput() => new(Guid.NewGuid(), "Professional", "AE", "Atelier Étoile", new { firstName = "Marie", lastName = "Dupont", role = "Atelier", phone = "01", email = "contact@example.test", mobile = "06" });
    private sealed record ClientTestInput(Guid commandId, string kind, string clientCode, string companyName, object contact);
}
