using System.ComponentModel;
using System.Runtime.InteropServices;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.Core.Storage;

public sealed class FolderTreeService
{
    private readonly Func<Guid> _newGuid;

    public FolderTreeService() : this(Guid.NewGuid) { }

    public FolderTreeService(Func<Guid> newGuid)
    {
        _newGuid = newGuid ?? throw new ArgumentNullException(nameof(newGuid));
    }

    public FolderCreationResult EnsureMainStructure(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var rootPath = Path.GetFullPath(root);
        if (File.Exists(rootPath))
        {
            throw Conflict(rootPath);
        }

        foreach (var folder in FolderTemplates.Main)
        {
            var path = Path.Combine(rootPath, folder);
            if (File.Exists(path))
            {
                throw Conflict(path);
            }
        }

        Directory.CreateDirectory(rootPath);
        var created = new List<string>();
        foreach (var folder in FolderTemplates.Main)
        {
            var path = Path.Combine(rootPath, folder);
            if (File.Exists(path))
            {
                throw Conflict(path);
            }

            if (Directory.Exists(path))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(path);
            }
            catch (IOException exception) when (PathExists(path))
            {
                throw Conflict(path, exception);
            }

            created.Add(folder);
        }

        return new FolderCreationResult(rootPath, created.AsReadOnly());
    }

    public FolderCreationResult CreateTree(string destination, IReadOnlyList<string> template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(template);
        var destinationPath = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(destinationPath);
        if (parent is null || destinationPath == Path.GetPathRoot(destinationPath))
        {
            throw new ArgumentException("Le dossier cible doit avoir un dossier parent.", nameof(destination));
        }

        var segments = template.Select(SplitAndValidate).ToArray();
        if (PathExists(destinationPath))
        {
            throw Conflict(destinationPath);
        }

        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".MS3DPRINT-STAGING-" + _newGuid().ToString("N"));
        var createdStagingDirectories = new List<string>();
        var moved = false;
        try
        {
            CreateNewStagingDirectory(staging);
            createdStagingDirectories.Add(staging);
            foreach (var parts in segments)
            {
                var current = staging;
                foreach (var part in parts)
                {
                    current = Path.Combine(current, part);
                    if (!Directory.Exists(current))
                    {
                        Directory.CreateDirectory(current);
                        createdStagingDirectories.Add(current);
                    }
                }
            }

            try
            {
                Directory.Move(staging, destinationPath);
                moved = true;
            }
            catch (IOException exception) when (PathExists(destinationPath))
            {
                throw Conflict(destinationPath, exception);
            }
        }
        finally
        {
            if (!moved)
            {
                for (var index = createdStagingDirectories.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        Directory.Delete(createdStagingDirectories[index], recursive: false);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        return new FolderCreationResult(destinationPath, template.ToArray());
    }

    private static string[] SplitAndValidate(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var parts = folder.Split(new[] { '/', '\\' }, StringSplitOptions.None);
        if (Path.IsPathRooted(folder) || parts.Any(part => part.Length == 0 || part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new ArgumentException("Le modèle contient un chemin de dossier invalide.", nameof(folder));
        }

        return parts;
    }

    private static bool PathExists(string path) => Directory.Exists(path) || File.Exists(path);

    private static void CreateNewStagingDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // Directory.CreateDirectory also succeeds for an existing directory.
            if (!CreateDirectoryWindows(path, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error is 80 or 183)
                {
                    throw Conflict(path);
                }

                throw new Win32Exception(error);
            }

            return;
        }

        if (PathExists(path))
        {
            throw Conflict(path);
        }

        Directory.CreateDirectory(path);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectoryWindows(string path, IntPtr securityAttributes);

    private static FolderConflictException Conflict(string path, Exception? innerException = null)
        => innerException is null
            ? new FolderConflictException($"Un fichier ou dossier existe déjà à cet emplacement : {path}")
            : new FolderConflictException($"Un fichier ou dossier existe déjà à cet emplacement : {path}", innerException);
}
