using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Storage;

public sealed class AccountWorkspacePathsTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-account-paths-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _id = Guid.NewGuid();

    [Fact]
    public void The_same_identity_always_gets_the_same_private_partition()
    {
        var first = new AccountWorkspacePaths(_root, new(new Uri("https://one.example"), "owner", _id));
        var again = new AccountWorkspacePaths(_root + Path.DirectorySeparatorChar, new(new Uri("https://ONE.example/"), "owner", _id));

        Assert.Equal(first.DirectoryPath, again.DirectoryPath);
        Assert.StartsWith(Path.GetFullPath(_root), first.DirectoryPath);
        Assert.Equal(Path.Combine(first.DirectoryPath, "copy.json"), first.CachePath);
        Assert.Equal(Path.Combine(first.DirectoryPath, "binding.json"), first.BindingPath);
    }

    [Fact]
    public void Another_account_server_or_workspace_gets_another_partition()
    {
        var first = new AccountWorkspacePaths(_root, new(new Uri("https://one.example"), "owner", _id));

        Assert.NotEqual(first.CachePath, new AccountWorkspacePaths(_root, new(new Uri("https://one.example"), "other", _id)).CachePath);
        Assert.NotEqual(first.CachePath, new AccountWorkspacePaths(_root, new(new Uri("https://two.example"), "owner", _id)).CachePath);
        Assert.NotEqual(first.CachePath, new AccountWorkspacePaths(_root, new(new Uri("https://one.example"), "owner", Guid.NewGuid())).CachePath);
    }

    [Fact]
    public void The_separator_prevents_two_identities_from_sharing_a_key()
    {
        var a = new AccountWorkspacePaths(_root, new(new Uri("https://one.example"), "ab", _id));
        var b = new AccountWorkspacePaths(_root, new(new Uri("https://one.example"), "a" + "\0" + "b", _id));
        Assert.NotEqual(a.DirectoryPath, b.DirectoryPath);
    }

    [Theory]
    [InlineData("http://one.example")]
    [InlineData("https://one.example/sub/")]
    [InlineData("https://user:pw@one.example")]
    [InlineData("https://one.example/?q=1")]
    [InlineData("https://one.example/#f")]
    public void Invalid_servers_are_refused(string server)
        => Assert.Throws<ArgumentException>(() => new AccountWorkspacePaths(_root, new(new Uri(server), "owner", _id)));

    [Fact]
    public void An_empty_account_or_workspace_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new AccountWorkspacePaths(_root, new(new Uri("https://one.example"), " ", _id)));
        Assert.Throws<ArgumentException>(() => new AccountWorkspacePaths(_root, new(new Uri("https://one.example"), "owner", Guid.Empty)));
    }

    [Fact]
    public void The_stable_settings_directory_does_not_depend_on_the_installation_folder()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        Assert.Equal(Path.Combine(local, "MS3DPRINT Manager", "data"), ApplicationPaths.DataDirectory);
        Assert.False(ApplicationPaths.DataDirectory.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Metadata_can_live_outside_the_documents_root_while_the_historical_layout_is_kept()
    {
        var documents = Path.Combine(_root, "docs");
        var metadata = Path.Combine(_root, "meta");

        var historical = new WorkspaceMetadataPaths(documents);
        var explicitRoot = new WorkspaceMetadataPaths(documents, metadata);

        Assert.Equal(Path.Combine(Path.GetFullPath(documents), ".ms3dprint-manager"), historical.MetadataDirectory);
        Assert.Equal(Path.GetFullPath(documents), explicitRoot.RootPath);
        Assert.Equal(Path.GetFullPath(metadata), explicitRoot.MetadataDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(metadata), "clients"), explicitRoot.ClientsDirectory);
    }
}
