using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Windows.Remote;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class WindowsRemoteSessionStoreTests
{
    [Fact]
    public void Unique_test_credential_roundtrips_unicode_and_clear_is_idempotent()
    {
        var target = "MS3DPRINT.Manager.Tests/" + Guid.NewGuid().ToString("N");
        var store = new WindowsRemoteSessionStore(target);
        try
        {
            Assert.Null(store.Load());
            var session = new RemoteDeviceSession("https://example.test/", "propriétaire", "test-secret-é-" + Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));
            store.Save(session);
            var loaded = store.Load();
            Assert.NotNull(loaded); Assert.Equal(session.Server, loaded.Server); Assert.Equal(session.Account, loaded.Account);
            Assert.Equal(session.AccessToken, loaded.AccessToken); Assert.Equal(session.ExpiresAt, loaded.ExpiresAt);
            store.Clear(); store.Clear(); Assert.Null(store.Load());
        }
        finally { store.Clear(); }
    }
}
