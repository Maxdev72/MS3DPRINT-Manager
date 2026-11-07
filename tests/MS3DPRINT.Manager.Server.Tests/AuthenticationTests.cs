using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task Web_login_requires_csrf_and_logout_revokes_a_copied_cookie()
    {
        using var host = new ServerFixture();
        host.CreateOwner();
        using var browser = host.Browser();
        var input = new { name = "owner", password = host.Password };
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/v1/auth/login", input)).StatusCode);
        await Csrf(browser);
        var login = await browser.PostAsJsonAsync("/api/v1/auth/login", input);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), x => x.StartsWith("__Host-ms3dforge="));
        Assert.Contains("secure", cookie.ToLowerInvariant());
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        Assert.DoesNotContain("accessToken", await login.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/auth/session")).StatusCode);
        using var stolen = host.Browser();
        stolen.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await stolen.GetAsync("/api/v1/clients")).StatusCode);
        stolen.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await stolen.GetAsync("/api/v1/clients")).StatusCode);
        stolen.DefaultRequestHeaders.Authorization = null;
        browser.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        await Csrf(browser);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stolen.GetAsync("/api/v1/clients")).StatusCode);
    }

    [Fact]
    public async Task Native_session_is_revocable_and_browser_origins_are_rejected()
    {
        using var host = new ServerFixture();
        host.CreateOwner();
        using var client = host.Browser();
        client.DefaultRequestHeaders.Add("X-MS3DFORGE-Client", "native");
        var input = new { name = "owner", password = host.Password };
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.invalid");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/login", input)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { name = "owner", password = "wrong" })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", input);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.False(login.Headers.Contains("Set-Cookie"));
        Assert.Contains("no-store", login.Headers.CacheControl!.ToString());
        var data = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = data.GetProperty("accessToken").GetString();
        Assert.Equal(30 * 24 * 3600, data.GetProperty("expiresIn").GetInt32());
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/projects")).StatusCode);
    }

    [Fact]
    public async Task Failed_logins_are_rate_limited()
    {
        using var host = new ServerFixture();
        using var client = host.Browser();
        client.DefaultRequestHeaders.Add("X-MS3DFORGE-Client", "native");
        HttpStatusCode status = 0;
        for (var i = 0; i < 21; i++)
            status = (await client.PostAsJsonAsync("/api/v1/auth/login", new { name = "owner", password = "wrong" })).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, status);
    }

    private static async Task Csrf(HttpClient browser)
    {
        var response = await browser.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        browser.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", json.GetProperty("requestToken").GetString());
    }
}
