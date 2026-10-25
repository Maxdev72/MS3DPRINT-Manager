using System.Text.Json;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Files;

public sealed record RecentFile(string RelativePath, DateTimeOffset OpenedAt);

public sealed class RecentFileStore
{
    private readonly string _root;
    private readonly string _path;
    public RecentFileStore(string root)
    {
        _root = Path.GetFullPath(root);
        _path = Path.Combine(_root, ".ms3dprint-manager", "recent-files.json");
    }
    public IReadOnlyList<RecentFile> Load()
    {
        WorkspacePathSafety.EnsureNoLinks(_path);
        if (!File.Exists(_path)) return [];
        if (new FileInfo(_path).Length > 256 * 1024) throw new JsonException("L’historique des fichiers est trop volumineux.");
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var entries = JsonSerializer.Deserialize<RecentFile[]>(stream) ?? throw new JsonException("L’historique des fichiers est vide.");
        if (entries.Length > 20 || entries.Any(e => e is null || !IsManagedRelative(e.RelativePath)))
            throw new JsonException("L’historique contient un chemin invalide.");
        return entries.OrderByDescending(e => e.OpenedAt).DistinctBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public string Resolve(RecentFile file)
    {
        if (!IsManagedRelative(file.RelativePath)) throw new ArgumentException("Le fichier doit rester dans les dossiers gérés de l’espace.");
        var path = Path.GetFullPath(Path.Combine(_root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        WorkspacePathSafety.EnsureNoLinks(path);
        return path;
    }
    public void Remember(string path, DateTimeOffset openedAt)
    {
        var relative = Path.GetRelativePath(_root, Path.GetFullPath(path)).Replace('\\', '/');
        if (!IsManagedRelative(relative)) throw new ArgumentException("Seuls les fichiers des dossiers gérés peuvent être ajoutés aux récents.");
        path = Resolve(new(relative, openedAt));
        if (!File.Exists(path)) throw new FileNotFoundException("Ce fichier n’est plus disponible.", path);
        WorkspacePathSafety.EnsureNoLinks(_path);
        if (!Directory.Exists(_root)) throw new DirectoryNotFoundException("L’espace de travail est indisponible.");
        using var mutation = ProfileMutationLock.Acquire(_path);
        var entries = new[] { new RecentFile(relative, openedAt) }.Concat(Load())
            .DistinctBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(output, entries); output.Flush(flushToDisk: true); }
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static bool IsManagedRelative(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2000 || path.Contains('\\') || Path.IsPathRooted(path) || ExecutableFileGuard.IsExecutable(path)) return false;
        var parts = path.Split('/');
        return parts.Length > 1 && FolderTemplates.Main.Contains(parts[0], StringComparer.OrdinalIgnoreCase) &&
            parts.All(p => p.Length > 0 && p is not ("." or "..") && !p.EndsWith('.') && !p.EndsWith(' ') && p.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }
}
