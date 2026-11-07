using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class DownloadTests
{
    [Fact]
    public async Task Installers_are_published_by_the_native_app_and_downloadable_only_with_a_session()
    {
        using var host = new ServerFixture();
        using var anonymous = host.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/downloads")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/downloads/windows")).StatusCode);
        using var native = await host.AuthenticatedClient();
        Assert.Empty(await native.GetFromJsonAsync<JsonElement[]>("/api/v1/downloads") ?? []);
        Assert.Equal(HttpStatusCode.NotFound, (await native.GetAsync("/api/v1/downloads/windows")).StatusCode);

        var notAnExe = new byte[4096]; notAnExe[0] = (byte)'X';
        Assert.Equal(HttpStatusCode.BadRequest, (await native.PutAsync("/api/v1/downloads/windows", new ByteArrayContent(notAnExe))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await native.PutAsync("/api/v1/downloads/other", new ByteArrayContent(notAnExe))).StatusCode);

        var exe = new byte[300_000]; Random.Shared.NextBytes(exe); exe[0] = (byte)'M'; exe[1] = (byte)'Z';
        var published = await native.PutAsync("/api/v1/downloads/windows", new ByteArrayContent(exe));
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var meta = await published.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(exe)), meta.GetProperty("sha256").GetString());
        Assert.Equal(exe.Length, meta.GetProperty("size").GetInt64());

        var list = await native.GetFromJsonAsync<JsonElement[]>("/api/v1/downloads");
        Assert.Equal("windows", Assert.Single(list!).GetProperty("kind").GetString());
        var download = await native.GetAsync("/api/v1/downloads/windows");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("MS3DPRINT Manager.exe", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(exe, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/downloads/windows")).StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(host.State, "downloads"), "*.upload"));
    }

    [Fact]
    public async Task The_native_client_publishes_a_linux_archive_from_disk()
    {
        using var host = new ServerFixture();
        host.CreateOwner();
        using var http = new HttpClient(host.Server.CreateHandler()) { BaseAddress = new Uri("https://localhost") };
        var client = new RemoteWorkspaceClient(http, http.BaseAddress);
        await client.LoginAsync("owner", host.Password);
        var archive = Path.Combine(host.Root, "MS3DPRINT-Manager-linux-x64.tar.gz");
        var bytes = new byte[70_000]; Random.Shared.NextBytes(bytes); bytes[0] = 0x1F; bytes[1] = 0x8B; File.WriteAllBytes(archive, bytes);
        var result = await client.PublishDownloadAsync("linux", archive);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), result.Sha256);
        Assert.Equal("MS3DPRINT-Manager-linux-x64.tar.gz", result.FileName);
        var wrong = Path.Combine(host.Root, "wrong.bin"); File.WriteAllBytes(wrong, new byte[4096]);
        Assert.Equal(RemoteFailure.Rejected, (await Assert.ThrowsAsync<RemoteWorkspaceException>(() => client.PublishDownloadAsync("windows", wrong))).Kind);
    }

    [Fact]
    public async Task A_browser_session_can_download_but_not_replace_an_installer()
    {
        using var host = new ServerFixture();
        host.CreateOwner();
        using var browser = host.Browser();
        var token = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
        browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        var login = await browser.PostAsJsonAsync("/api/v1/auth/login", new { name = "owner", password = host.Password });
        login.EnsureSuccessStatusCode();
        browser.DefaultRequestHeaders.Add("Cookie", string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/downloads")).StatusCode);
        var archive = new byte[4096]; archive[0] = 0x1F; archive[1] = 0x8B;
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PutAsync("/api/v1/downloads/linux", new ByteArrayContent(archive))).StatusCode);
    }
}
