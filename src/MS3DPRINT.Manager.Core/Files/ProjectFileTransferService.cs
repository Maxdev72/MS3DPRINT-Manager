using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Files;

public sealed class ProjectFileTransferService
{
    public string Move(string sourcePath, string projectPath, ProjectFileDestination destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(destination);

        var source = Path.GetFullPath(sourcePath);
        var project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        var targetDirectory = ResolveInsideProject(project, destination.RelativeDirectory);
        var target = ResolveFileInsideDirectory(targetDirectory, destination.FileName);

        // Exists hides access and cloud-provider errors; retain the actual OS failure.
        try
        {
            var attributes = File.GetAttributes(source);
            if ((attributes & FileAttributes.Directory) != 0)
                throw new FileNotFoundException("Le fichier source est introuvable.", source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProjectFileTransferException("File.GetAttributes (source)", source, target, exception);
        }
        if (!Directory.Exists(project)) throw new DirectoryNotFoundException("Le dossier projet est introuvable.");
        if (!Directory.Exists(targetDirectory)) throw new DirectoryNotFoundException("Le dossier projet cible est introuvable.");
        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new FolderConflictException($"Un fichier ou dossier existe déjà à cet emplacement : {target}");
        }

        try
        {
            File.Move(source, target, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProjectFileTransferException("File.Move", source, target, exception);
        }
        return target;
    }

    private static string ResolveInsideProject(string projectPath, string relativeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDirectory);
        if (Path.IsPathRooted(relativeDirectory)) throw new ArgumentException("Le dossier cible doit être relatif au projet.", nameof(relativeDirectory));

        var targetDirectory = Path.GetFullPath(Path.Combine(projectPath, relativeDirectory));
        var relative = Path.GetRelativePath(projectPath, targetDirectory);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new ArgumentException("Le dossier cible doit rester dans le projet.", nameof(relativeDirectory));
        }

        return targetDirectory;
    }

    private static string ResolveFileInsideDirectory(string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Le nom de fichier cible est invalide.", nameof(fileName));
        }

        return Path.Combine(directory, fileName);
    }
}
