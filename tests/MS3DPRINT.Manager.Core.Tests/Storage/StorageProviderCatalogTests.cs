using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.Storage;

public sealed class StorageProviderCatalogTests
{
    private const string Workspace = @"C:\Atelier\MS3DPRINT";

    [Fact]
    public void Nothing_detected_leaves_the_three_services_unavailable_and_the_local_folder_in_use()
    {
        var providers = StorageProviderCatalog.Create(Workspace, []);

        Assert.Collection(providers,
            provider => Assert.Equal(new(StorageProviderKind.Nextcloud, "Nextcloud", false, null), provider),
            provider => Assert.Equal(new(StorageProviderKind.GoogleDrive, "Google Drive", false, null), provider),
            provider => Assert.Equal(new(StorageProviderKind.Dropbox, "Dropbox", false, null), provider),
            provider => Assert.Equal(new(StorageProviderKind.LocalFolder, "Dossier local", true, Workspace), provider));
    }

    [Fact]
    public void Each_detected_folder_is_listed_and_a_service_without_one_is_not()
    {
        var providers = StorageProviderCatalog.Create(Workspace,
        [
            new SyncFolder(StorageProviderKind.Nextcloud, @"C:\Users\a\Nextcloud"),
            new SyncFolder(StorageProviderKind.Nextcloud, @"D:\Cloud"),
            new SyncFolder(StorageProviderKind.Dropbox, @"C:\Users\a\Dropbox")
        ]);

        Assert.Equal(
        [
            (StorageProviderKind.Nextcloud, true, @"C:\Users\a\Nextcloud"),
            (StorageProviderKind.Nextcloud, true, @"D:\Cloud"),
            (StorageProviderKind.GoogleDrive, false, (string?)null),
            (StorageProviderKind.Dropbox, true, @"C:\Users\a\Dropbox"),
            (StorageProviderKind.LocalFolder, true, Workspace)
        ], providers.Select(provider => (provider.Kind, provider.IsAvailable, provider.RootPath)).ToArray());
    }

    [Theory]
    [InlineData(@"C:\Users\a\Nextcloud\MS3DPRINT", @"C:\Users\a\Nextcloud", true)]
    [InlineData(@"C:\Users\a\Nextcloud", @"C:\Users\a\Nextcloud\", true)]
    [InlineData(@"c:\users\a\NEXTCLOUD\MS3DPRINT", @"C:\Users\a\Nextcloud", true)]
    [InlineData(@"C:\Users\a\Nextcloud2\MS3DPRINT", @"C:\Users\a\Nextcloud", false)]
    [InlineData(@"C:\Atelier\MS3DPRINT", @"C:\Users\a\Nextcloud", false)]
    public void IsInside_tells_whether_a_path_lies_in_a_folder(string path, string folder, bool expected)
        => Assert.Equal(expected, StorageProviderCatalog.IsInside(path, folder));
}
