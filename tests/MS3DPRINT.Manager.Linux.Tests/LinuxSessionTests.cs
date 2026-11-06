using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Linux;

namespace MS3DPRINT.Manager.Linux.Tests;

public sealed class LinuxSessionTests
{
    [Fact]
    public void Missing_keyring_never_saves_a_plaintext_fallback()
    {
        if (!OperatingSystem.IsLinux()) return;
        var store = new LinuxRemoteSessionStore("/not-installed/ms3d-secret-tool");
        Assert.Null(store.Load());
        Assert.Throws<IOException>(() => store.Save(Session()));
        store.Clear();
    }

    [Fact]
    public void Session_uses_standard_input_and_the_keyring_can_be_cleared()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("ms3d-keyring-test-").FullName;
        try
        {
            var executable = Path.Combine(directory, "secret-tool-test");
            File.WriteAllText(executable, "#!/bin/sh\ncd -- \"$(dirname -- \"$0\")\"\nprintf '%s\\n' \"$@\" > arguments\ncase \"$1\" in\n store) cat > payload ;;\n lookup) test -f payload || exit 1; cat payload ;;\n clear) rm -f payload ;;\nesac\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var store = new LinuxRemoteSessionStore(executable, "isolated-test");
            var session = Session();
            store.Save(session);
            Assert.DoesNotContain(session.AccessToken, File.ReadAllText(Path.Combine(directory, "arguments")));
            Assert.Equal(session.AccessToken, store.Load()!.AccessToken);
            Assert.Equal(session.Account, store.Load()!.Account);
            File.WriteAllText(Path.Combine(directory, "payload"), "invalid json");
            Assert.Throws<InvalidDataException>(() => store.Load());
            store.Clear();
            Assert.Null(store.Load());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void A_locked_keyring_reports_failure_instead_of_an_empty_session()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("ms3d-keyring-error-").FullName;
        try
        {
            var executable = Path.Combine(directory, "locked");
            File.WriteAllText(executable, "#!/bin/sh\necho locked >&2\nexit 1\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Assert.Throws<IOException>(() => new LinuxRemoteSessionStore(executable).Load());
        }
        finally { Directory.Delete(directory, true); }
    }

    private static RemoteDeviceSession Session() => new("https://example.test/", "test", "synthetic-token-not-a-real-secret", DateTimeOffset.UtcNow.AddHours(1));
}
