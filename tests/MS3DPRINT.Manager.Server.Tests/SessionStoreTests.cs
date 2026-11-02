using System.Text;
using Microsoft.Extensions.Configuration;
using MS3DPRINT.Manager.Server.Authentication;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class SessionStoreTests
{
    [Fact]
    public void Credentials_and_sessions_are_persistent_hashed_and_revocable()
    {
        using var host = new ServerFixture();
        Directory.CreateDirectory(host.Workspace);
        var settings = Storage(host);
        var clock = new TestClock();
        var store = new SessionStore(settings, clock);
        var password = Guid.NewGuid() + "-Secret!";
        store.CreateOwner("owner", password);
        Assert.True(store.VerifyOwner("owner", password));
        Assert.False(store.VerifyOwner("owner", "wrong-password"));
        Assert.False(store.VerifyOwner("other", password));
        Assert.Throws<InvalidOperationException>(() => store.CreateOwner("other", password));
        var token = store.CreateSession();
        var reopened = new SessionStore(settings, clock);
        Assert.True(reopened.IsActive(token));
        Assert.False(reopened.IsActive(token + "x"));
        foreach (var file in Directory.GetFiles(host.State))
        {
            var contents = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            Assert.DoesNotContain(password, contents);
            Assert.DoesNotContain(token, contents);
        }
        reopened.Revoke(token);
        Assert.False(store.IsActive(token));
        var expired = store.CreateSession();
        clock.Now = clock.Now.AddHours(8);
        Assert.False(reopened.IsActive(expired));
    }

    [Fact]
    public void Weak_credentials_are_rejected_and_a_session_requires_an_owner()
    {
        using var host = new ServerFixture();
        Directory.CreateDirectory(host.Workspace);
        var store = new SessionStore(Storage(host), TimeProvider.System);
        Assert.Throws<InvalidOperationException>(() => store.CreateSession());
        Assert.Throws<ArgumentException>(() => store.CreateOwner("owner", "short"));
        Assert.Throws<ArgumentException>(() => store.CreateOwner("", new string('a', 20)));
    }

    private static ServerStorage Storage(ServerFixture host) => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Server:StateRoot"] = host.State, ["Server:WorkspaceRoot"] = host.Workspace }).Build());

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
