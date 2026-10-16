namespace MS3DPRINT.Manager.Core.Storage;

public static class StorageProviderCatalog
{
    /// <summary>
    /// One entry per synchronised folder found, then one "not detected" entry for each service without any, then the local folder
    /// that holds the workspace. The software works in an ordinary folder: the clients of the services do the synchronising.
    /// </summary>
    public static IReadOnlyList<StorageProviderDefinition> Create(string workspaceRoot, IReadOnlyList<SyncFolder> detected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(detected);

        var providers = new List<StorageProviderDefinition>();
        foreach (var kind in new[] { StorageProviderKind.Nextcloud, StorageProviderKind.GoogleDrive, StorageProviderKind.Dropbox })
        {
            var folders = detected.Where(folder => folder.Kind == kind).ToArray();
            if (folders.Length == 0) providers.Add(new(kind, Name(kind), false, null));
            else providers.AddRange(folders.Select(folder => new StorageProviderDefinition(kind, Name(kind), true, folder.Path)));
        }
        providers.Add(new(StorageProviderKind.LocalFolder, Name(StorageProviderKind.LocalFolder), true, workspaceRoot));
        return providers;
    }

    public static string Name(StorageProviderKind kind) => kind switch
    {
        StorageProviderKind.Nextcloud => "Nextcloud",
        StorageProviderKind.GoogleDrive => "Google Drive",
        StorageProviderKind.Dropbox => "Dropbox",
        _ => "Dossier local"
    };

    /// <summary>True when <paramref name="path"/> is the folder or lies inside it.</summary>
    public static bool IsInside(string path, string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return full.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }
}
