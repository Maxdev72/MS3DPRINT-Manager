using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class ServerBoundaryTests
{
    [Theory]
    [InlineData("192.0.2.10", 401)]
    [InlineData("192.0.2.11", 400)]
    public async Task Only_the_explicit_proxy_can_forward_https(string peer, int expectedStatus)
    {
        using var host = new ServerFixture();
        using var configured = host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Server:TrustedProxyAddresses:0"] = "192.0.2.10" })));
        var response = await configured.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Scheme = "http";
            context.Request.Method = "GET";
            context.Request.Path = "/api/v1/clients";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });
        Assert.Equal(expectedStatus, response.Response.StatusCode);
    }

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
