using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.Storage;

public sealed class StorageProviderCatalogTests
{
    [Fact]
    public void Create_ExposesNextcloudAsTheOnlyAvailableProvider()
    {
        var root = @"C:\MS3DPRINT\Nextcloud\MS3DPRINT";

        var providers = StorageProviderCatalog.Create(root);

        Assert.Collection(providers,
            provider => Assert.Equal(new(StorageProviderKind.Nextcloud, "Nextcloud", true, root), provider),
            provider => Assert.Equal(new(StorageProviderKind.GoogleDrive, "Google Drive", false, null), provider),
            provider => Assert.Equal(new(StorageProviderKind.Dropbox, "Dropbox", false, null), provider),
            provider => Assert.Equal(new(StorageProviderKind.LocalFolder, "Dossier local", false, null), provider));
    }
}
