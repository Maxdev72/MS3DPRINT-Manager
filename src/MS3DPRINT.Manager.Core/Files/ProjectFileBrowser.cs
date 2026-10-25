using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Files;

public sealed class ProjectFileBrowser
{
    public IReadOnlyList<ProjectFileEntry> List(string projectRoot, string currentDirectory)
        => Enumerate(projectRoot, currentDirectory).OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Unsorted, cancellable read. File metadata comes from the directory enumeration.</summary>
    public IEnumerable<ProjectFileEntry> Enumerate(string projectRoot, string currentDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDirectory));
        EnsureInsideProject(root, current);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Le dossier projet est introuvable.");
        if (!Directory.Exists(current)) throw new DirectoryNotFoundException("Le dossier sélectionné est introuvable.");

        foreach (var info in new DirectoryInfo(current).EnumerateFileSystemInfos("*", new EnumerationOptions
                 { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Ancestors have already been checked. Only reparse entries need a separate tag check;
            // this still permits Cloud Files placeholders and excludes links and junctions.
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 && !IsSafe(info.FullName)) continue;
            var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
            yield return new(info.Name, info.FullName, isDirectory, isDirectory ? null : ((FileInfo)info).Length,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public string GetParentDirectory(string projectRoot, string currentDirectory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDirectory));
        EnsureInsideProject(root, current);
        return string.Equals(root, current, PathComparison()) ? root : Directory.GetParent(current)?.FullName ?? root;
    }

    private static void EnsureInsideProject(string root, string current)
    {
        var relative = Path.GetRelativePath(root, current);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new UnauthorizedAccessException("Le dossier sélectionné doit rester dans le projet.");
        WorkspacePathSafety.EnsureNoLinks(root);
        WorkspacePathSafety.EnsureNoLinks(current);
    }

    private static bool IsSafe(string path)
    {
        try { WorkspacePathSafety.EnsureNoLinks(path); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
