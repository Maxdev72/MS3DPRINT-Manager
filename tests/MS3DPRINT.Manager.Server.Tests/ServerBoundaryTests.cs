using System.Net;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class ServerBoundaryTests
{
    [Fact]
    public async Task Health_is_minimal_and_private_routes_require_login()
    {
        using var host = new ServerFixture();
        using var client = host.Browser();
        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.DoesNotContain(host.Root, await health.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/clients")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/auth/register", null)).StatusCode);
    }

    [Fact]
    public async Task Non_health_requests_require_https()
    {
        using var host = new ServerFixture();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/clients")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }
}
