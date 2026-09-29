namespace MS3DPRINT.Manager.Core.Storage;

public static class StorageProviderCatalog
{
    public static IReadOnlyList<StorageProviderDefinition> Create(string nextcloudRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nextcloudRoot);

        return
        [
            new(StorageProviderKind.Nextcloud, "Nextcloud", true, nextcloudRoot),
            new(StorageProviderKind.GoogleDrive, "Google Drive", false, null),
            new(StorageProviderKind.Dropbox, "Dropbox", false, null),
            new(StorageProviderKind.LocalFolder, "Dossier local", false, null)
        ];
    }
}
