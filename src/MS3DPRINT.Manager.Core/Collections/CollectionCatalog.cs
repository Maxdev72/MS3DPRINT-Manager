namespace MS3DPRINT.Manager.Core.Collections;

public sealed class CollectionCatalog
{
    public IReadOnlyList<CollectionItemSummary> Load(string workspaceRoot, string collectionFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionFolder);

        var collectionPath = Path.Combine(Path.GetFullPath(workspaceRoot), collectionFolder);
        if (!Directory.Exists(collectionPath)) return [];

        return Directory.EnumerateDirectories(collectionPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith(".MS3DPRINT-STAGING-", StringComparison.OrdinalIgnoreCase))
            .Select(path => new CollectionItemSummary(
                Path.GetFileName(path),
                path,
                new DateTimeOffset(Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero)))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed record CollectionItemSummary(string Name, string Path, DateTimeOffset LastWriteTime);
