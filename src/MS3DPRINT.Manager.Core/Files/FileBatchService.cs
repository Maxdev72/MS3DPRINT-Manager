using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Files;

public enum FileBatchOperation { Move, Trash }
public sealed record FileBatchFailure(string Path, string Message);
public sealed record FileBatchResult(IReadOnlyList<string> Completed, IReadOnlyList<FileBatchFailure> Failures);

/// <summary>Independent, non-overwriting operations on a frozen list of immediate files.</summary>
public sealed class FileBatchService
{
    public FileBatchResult Execute(string root, string directory, IReadOnlyList<string> paths, FileBatchOperation operation, string? destination = null)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var service = new EntityManagementService(root);
        var completed = new List<string>(); var failures = new List<FileBatchFailure>();
        foreach (var path in paths.Distinct(comparer))
        {
            try
            {
                var source = Path.GetFullPath(path);
                if (!string.Equals(Path.GetDirectoryName(source), parent, comparison))
                    throw new IOException("Le fichier ne se trouve plus dans le dossier sélectionné.");
                if (!File.Exists(source) || Directory.Exists(source)) throw new IOException("Le fichier n’est plus disponible.");
                if (operation == FileBatchOperation.Move)
                    service.MovePath(source, destination ?? throw new ArgumentException("Choisissez un dossier de destination."));
                else service.TrashPath(source, Path.GetFileName(source));
                completed.Add(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { failures.Add(new(path, exception.Message)); }
        }
        return new(completed, failures);
    }
}
