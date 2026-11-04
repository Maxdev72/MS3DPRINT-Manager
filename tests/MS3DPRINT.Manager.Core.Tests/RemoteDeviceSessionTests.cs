using System.Net;
using System.Net.Http.Json;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class RemoteDeviceSessionTests
{
    [Fact]
    public async Task Captured_session_cannot_be_relabelled_as_another_account()
    {
        var client = new RemoteWorkspaceClient(new HttpClient(new LoginHandler()), new("https://example.test/"));
        await client.LoginAsync("owner", "password");
        Assert.Throws<ArgumentException>(() => client.CaptureSession("other"));
        var captured = client.CaptureSession("owner");
        var restored = new RemoteWorkspaceClient(new HttpClient(new LoginHandler()), new("https://example.test/"));
        restored.RestoreSession(captured);
        Assert.True(restored.IsAuthenticated);
        Assert.Throws<ArgumentException>(() => restored.CaptureSession("other"));
    }

    [Theory]
    [InlineData("https://different.test/", 1)]
    [InlineData("https://example.test/", -1)]
    [InlineData("https://example.test/", 9)]
    public void Session_destination_expiry_and_native_eight_hour_limit_are_verified(string server, int hours)
    {
        var client = new RemoteWorkspaceClient(new HttpClient(new LoginHandler()), new("https://example.test/"));
        Assert.Throws<RemoteWorkspaceException>(() => client.RestoreSession(new(server, "owner", "token", DateTimeOffset.UtcNow.AddHours(hours))));
        Assert.False(client.IsAuthenticated);
    }

    private sealed class LoginHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { accessToken = "token", expiresIn = 28800 }) });
    }
}
