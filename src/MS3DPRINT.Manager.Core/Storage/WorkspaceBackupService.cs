using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Storage;

public sealed record WorkspaceBackupPreview(DateTimeOffset CreatedAt, bool IncludesDocuments, int FileCount, long TotalBytes, string ArchiveSha256);

public sealed class WorkspaceBackupService
{
    private const int MaxEntries = 100_000;
    private const long MaxBytes = 512L * 1024 * 1024 * 1024;
    private const string ManifestName = "ms3dprint-backup.json";
    private static readonly HashSet<string> Roots = new(FolderTemplates.Main.Append(".ms3dprint-manager"), StringComparer.OrdinalIgnoreCase);
    private sealed record SavedFile(string Path, long Bytes, string Sha256);
    private sealed record Manifest(int Version, DateTimeOffset CreatedAt, bool IncludesDocuments, string[] Directories, SavedFile[] Files);
    private sealed record Source(string Path, long Bytes, DateTime Modified);
    private sealed record Inventory(string[] Directories, Source[] Files);

    public WorkspaceBackupPreview Create(string root, string archivePath, bool includeDocuments)
    {
        root = Path.GetFullPath(root); archivePath = Path.GetFullPath(archivePath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("L’espace de travail est introuvable.");
        if (Inside(archivePath, root)) throw new IOException("Choisissez un emplacement de sauvegarde en dehors de l’espace de travail.");
        WorkspacePathSafety.EnsureNoLinks(root); WorkspacePathSafety.EnsureNoLinks(archivePath);
        if (File.Exists(archivePath) || Directory.Exists(archivePath)) throw new IOException("Une archive existe déjà à cet emplacement. Choisissez un autre nom.");
        var inventory = Scan(root, includeDocuments);
        if (inventory.Files.Length == 0) throw new IOException("Aucune donnée à sauvegarder dans les dossiers gérés de cet espace.");
        var temporary = archivePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var files = new List<SavedFile>();
        var created = DateTimeOffset.Now;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var file in inventory.Files)
                    {
                        var source = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
                        WorkspacePathSafety.EnsureNoLinks(source);
                        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (input.Length != file.Bytes || File.GetLastWriteTimeUtc(source) != file.Modified) throw Changed();
                        var entry = zip.CreateEntry("workspace/" + file.Path, CompressionLevel.Fastest);
                        using var content = entry.Open();
                        var hash = CopyAndHash(input, content, file.Bytes);
                        files.Add(new(file.Path, file.Bytes, hash));
                    }
                    var current = Scan(root, includeDocuments);
                    if (!inventory.Directories.SequenceEqual(current.Directories) || !inventory.Files.SequenceEqual(current.Files)) throw Changed();
                    var entryManifest = zip.CreateEntry(ManifestName);
                    using var json = entryManifest.Open();
                    JsonSerializer.Serialize(json, new Manifest(1, created, includeDocuments, inventory.Directories, files.ToArray()));
                }
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, archivePath); // Never overwrite an existing archive.
            using var saved = Open(archivePath);
            return new(created, includeDocuments, files.Count, files.Sum(f => f.Bytes), Convert.ToHexString(SHA256.HashData(saved)));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public WorkspaceBackupPreview Inspect(string archivePath)
    {
        WorkspacePathSafety.EnsureNoLinks(archivePath);
        using var input = Open(archivePath);
        return Read(input).Preview;
    }

    public void Restore(string archivePath, string destination, string expectedArchiveSha256)
    {
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var parent = Path.GetDirectoryName(destination) ?? throw new IOException("Choisissez un nouveau sous-dossier.");
        WorkspacePathSafety.EnsureNoLinks(archivePath); WorkspacePathSafety.EnsureNoLinks(destination);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Le dossier parent est introuvable.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("La restauration exige un nouveau dossier. Aucun fichier existant ne sera remplacé.");
        using var input = Open(archivePath); // Prevent changing/replacing the ZIP between verification and extraction.
        var (manifest, preview) = Read(input);
        if (!string.Equals(preview.ArchiveSha256, expectedArchiveSha256, StringComparison.Ordinal))
            throw new InvalidDataException("L’archive a changé depuis l’aperçu. Sélectionnez-la à nouveau.");
        var staging = Path.Combine(parent, ".ms3d-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var directory in manifest.Directories) Directory.CreateDirectory(Path.Combine(staging, directory.Replace('/', Path.DirectorySeparatorChar)));
            input.Position = 0;
            using (var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true))
            {
                var entries = zip.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
                foreach (var file in manifest.Files)
                {
                    var target = Path.Combine(staging, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var source = entries["workspace/" + file.Path].Open();
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    if (CopyAndHash(source, output, file.Bytes) != file.Sha256) throw new InvalidDataException("Un fichier de l’archive est altéré.");
                }
            }
            WorkspacePathSafety.EnsureNoLinks(destination);
            Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private static (Manifest Manifest, WorkspaceBackupPreview Preview) Read(FileStream input)
    {
        input.Position = 0;
        var archiveHash = Convert.ToHexString(SHA256.HashData(input)); input.Position = 0;
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > MaxEntries + 1) throw new InvalidDataException("Cette archive contient trop de fichiers.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
            if (!names.Add(entry.FullName)) throw new InvalidDataException("L’archive contient des chemins en double.");
        var jsonEntry = zip.GetEntry(ManifestName) ?? throw new InvalidDataException("Ce fichier n’est pas une sauvegarde MS3DPRINT Manager.");
        if (jsonEntry.Length > 16 * 1024 * 1024) throw new InvalidDataException("Le manifeste est trop volumineux.");
        Manifest manifest;
        try
        {
            using var json = jsonEntry.Open();
            using var bounded = new MemoryStream();
            CopyAndHash(json, bounded, jsonEntry.Length); bounded.Position = 0;
            manifest = JsonSerializer.Deserialize<Manifest>(bounded) ?? throw new InvalidDataException("Le manifeste est vide.");
        }
        catch (JsonException exception) { throw new InvalidDataException("Le manifeste est illisible.", exception); }
        if (manifest.Version != 1 || manifest.Files is null || manifest.Directories is null || manifest.Files.Length == 0 ||
            manifest.Files.Length > MaxEntries || manifest.Directories.Length > MaxEntries || zip.Entries.Count != manifest.Files.Length + 1)
            throw new InvalidDataException("Le format ou le contenu de la sauvegarde est invalide.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in manifest.Directories)
        {
            ValidateRelative(directory);
            if (!paths.Add(directory)) throw new InvalidDataException("L’archive contient des dossiers en double.");
        }
        var directories = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in manifest.Files)
        {
            if (file is null) throw new InvalidDataException("Le manifeste contient un fichier invalide.");
            ValidateRelative(file.Path);
            if (!paths.Add(file.Path) || file.Bytes < 0 || file.Bytes > MaxBytes - total || file.Sha256 is null || file.Sha256.Length != 64)
                throw new InvalidDataException("Les fichiers du manifeste sont invalides ou trop volumineux.");
            if (!manifest.IncludesDocuments && ((!file.Path.StartsWith(".ms3dprint-manager/", StringComparison.OrdinalIgnoreCase) && !IsRegistry(file.Path)) || IsTrash(file.Path)))
                throw new InvalidDataException("La sauvegarde sans documents contient un fichier métier inattendu.");
            var entry = zip.GetEntry("workspace/" + file.Path);
            if (entry is null || entry.Length != file.Bytes) throw new InvalidDataException("Un fichier manque ou sa taille est incorrecte.");
            total += file.Bytes;
        }
        foreach (var path in paths)
        {
            for (var slash = path.LastIndexOf('/'); slash >= 0; slash = path.LastIndexOf('/', slash - 1))
                if (!directories.Contains(path[..slash])) throw new InvalidDataException("L’arborescence de la sauvegarde est incohérente.");
        }
        foreach (var file in manifest.Files)
        {
            using var content = zip.GetEntry("workspace/" + file.Path)!.Open();
            if (CopyAndHash(content, Stream.Null, file.Bytes) != file.Sha256) throw new InvalidDataException("Un fichier de la sauvegarde est altéré.");
        }
        return (manifest, new(manifest.CreatedAt, manifest.IncludesDocuments, manifest.Files.Length, total, archiveHash));
    }

    private static Inventory Scan(string root, bool documents)
    {
        var directories = new List<string>(); var files = new List<Source>(); long bytes = 0;
        void AddFile(string path)
        {
            WorkspacePathSafety.EnsureNoLinks(path);
            var name = Path.GetRelativePath(root, path).Replace('\\', '/'); ValidateRelative(name);
            if (name.Equals(".ms3dprint-manager/last-backup.json", StringComparison.OrdinalIgnoreCase)) return;
            var info = new FileInfo(path);
            if (info.Length > MaxBytes - bytes || files.Count >= MaxEntries) throw new IOException("Cette sauvegarde dépasse la limite de 512 Gio ou 100 000 fichiers.");
            bytes += info.Length; files.Add(new(name, info.Length, info.LastWriteTimeUtc));
        }
        var data = Path.Combine(root, "data");
        WorkspacePathSafety.EnsureNoLinks(data);
        if (Directory.Exists(data))
        {
            var registry = Directory.EnumerateFiles(data).Where(p => IsRegistry("data/" + Path.GetFileName(p))).ToArray();
            if (registry.Length > 0) { directories.Add("data"); foreach (var path in registry) AddFile(path); }
        }
        var pending = new Stack<string>();
        foreach (var name in Roots) if (Directory.Exists(Path.Combine(root, name))) pending.Push(Path.Combine(root, name));
        while (pending.TryPop(out var directory))
        {
            WorkspacePathSafety.EnsureNoLinks(directory);
            var relative = Path.GetRelativePath(root, directory).Replace('\\', '/'); ValidateRelative(relative);
            if (!documents && IsTrash(relative)) continue;
            directories.Add(relative);
            if (directories.Count > MaxEntries) throw new IOException("Trop de dossiers pour une seule sauvegarde.");
            foreach (var child in Directory.EnumerateDirectories(directory)) pending.Push(child);
            if (!documents && !relative.StartsWith(".ms3dprint-manager", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var path in Directory.EnumerateFiles(directory)) AddFile(path);
        }
        return new(directories.Order(StringComparer.Ordinal).ToArray(), files.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray());
    }

    private static void ValidateRelative(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2000 || path.Contains('\\')) throw new InvalidDataException("Chemin invalide dans la sauvegarde.");
        var parts = path.Split('/');
        if ((!Roots.Contains(parts[0]) && path != "data" && !IsRegistry(path)) || parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') ||
            p.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || Reserved(p))) throw new InvalidDataException("Chemin non autorisé dans la sauvegarde.");
        if ((path.StartsWith(".ms3dprint-manager/operations/", StringComparison.OrdinalIgnoreCase) || path.StartsWith(".ms3dprint-manager/renames/", StringComparison.OrdinalIgnoreCase)) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Une opération de classement est en attente. Terminez sa récupération dans le logiciel avant de sauvegarder cet espace.");
    }
    private static bool IsRegistry(string path) => path.Equals("data/client-codes.json", StringComparison.OrdinalIgnoreCase) ||
        (path.StartsWith("data/client-codes.previous", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && path.Count(c => c == '/') == 1);
    private static bool IsTrash(string path) => path.Equals(".ms3dprint-manager/trash", StringComparison.OrdinalIgnoreCase) || path.StartsWith(".ms3dprint-manager/trash/", StringComparison.OrdinalIgnoreCase);
    private static bool Reserved(string part)
    {
        var name = part.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" || (name.Length == 4 && (name.StartsWith("COM") || name.StartsWith("LPT")) && name[3] is >= '1' and <= '9');
    }
    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    private static bool Inside(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static IOException Changed() => new("Des fichiers ont changé pendant la sauvegarde. Enregistrez vos modifications et réessayez.");
    private static string CopyAndHash(Stream input, Stream output, long expectedBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long copied = 0; int read;
        while ((read = input.Read(buffer)) != 0)
        {
            if (read > expectedBytes - copied) throw new InvalidDataException("La taille d’un fichier ne correspond pas au manifeste.");
            copied += read; hash.AppendData(buffer, 0, read); output.Write(buffer, 0, read);
        }
        if (copied != expectedBytes) throw new InvalidDataException("Un fichier est incomplet.");
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
