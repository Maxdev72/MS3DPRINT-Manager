using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Collections;

public sealed class CollectionCatalog
{
    public IReadOnlyList<CollectionItemSummary> Load(string workspaceRoot, string collectionFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionFolder);
        CollectionProfileStore.ValidateCategory(collectionFolder);

        var collectionPath = Path.Combine(Path.GetFullPath(workspaceRoot), collectionFolder);
        WorkspacePathSafety.EnsureNoLinks(collectionPath);
        if (!Directory.Exists(collectionPath)) return [];

        var profiles = new CollectionProfileStore(workspaceRoot).LoadAll()
            .Where(p => p.Category == collectionFolder)
            .Select(p => (Profile: p, Path: Path.GetFullPath(Path.Combine(workspaceRoot, p.RelativePath))))
            .Where(p => Directory.Exists(p.Path)).ToArray();
        var indexed = profiles.Select(p => new CollectionItemSummary(p.Profile.Name, p.Path,
            new DateTimeOffset(Directory.GetLastWriteTimeUtc(p.Path), TimeSpan.Zero), p.Profile));
        var legacy = Directory.EnumerateDirectories(collectionPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !CollectionProfileStore.IsReserved(Path.GetFileName(path)) && IsSafe(path))
            .Where(path => !profiles.Any(p => string.Equals(path, p.Path, StringComparison.OrdinalIgnoreCase)))
            .Where(path => Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).Any()
                || !profiles.Any(p => p.Path.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .Select(path => new CollectionItemSummary(Path.GetFileName(path), path, new DateTimeOffset(Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero)));
        return indexed.Concat(legacy)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSafe(string path)
    {
        try { WorkspacePathSafety.EnsureNoLinks(path); return true; }
        catch (IOException) { return false; }
    }
}

public sealed record CollectionItemSummary(string Name, string Path, DateTimeOffset LastWriteTime, CollectionProfile? Profile = null);
