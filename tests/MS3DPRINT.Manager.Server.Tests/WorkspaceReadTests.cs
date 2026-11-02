using System.Net;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class WorkspaceReadTests
{
    [Fact]
    public async Task Authenticated_read_returns_versions_and_tasks_without_absolute_paths()
    {
        using var host = new ServerFixture();
        using var client = await host.AuthenticatedClient();
        var paths = new WorkspaceMetadataPaths(host.Workspace);
        var now = DateTimeOffset.UtcNow;
        var owner = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "CLIENT_TEST", "CT", "Client test", null,
            null, null, null, new(null, null, null, null, null), now, now, RelativePath: host.Workspace);
        new ClientProfileStore(paths).Create(owner);
        var project = new ProjectProfile(Guid.NewGuid(), owner.Id, "CT", "CT-2026-001", "CT-2026-001_TEST", "Projet test",
            ProjectStatus.InProgress, now, null, null, null, now, RelativePath: host.Workspace,
            Tasks: [new(Guid.NewGuid(), "Imprimer")]);
        new ProjectProfileStore(paths).Create(project);
        var clients = await client.GetStringAsync("/api/v1/clients?root=" + Uri.EscapeDataString(host.State));
        var projects = await client.GetStringAsync("/api/v1/projects");
        Assert.DoesNotContain("relativePath", clients);
        Assert.DoesNotContain("relativePath", projects);
        Assert.DoesNotContain(host.Root, clients);
        Assert.DoesNotContain(host.Root, projects);
        using var clientJson = JsonDocument.Parse(clients);
        using var projectJson = JsonDocument.Parse(projects);
        Assert.Equal(owner.Id, clientJson.RootElement[0].GetProperty("id").GetGuid());
        Assert.Equal(now, projectJson.RootElement[0].GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal("Imprimer", projectJson.RootElement[0].GetProperty("tasks")[0].GetProperty("title").GetString());
        Assert.Equal("InProgress", projectJson.RootElement[0].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/state/identity.sqlite")).StatusCode);
    }

    [Fact]
    public async Task Corrupt_profile_is_reported_without_partial_success_or_internal_paths()
    {
        using var host = new ServerFixture();
        using var client = await host.AuthenticatedClient();
        var paths = new WorkspaceMetadataPaths(host.Workspace);
        paths.EnsureMetadataDirectories();
        File.WriteAllText(Path.Combine(paths.ClientsDirectory, Guid.NewGuid() + ".json"), "broken-json");
        var response = await client.GetAsync("/api/v1/clients");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(host.Root, await response.Content.ReadAsStringAsync());
    }
}
