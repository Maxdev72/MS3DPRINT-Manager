namespace MS3DPRINT.Manager.Core.Projects;

/// <summary>Finds the project folder a file belongs to, from the reference at the start of the folder name.</summary>
public static class ProjectLocator
{
    public const string PhotosFolderName = "06_PHOTOS_RENDUS";

    /// <summary>The folder named like a project (for example FA-2026-001_SAMURAI) that contains the path, or null.</summary>
    public static string? FindProjectDirectory(string path)
    {
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(path)); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
            if (ProjectReferenceFormat.TryParseFolderName(Path.GetFileName(directory).ToUpperInvariant(), out _))
                return directory;
        return null;
    }

    /// <summary>The photos and renders folder of the project that contains the path, or null outside a project.</summary>
    public static string? PhotosFolder(string path) => FindProjectDirectory(path) is { } project ? Path.Combine(project, PhotosFolderName) : null;
}
