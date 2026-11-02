using System.Net;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class WebSurfaceTests
{
    [Fact]
    public async Task Login_shell_is_public_but_uses_local_assets_and_keeps_api_private()
    {
        using var host = new ServerFixture();
        using var client = host.Browser();
        var page = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("lang=\"fr\"", await page.Content.ReadAsStringAsync());
        Assert.Contains("frame-ancestors 'none'", Assert.Single(page.Headers.GetValues("Content-Security-Policy")));
        Assert.Contains("script-src 'self'", Assert.Single(page.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app.css")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/clients")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/.ms3dprint-manager/clients/test.json")).StatusCode);
    }
}
