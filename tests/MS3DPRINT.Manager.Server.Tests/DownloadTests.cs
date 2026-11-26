using System.Net;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class DownloadTests
{
    [Fact]
    public async Task No_session_can_replace_or_serve_an_installer_any_more()
    {
        // The installers come from the GitHub releases the apps update from; the server keeps none.
        using var host = new ServerFixture();
        using var native = await host.AuthenticatedClient();
        var exe = new byte[300_000]; exe[0] = (byte)'M'; exe[1] = (byte)'Z';
        Assert.NotEqual(HttpStatusCode.OK, (await native.PutAsync("/api/v1/downloads/windows", new ByteArrayContent(exe))).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await native.GetAsync("/api/v1/downloads/windows")).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(host.State, "downloads")));
    }
}
