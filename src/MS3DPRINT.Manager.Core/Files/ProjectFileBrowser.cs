namespace MS3DPRINT.Manager.Core.Files;

public sealed class ProjectFileBrowser
{
    public IReadOnlyList<ProjectFileEntry> List(string projectRoot, string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDirectory));
        EnsureInsideProject(root, current);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Le dossier projet est introuvable.");
        if (!Directory.Exists(current)) throw new DirectoryNotFoundException("Le dossier sélectionné est introuvable.");

        return Directory.EnumerateFileSystemEntries(current, "*", SearchOption.TopDirectoryOnly)
            .Select(CreateEntry)
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string GetParentDirectory(string projectRoot, string currentDirectory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDirectory));
        EnsureInsideProject(root, current);
        return string.Equals(root, current, PathComparison()) ? root : Directory.GetParent(current)?.FullName ?? root;
    }

    private static ProjectFileEntry CreateEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var lastWriteTime = isDirectory ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path);
        return new ProjectFileEntry(Path.GetFileName(path), path, isDirectory,
            isDirectory ? null : new FileInfo(path).Length, new DateTimeOffset(lastWriteTime, TimeSpan.Zero));
    }

    private static void EnsureInsideProject(string root, string current)
    {
        var relative = Path.GetRelativePath(root, current);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new UnauthorizedAccessException("Le dossier sélectionné doit rester dans le projet.");
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
